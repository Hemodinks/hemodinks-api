using System.Net;
using System.Net.Http.Json;

namespace HemodinksAPI.Tests;

public partial class ApiEndpointIntegrationTests
{
    [Theory]
    [InlineData("POST", "/api/convenios-procedimentos-precos/")]
    [InlineData("PUT", "/api/convenios-procedimentos-precos/999999")]
    [InlineData("POST", "/api/faturamentos/999999/contas-receber")]
    public async Task FinanceiroEndpoints_WithMissingRequiredFields_ReturnBadRequest(
        string method,
        string path)
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);

        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(new { })
        };
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateTeam_WithMissingRequiredFields_ReturnsBadRequest()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);

        using var response = await client.PostAsJsonAsync("/api/equipes/", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task IdentifyTeamOperator_WithMissingChallenge_ReturnsBadRequest()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/equipe-auth/identificar", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
