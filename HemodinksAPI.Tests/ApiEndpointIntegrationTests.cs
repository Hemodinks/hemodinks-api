using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HemodinksAPI.Api;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace HemodinksAPI.Tests;

public partial class ApiEndpointIntegrationTests
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
    public async Task AgendaEndpoint_WhenRetriedWithSameIdempotencyKey_ReplaysCreatedEventWithoutDuplicateInsert()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);

        var key = Guid.NewGuid().ToString("N");
        var start = DateTime.UtcNow.AddDays(1);
        var payload = new
        {
            title = "Evento idempotente",
            description = "Criado uma vez e reaproveitado no retry",
            start,
            end = start.AddHours(1),
            notifyMedicalProfile = false,
            notifyUser = true,
            reminderPeriodMinutes = 30
        };

        var firstResponse = await PostAsJsonWithIdempotencyKeyAsync(client, "/api/events/", key, payload);
        var secondResponse = await PostAsJsonWithIdempotencyKeyAsync(client, "/api/events/", key, payload);

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        Assert.Equal("stored", firstResponse.Headers.GetValues(RequestIdempotencyService.IdempotencyStatusHeaderName).Single());
        Assert.Equal("replayed", secondResponse.Headers.GetValues(RequestIdempotencyService.IdempotencyStatusHeaderName).Single());

        using var firstJson = await ReadJsonAsync(firstResponse);
        using var secondJson = await ReadJsonAsync(secondResponse);

        var firstId = firstJson.RootElement.GetProperty("id").GetInt32();
        var secondId = secondJson.RootElement.GetProperty("id").GetInt32();

        Assert.Equal(firstId, secondId);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        scope.ServiceProvider.GetRequiredService<HemodinksAPI.Application.Tenancy.ClinicaContext>().SetPlatformScope();
        Assert.Equal(1, dbContext.Events.Count(item => item.Title == "Evento idempotente"));
        Assert.Equal(1, dbContext.IdempotencyRequests.Count(item => item.Operation == "events.create"));
    }

    [Fact]
    public async Task AgendaEndpoint_WhenSameIdempotencyKeyIsReusedWithDifferentPayload_ReturnsConflict()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);

        var key = Guid.NewGuid().ToString("N");
        var start = DateTime.UtcNow.AddDays(2);

        var firstResponse = await PostAsJsonWithIdempotencyKeyAsync(client, "/api/events/", key, new
        {
            title = "Evento original",
            description = "Primeira versao",
            start,
            end = start.AddHours(1),
            notifyMedicalProfile = false,
            notifyUser = true,
            reminderPeriodMinutes = 45
        });

        var secondResponse = await PostAsJsonWithIdempotencyKeyAsync(client, "/api/events/", key, new
        {
            title = "Evento alterado",
            description = "Payload diferente com a mesma chave",
            start,
            end = start.AddHours(2),
            notifyMedicalProfile = false,
            notifyUser = true,
            reminderPeriodMinutes = 45
        });

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);

        using var secondJson = await ReadJsonAsync(secondResponse);
        Assert.Equal(
            "A mesma Idempotency-Key nao pode ser reutilizada com payload diferente.",
            secondJson.RootElement.GetProperty("message").GetString());
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

}
