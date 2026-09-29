using System.Net;
using System.Net.Http.Json;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Tests;

public partial class ApiEndpointIntegrationTests
{
    [Fact]
    public async Task AgendaFilters_CombineSearchOwnerStatusAndPeriodWithoutCrossTenantAccess()
    {
        using var factory = new HemodinksApiFactory();
        using var alpha = factory.CreateClient();
        await AuthenticateAsync(alpha, Clinica.DefaultSlug);
        var beta = await SeedClinicaBetaAsync(factory);
        using var betaClient = factory.CreateClient();
        await AuthenticateAsync(betaClient, beta.Slug, beta.AdminEmail, beta.AdminPassword);
        var start = new DateTime(2033, 9, 26, 12, 0, 0, DateTimeKind.Utc);
        var payload = new { title = "Reuniao clinica", description = "Auditoria mensal", start, end = start.AddHours(1) };
        using var ownResponse = await alpha.PostAsJsonAsync("/api/events/", payload);
        ownResponse.EnsureSuccessStatusCode();
        using var own = await ReadJsonAsync(ownResponse);
        var ownId = own.RootElement.GetProperty("id").GetInt32();
        var ownerId = own.RootElement.GetProperty("userId").GetInt32();
        using var foreignResponse = await betaClient.PostAsJsonAsync("/api/events/", payload);
        foreignResponse.EnsureSuccessStatusCode();
        using var foreign = await ReadJsonAsync(foreignResponse);
        var foreignOwnerId = foreign.RootElement.GetProperty("userId").GetInt32();
        var query = $"/api/events/?from=2033-09-01T00:00:00Z&to=2033-09-30T23:59:59Z&search=AUDITORIA&userId={ownerId}";
        using var active = await ReadJsonAsync(await alpha.GetAsync(query + "&isCompleted=false"));
        Assert.Equal(ownId, Assert.Single(active.RootElement.EnumerateArray()).GetProperty("id").GetInt32());
        using var completedBefore = await ReadJsonAsync(await alpha.GetAsync(query + "&isCompleted=true"));
        Assert.Empty(completedBefore.RootElement.EnumerateArray());
        (await alpha.PostAsync($"/api/events/{ownId}/complete", null)).EnsureSuccessStatusCode();
        using var completed = await ReadJsonAsync(await alpha.GetAsync(query + "&isCompleted=true"));
        Assert.Equal(ownId, Assert.Single(completed.RootElement.EnumerateArray()).GetProperty("id").GetInt32());
        using var hidden = await ReadJsonAsync(await alpha.GetAsync($"/api/events/?search=Reuniao&userId={foreignOwnerId}"));
        Assert.Empty(hidden.RootElement.EnumerateArray());
        using var outside = await ReadJsonAsync(await alpha.GetAsync(query.Replace("2033-09", "2033-10")));
        Assert.Empty(outside.RootElement.EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await alpha.GetAsync("/api/events/?userId=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alpha.GetAsync("/api/events/?from=2033-10-01&to=2033-09-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alpha.GetAsync("/api/events/?search=" + new string('a', 201))).StatusCode);
    }
}
