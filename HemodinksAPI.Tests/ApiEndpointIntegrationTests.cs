using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HemodinksAPI.Api.Utils;

namespace HemodinksAPI.Tests;

public class ApiEndpointIntegrationTests
{
    [Fact]
    public async Task Healthz_ReturnsOk()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AgendaEndpoint_WithoutToken_ReturnsUnauthorized()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/events/");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AgendaEndpoint_WhenAuthenticated_CreatesAndListsEvent()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);

        var start = DateTime.UtcNow.AddDays(1);
        var createResponse = await client.PostAsJsonAsync("/api/events/", new
        {
            title = "Evento de integracao",
            description = "Criado pelo teste de endpoint",
            start,
            end = start.AddHours(1),
            notifyMedicalProfile = false,
            notifyUser = true,
            reminderPeriodMinutes = 60
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        using var createdJson = await ReadJsonAsync(createResponse);
        var created = createdJson.RootElement;
        Assert.True(created.GetProperty("id").GetInt32() > 0);
        Assert.Equal("Evento de integracao", created.GetProperty("title").GetString());
        Assert.True(created.TryGetProperty("nextReminderAt", out var nextReminderAt));
        Assert.NotEqual(JsonValueKind.Null, nextReminderAt.ValueKind);

        var from = Uri.EscapeDataString(DateTime.UtcNow.AddDays(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTime.UtcNow.AddDays(2).ToString("O"));
        var listResponse = await client.GetAsync($"/api/events/?from={from}&to={to}");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        using var listJson = await ReadJsonAsync(listResponse);
        Assert.Contains(listJson.RootElement.EnumerateArray(), item =>
            item.GetProperty("title").GetString() == "Evento de integracao");
    }

    private static async Task AuthenticateAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/users/authenticate", new
        {
            Email = "gmarcone@gmail.com",
            Senha = DefaultUserPassword.Value
        });

        response.EnsureSuccessStatusCode();

        using var json = await ReadJsonAsync(response);
        var token = json.RootElement.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }
}
