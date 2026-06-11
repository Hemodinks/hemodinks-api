using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Domain.Utils;
using Microsoft.Extensions.DependencyInjection;

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
        Assert.True(response.Headers.Contains("X-Request-ID"));

        using var json = await ReadJsonAsync(response);
        Assert.Equal("Healthy", json.RootElement.GetProperty("status").GetString());
        Assert.True(json.RootElement.GetProperty("checks").TryGetProperty("database", out var database));
        Assert.Equal("Healthy", database.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Root_ReturnsHealthCheck()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        Assert.Equal("Healthy", json.RootElement.GetProperty("status").GetString());
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

    [Fact]
    public async Task AgendaEndpoint_WhenEventPayloadIsInvalid_ReturnsBadRequestFromValidationPipeline()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);

        var start = DateTime.UtcNow.AddDays(1);
        var response = await client.PostAsJsonAsync("/api/events/", new
        {
            title = "",
            start,
            end = start.AddHours(1),
            notifyMedicalProfile = false,
            notifyUser = true
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        Assert.Equal("Informe o titulo do evento.", json.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task DashboardSummary_WhenAuthenticated_ReturnsSummary()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);

        var response = await client.GetAsync("/api/dashboard/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        Assert.True(json.RootElement.TryGetProperty("usersCount", out _));
        Assert.True(json.RootElement.TryGetProperty("pacientesCount", out _));
        Assert.True(json.RootElement.TryGetProperty("upcomingEventsCount", out _));
    }

    [Fact]
    public async Task DashboardSummary_WhenReminderProcessorFails_ReturnsSummary()
    {
        using var factory = new HemodinksApiFactory(services =>
        {
            services.AddScoped<IEventReminderProcessor, ThrowingEventReminderProcessor>();
        });
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);

        var response = await client.GetAsync("/api/dashboard/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PasswordResetFlow_WhenTokenIsValid_AllowsAuthenticationWithNewPassword()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();

        var requestResponse = await client.PostAsJsonAsync("/api/users/password/reset", new
        {
            email = "gmarcone@gmail.com"
        });

        Assert.Equal(HttpStatusCode.OK, requestResponse.StatusCode);
        using var requestJson = await ReadJsonAsync(requestResponse);
        var token = requestJson.RootElement.GetProperty("debugToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        var confirmResponse = await client.PostAsJsonAsync("/api/users/password/reset/confirm", new
        {
            token,
            novaSenha = "NovaSenha@123"
        });

        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync("/api/users/authenticate", new
        {
            Email = "gmarcone@gmail.com",
            Senha = "NovaSenha@123"
        });

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    }

    [Fact]
    public async Task PasswordResetRequest_WhenEmailDoesNotExist_ReturnsGenericResponse()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/users/password/reset", new
        {
            email = "nao-existe@email.com"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        Assert.False(json.RootElement.TryGetProperty("debugToken", out var token)
            && token.ValueKind != JsonValueKind.Null);
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

    private sealed class ThrowingEventReminderProcessor : IEventReminderProcessor
    {
        public Task<int> ProcessDueRemindersAsync(CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Falha simulada no processamento de lembretes.");
        }
    }
}
