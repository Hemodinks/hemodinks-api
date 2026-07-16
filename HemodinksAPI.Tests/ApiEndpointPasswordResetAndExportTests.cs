using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HemodinksAPI.Api;
using HemodinksAPI.Application.Async;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Domain.Utils;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Tests;

public partial class ApiEndpointIntegrationTests
{
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

}
