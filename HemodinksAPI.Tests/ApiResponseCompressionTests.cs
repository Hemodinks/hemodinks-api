using System.Net;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HemodinksAPI.Api;
using Microsoft.AspNetCore.Http;

namespace HemodinksAPI.Tests;

public sealed class ApiResponseCompressionTests
{
    [Theory]
    [InlineData("br")]
    [InlineData("gzip")]
    public async Task LargeJsonResponse_IsCompressedWithRequestedEncoding(string encoding)
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/swagger/v1/swagger.json");
        request.Headers.Add("Accept-Encoding", encoding);

        var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        Assert.Equal(encoding, Assert.Single(response.Content.Headers.ContentEncoding));
        Assert.Contains("Accept-Encoding", response.Headers.Vary);
    }

    [Fact]
    public async Task JsonResponse_WithoutAcceptEncoding_IsNotCompressed()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.EnsureSuccessStatusCode();
        Assert.Empty(response.Content.Headers.ContentEncoding);
    }

    // http cobre o caso em que o proxy não é reconhecido e a app não enxerga HTTPS.
    [Theory]
    [InlineData("https://localhost")]
    [InlineData("http://localhost")]
    public async Task SessionResponsesWithToken_AreNotCompressed_WhileAuthenticatedJsonIs(string baseAddress)
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri(baseAddress) });
        client.DefaultRequestHeaders.Add("Accept-Encoding", "br, gzip");

        var login = await client.PostAsJsonAsync("/api/users/authenticate", new { Email = "gmarcone@gmail.com", Senha = TestPasswords.Valid });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Empty(login.Content.Headers.ContentEncoding);
        var token = (await ReadJsonAsync(login)).RootElement.GetProperty("token").GetString();
        Assert.False(string.IsNullOrEmpty(token));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var perfis = await client.GetAsync("/api/users/perfis");

        Assert.Equal(HttpStatusCode.OK, perfis.StatusCode);
        Assert.Equal("br", Assert.Single(perfis.Content.Headers.ContentEncoding));

        var claims = new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.ToDictionary(x => x.Type, x => x.Value);
        client.DefaultRequestHeaders.Add("Origin", "https://hemodinks.gestao-saude.tec.br");
        client.DefaultRequestHeaders.Add("X-Session-Refresh", "1");
        var refresh = await client.PostAsJsonAsync("/api/session/renovar", new { sessionId = claims["sid"], membershipId = int.Parse(claims["usuarioClinicaId"]) });

        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.Empty(refresh.Content.Headers.ContentEncoding);
        Assert.False(string.IsNullOrEmpty((await ReadJsonAsync(refresh)).RootElement.GetProperty("token").GetString()));
    }

    // http cobre o caso em que o proxy não é reconhecido e a app não enxerga HTTPS.
    [Theory]
    [InlineData("https://localhost")]
    [InlineData("http://localhost")]
    public async Task PasswordResetJsonResponse_IsNotCompressed(string baseAddress)
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri(baseAddress) });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users/password/reset")
        {
            Content = JsonContent.Create(new { Email = "gmarcone@gmail.com" })
        };
        request.Headers.Add("Accept-Encoding", "br, gzip");

        var response = await client.SendAsync(request);

        // Corpo JSON não vazio: seria comprimido se a rota não estivesse excluída.
        Assert.Contains("json", response.Content.Headers.ContentType?.MediaType ?? string.Empty);
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());
        Assert.Empty(response.Content.Headers.ContentEncoding);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    [Theory]
    [InlineData("/api/session/clinicas", true)]
    [InlineData("/API/Users/Authenticate", true)]
    [InlineData("/api/users/login-context", true)]
    [InlineData("/api/users/42/password", true)]
    [InlineData("/api/users/42/password/reset", true)]
    [InlineData("/api/users/password/temporary/complete", true)]
    [InlineData("/api/equipe-auth/pin", true)]
    [InlineData("/api/equipes/1/operadores/2/pin", true)]
    [InlineData("/api/equipes/1/membros", true)]
    [InlineData("/api/platform/clinicas/1/equipes/2/operadores/3/pin", true)]
    [InlineData("/api/platform/clinicas/1/equipes/2/membros", true)]
    [InlineData("/api/pacientes", false)]
    [InlineData("/api/financeiro/relatorios", false)]
    [InlineData("/api/users", false)]
    [InlineData("/api/users/authenticated-profile", false)]
    [InlineData("/api/pinturas", false)]
    [InlineData("/", false)]
    public void IsCompressionExcluded_MatchesSecretBearingRoutesOnly(string path, bool expected)
    {
        Assert.Equal(expected, ApiServiceCollectionExtensions.IsCompressionExcluded(new PathString(path)));
    }
}
