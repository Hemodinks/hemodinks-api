using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HemodinksAPI.Api;
using HemodinksAPI.Application.Async;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Domain.Utils;
using HemodinksAPI.Infrastructure.Data;
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
    public async Task ConfiguracoesSistema_AllowsPublicReadAndAdminUpdate()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();

        var getResponse = await client.GetAsync("/api/configuracoes-sistema/current");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        using var getJson = await ReadJsonAsync(getResponse);
        Assert.Equal("Hemodinks", getJson.RootElement.GetProperty("nomeEmpresa").GetString());

        await AuthenticateAsync(client);

        var updateResponse = await client.PutAsJsonAsync("/api/configuracoes-sistema/current", new
        {
            nomeEmpresa = "Clinica Alfa"
        });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        using var updateJson = await ReadJsonAsync(updateResponse);
        Assert.Equal("Clinica Alfa", updateJson.RootElement.GetProperty("nomeEmpresa").GetString());
        Assert.NotEqual(JsonValueKind.Null, updateJson.RootElement.GetProperty("dataAtualizacao").ValueKind);
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
    public async Task PasswordResetRequest_WhenRetriedWithSameIdempotencyKey_ReplaysSameToken()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();

        var key = Guid.NewGuid().ToString("N");

        var firstResponse = await PostAsJsonWithIdempotencyKeyAsync(client, "/api/users/password/reset", key, new
        {
            email = "gmarcone@gmail.com"
        });

        var secondResponse = await PostAsJsonWithIdempotencyKeyAsync(client, "/api/users/password/reset", key, new
        {
            email = "gmarcone@gmail.com"
        });

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal("stored", firstResponse.Headers.GetValues(RequestIdempotencyService.IdempotencyStatusHeaderName).Single());
        Assert.Equal("replayed", secondResponse.Headers.GetValues(RequestIdempotencyService.IdempotencyStatusHeaderName).Single());

        using var firstJson = await ReadJsonAsync(firstResponse);
        using var secondJson = await ReadJsonAsync(secondResponse);

        Assert.Equal(
            firstJson.RootElement.GetProperty("debugToken").GetString(),
            secondJson.RootElement.GetProperty("debugToken").GetString());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, dbContext.PasswordResetTokens.Count());
        Assert.Equal(1, dbContext.IdempotencyRequests.Count(item => item.Operation == "users.password-reset.request"));
    }

    [Fact]
    public async Task PasswordResetConfirm_WhenRetriedWithSameIdempotencyKey_ReplaysSuccess()
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

        var key = Guid.NewGuid().ToString("N");
        var firstConfirm = await PostAsJsonWithIdempotencyKeyAsync(client, "/api/users/password/reset/confirm", key, new
        {
            token,
            novaSenha = "SenhaRetry@123"
        });

        var secondConfirm = await PostAsJsonWithIdempotencyKeyAsync(client, "/api/users/password/reset/confirm", key, new
        {
            token,
            novaSenha = "SenhaRetry@123"
        });

        Assert.Equal(HttpStatusCode.OK, firstConfirm.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondConfirm.StatusCode);
        Assert.Equal("stored", firstConfirm.Headers.GetValues(RequestIdempotencyService.IdempotencyStatusHeaderName).Single());
        Assert.Equal("replayed", secondConfirm.Headers.GetValues(RequestIdempotencyService.IdempotencyStatusHeaderName).Single());

        using var firstJson = await ReadJsonAsync(firstConfirm);
        using var secondJson = await ReadJsonAsync(secondConfirm);

        Assert.Equal(
            firstJson.RootElement.GetProperty("message").GetString(),
            secondJson.RootElement.GetProperty("message").GetString());

        var loginResponse = await client.PostAsJsonAsync("/api/users/authenticate", new
        {
            Email = "gmarcone@gmail.com",
            Senha = "SenhaRetry@123"
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

    [Fact]
    public async Task ExportEndpoint_WhenAuthenticated_QueuesExportJob()
    {
        var fileExportQueue = new CapturingFileExportQueue();
        using var factory = new HemodinksApiFactory(services =>
        {
            var descriptor = services.FirstOrDefault(item => item.ServiceType == typeof(IFileExportQueue));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            services.AddSingleton<IFileExportQueue>(fileExportQueue);
        });
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);

        var response = await client.PostAsJsonAsync("/api/exports/", new
        {
            resource = "faturamentos-medicos",
            format = "xlsx",
            filters = new Dictionary<string, string?>
            {
                ["medico"] = "Dr. Teste"
            }
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        Assert.Equal("queued", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("faturamentos-medicos", json.RootElement.GetProperty("resource").GetString());
        Assert.Equal("xlsx", json.RootElement.GetProperty("format").GetString());
        Assert.Single(fileExportQueue.Messages);
        Assert.Equal("Dr. Teste", fileExportQueue.Messages[0].Filters["medico"]);
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

    private static Task<HttpResponseMessage> PostAsJsonWithIdempotencyKeyAsync(
        HttpClient client,
        string uri,
        string idempotencyKey,
        object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(payload)
        };

        request.Headers.Add(RequestIdempotencyService.IdempotencyKeyHeaderName, idempotencyKey);
        return client.SendAsync(request);
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

    private sealed class CapturingFileExportQueue : IFileExportQueue
    {
        public List<FileExportQueueMessage> Messages { get; } = new();

        public Task EnqueueAsync(FileExportQueueMessage message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }
}
