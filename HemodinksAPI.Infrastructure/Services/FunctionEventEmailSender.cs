using System.Net.Http.Json;
using HemodinksAPI.Application.Services;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Infrastructure.Services;

public sealed class FunctionEventEmailSender(IHttpClientFactory clients, IOptions<EventEmailFunctionOptions> options) : IEventEmailSender
{
    public async Task SendAsync(EventEmailMessage message, CancellationToken cancellationToken)
    {
        if (!message.IsValid()) throw new InvalidOperationException("Mensagem de email invalida.");
        var configuration = options.Value;
        if (!Uri.TryCreate(configuration.BaseUrl?.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || string.IsNullOrWhiteSpace(configuration.FunctionKey))
            throw new InvalidOperationException("Envio por Function indisponivel.");
        var baseUrl = uri.AbsoluteUri.TrimEnd('/');
        if (!baseUrl.EndsWith("/api", StringComparison.OrdinalIgnoreCase)) baseUrl += "/api";
        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/event-email/send");
        request.Headers.Add("x-functions-key", configuration.FunctionKey.Trim());
        request.Content = JsonContent.Create(message);
        using var client = clients.CreateClient(nameof(FunctionEventEmailSender));
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode != System.Net.HttpStatusCode.OK)
            throw new InvalidOperationException("Falha no envio de email pela Function.");
    }
}
