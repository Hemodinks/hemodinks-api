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
    public async Task AuthenticateUser_WhenMultipleClinicasAndNoHintIsProvided_ReturnsBadRequest()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await SeedClinicaBetaAsync(factory);

        var response = await client.PostAsJsonAsync("/api/users/authenticate", new
        {
            Email = "gmarcone@gmail.com",
            Senha = DefaultUserPassword.Value
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        Assert.Equal(
            "Clinica nao resolvida. Envie X-Clinica-Slug ou use um subdominio configurado.",
            json.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task AuthenticateUser_WhenClinicHeaderTargetsDuplicateEmail_ReturnsClinicScopedPayload()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        var beta = await SeedClinicaBetaAsync(factory);

        var response = await PostAsJsonWithClinicHeaderAsync(client, beta.Slug, "/api/users/authenticate", new
        {
            Email = beta.AdminEmail,
            Senha = beta.AdminPassword
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        Assert.Equal(beta.AdminEmail, json.RootElement.GetProperty("email").GetString());
        Assert.Equal(beta.AdminName, json.RootElement.GetProperty("nome").GetString());
        Assert.Equal(beta.Id, json.RootElement.GetProperty("clinicaId").GetInt32());
        Assert.Equal(beta.Slug, json.RootElement.GetProperty("clinicaSlug").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("token").GetString()));
    }

    [Fact]
    public async Task UsersEndpoint_WhenAuthenticatedInSecondClinic_ReturnsOnlyItsOwnUsers()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        var beta = await SeedClinicaBetaAsync(factory);

        await AuthenticateAsync(client, beta.Slug, beta.AdminEmail, beta.AdminPassword);

        var response = await client.GetAsync("/api/users/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        var names = items
            .Select(item => item.GetProperty("nome").GetString())
            .Where(value => value != null)
            .Cast<string>()
            .OrderBy(value => value)
            .ToList();

        Assert.Equal(2, items.Count);
        Assert.Equal([beta.DoctorName, beta.AdminName], names);
        Assert.DoesNotContain("George Marcone Morais dos Santos", names);
    }

    [Fact]
    public async Task AgendaEndpoint_WhenSameIdempotencyKeyIsUsedAcrossClinicas_CreatesOneEventPerClinic()
    {
        using var factory = new HemodinksApiFactory();
        using var clientA = factory.CreateClient();
        using var clientB = factory.CreateClient();
        var beta = await SeedClinicaBetaAsync(factory);

        await AuthenticateAsync(clientA, Clinica.DefaultSlug, "gmarcone@gmail.com", DefaultUserPassword.Value);
        await AuthenticateAsync(clientB, beta.Slug, beta.AdminEmail, beta.AdminPassword);

        var key = Guid.NewGuid().ToString("N");
        var start = DateTime.UtcNow.AddDays(3);
        var payload = new
        {
            title = "Evento multi-clinica",
            description = "Mesmo idempotency key em clinicas diferentes",
            start,
            end = start.AddHours(1),
            notifyMedicalProfile = false,
            notifyUser = true,
            reminderPeriodMinutes = 20
        };

        var responseA = await PostAsJsonWithIdempotencyKeyAsync(clientA, "/api/events/", key, payload);
        var responseB = await PostAsJsonWithIdempotencyKeyAsync(clientB, "/api/events/", key, payload);

        Assert.Equal(HttpStatusCode.Created, responseA.StatusCode);
        Assert.Equal(HttpStatusCode.Created, responseB.StatusCode);
        Assert.Equal("stored", responseA.Headers.GetValues(RequestIdempotencyService.IdempotencyStatusHeaderName).Single());
        Assert.Equal("stored", responseB.Headers.GetValues(RequestIdempotencyService.IdempotencyStatusHeaderName).Single());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, dbContext.Events.Count(item => item.Title == "Evento multi-clinica"));
        Assert.Equal(2, dbContext.IdempotencyRequests.Count(item => item.Operation == "events.create"));
    }

}
