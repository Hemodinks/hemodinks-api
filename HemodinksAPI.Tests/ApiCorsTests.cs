using System.Net;

namespace HemodinksAPI.Tests;

public sealed class ApiCorsTests
{
    public static TheoryData<string> FrontendOrigins => new()
    {
        "https://hemodinks.gestao-saude.tec.br",
        "https://hemodinks-homologacao.gestao-saude.tec.br"
    };

    [Theory]
    [MemberData(nameof(FrontendOrigins))]
    public async Task PreflightFromCustomDomains_ReturnsAllowedOrigin(string origin)
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/users/authenticate");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type,x-clinica-slug");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowedOrigins));
        Assert.Equal(origin, allowedOrigins.Single());
    }
}
