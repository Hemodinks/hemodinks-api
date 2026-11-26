using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HemodinksAPI.Tests;

// #259: handler que falha depois de escrever parte do corpo, com compressão ativa, nunca gera corpo corrompido:
// ou sai uma resposta de erro válida (decodificável), ou a conexão é abortada.
public sealed class ApiResponseCompressionErrorTests : ApiEndpointTestBase
{
    private const string PartialWritePath = "/__test/partial-write-then-fail";

    [Theory]
    [InlineData("br", 64)]
    [InlineData("gzip", 64)]
    [InlineData("br", 512 * 1024)]
    [InlineData("gzip", 512 * 1024)]
    public async Task HandlerFailingAfterPartialWrite_NeverReturnsCorruptedBody(string encoding, int partialBytes)
    {
        using var factory = new HemodinksApiFactory(services =>
            services.AddTransient<IStartupFilter>(_ => new PartialWriteStartupFilter(partialBytes)));
        // Kestrel real: no TestServer a resposta conta como iniciada na primeira escrita, o que esconde o bug
        // (a compressao segura os bytes em buffer e, no Kestrel, HasStarted continua false).
        factory.UseKestrel(0);
        factory.StartServer();
        using var client = new HttpClient(new SocketsHttpHandler { UseCookies = false, AutomaticDecompression = DecompressionMethods.None })
        {
            BaseAddress = new Uri(factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())
        };
        await AuthenticateAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Get, PartialWritePath);
        request.Headers.Add("Accept-Encoding", encoding);

        HttpResponseMessage response;
        byte[] body;
        try
        {
            response = await client.SendAsync(request);
            body = await response.Content.ReadAsByteArrayAsync();
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            return; // Conexão abortada: aceitável, o cliente nunca recebe corpo corrompido.
        }

        using (response)
        {
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            var text = Decode(body, response.Content.Headers.ContentEncoding.SingleOrDefault());
            using var json = JsonDocument.Parse(text);
            Assert.True(json.RootElement.TryGetProperty("message", out _));
            Assert.DoesNotContain("partial", text);
        }
    }

    // Ramo que o Kestrel não força de forma determinística: compressor com bytes só em buffer (HasStarted false).
    [Fact]
    public async Task ExceptionWithCompressionStarted_AbortsInsteadOfWritingErrorOverCompressedStream()
    {
        var lifetime = new AbortProbe();
        var context = NewContext(lifetime);
        var middleware = new HemodinksAPI.Api.ApiExceptionHandlingMiddleware(http =>
        {
            http.Response.Headers.ContentEncoding = "br";
            throw new InvalidOperationException("falha depois da escrita parcial");
        }, Microsoft.Extensions.Logging.Abstractions.NullLogger<HemodinksAPI.Api.ApiExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.True(lifetime.Aborted);
        Assert.Equal(0, context.Response.Body.Length);
    }

    [Fact]
    public async Task ExceptionWithoutCompression_StillReturnsStandardError()
    {
        var lifetime = new AbortProbe();
        var context = NewContext(lifetime);
        var middleware = new HemodinksAPI.Api.ApiExceptionHandlingMiddleware(_ => throw new InvalidOperationException("falha"),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<HemodinksAPI.Api.ApiExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.False(lifetime.Aborted);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.True(context.Response.Body.Length > 0);
    }

    private static DefaultHttpContext NewContext(AbortProbe lifetime)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider(), Response = { Body = new MemoryStream() } };
        context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpRequestLifetimeFeature>(lifetime);
        return context;
    }

    private sealed class AbortProbe : Microsoft.AspNetCore.Http.Features.IHttpRequestLifetimeFeature
    {
        public bool Aborted { get; private set; }
        public CancellationToken RequestAborted { get; set; }
        public void Abort() => Aborted = true;
    }

    private static string Decode(byte[] body, string? encoding)
    {
        using var input = new MemoryStream(body);
        using Stream decoded = encoding switch
        {
            "br" => new BrotliStream(input, CompressionMode.Decompress),
            "gzip" => new GZipStream(input, CompressionMode.Decompress),
            null => input,
            _ => throw new InvalidOperationException(encoding)
        };
        using var reader = new StreamReader(decoded, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // Acrescentado depois do pipeline do Program: só responde a rota de teste (nenhum endpoint casa com ela) e
    // fica dentro dos middlewares de exceção e compressão, como um handler normal.
    private sealed class PartialWriteStartupFilter(int partialBytes) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Path != PartialWritePath)
                {
                    await nextMiddleware(context);
                    return;
                }

                context.Response.ContentType = "application/json";
                var chunk = Encoding.UTF8.GetBytes("{\"items\":[\"partial" + new string('x', Math.Max(0, partialBytes - 20)));
                await context.Response.Body.WriteAsync(chunk);
                throw new InvalidOperationException("falha de serializacao simulada");
            });
        };
    }
}
