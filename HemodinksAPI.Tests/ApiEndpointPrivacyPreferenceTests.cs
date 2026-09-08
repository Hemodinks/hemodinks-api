using System.Net;
using System.Net.Http.Json;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HemodinksAPI.Tests;

public partial class ApiEndpointIntegrationTests
{
    [Fact]
    public async Task PrivacyPreferences_RequireAuthentication()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/privacy-preferences/current");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PrivacyPreferences_PersistAndReturnCurrentSelection()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);

        var initial = await client.GetAsync("/api/privacy-preferences/current");
        initial.EnsureSuccessStatusCode();
        using (var initialJson = await ReadJsonAsync(initial))
        {
            Assert.False(initialJson.RootElement.GetProperty("hasPreference").GetBoolean());
            Assert.False(initialJson.RootElement.GetProperty("analyticsEnabled").GetBoolean());
        }

        var updated = await client.PutAsJsonAsync("/api/privacy-preferences/current", new
        {
            DocumentVersion = "1.1",
            PreferencesEnabled = true,
            AnalyticsEnabled = false
        });
        updated.EnsureSuccessStatusCode();

        var reloaded = await client.GetAsync("/api/privacy-preferences/current");
        reloaded.EnsureSuccessStatusCode();
        using (var json = await ReadJsonAsync(reloaded))
        {
            Assert.True(json.RootElement.GetProperty("hasPreference").GetBoolean());
            Assert.Equal("1.1", json.RootElement.GetProperty("documentVersion").GetString());
            Assert.True(json.RootElement.GetProperty("preferencesEnabled").GetBoolean());
            Assert.False(json.RootElement.GetProperty("analyticsEnabled").GetBoolean());
        }

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<HemodinksAPI.Application.Tenancy.ClinicaContext>().SetPlatformScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(await context.UserPrivacyPreferences.ToListAsync());
    }

    [Fact]
    public async Task PrivacyPreferences_RejectStaleVersion()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);

        var response = await client.PutAsJsonAsync("/api/privacy-preferences/current", new
        {
            DocumentVersion = "1.0",
            PreferencesEnabled = true,
            AnalyticsEnabled = true
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PrivacyPreferences_AreIsolatedAcrossClinicsForSameGlobalIdentity()
    {
        using var factory = new HemodinksApiFactory();
        using var defaultClient = factory.CreateClient();
        using var betaClient = factory.CreateClient();
        var beta = await SeedClinicaBetaAsync(factory);
        await AuthenticateAsync(defaultClient, HemodinksAPI.Domain.Models.Clinica.DefaultSlug);
        await AuthenticateAsync(betaClient, beta.Slug, beta.AdminEmail, beta.AdminPassword);

        var defaultUpdate = await defaultClient.PutAsJsonAsync("/api/privacy-preferences/current", new
        {
            DocumentVersion = "1.1",
            PreferencesEnabled = true,
            AnalyticsEnabled = false
        });
        var betaUpdate = await betaClient.PutAsJsonAsync("/api/privacy-preferences/current", new
        {
            DocumentVersion = "1.1",
            PreferencesEnabled = false,
            AnalyticsEnabled = true
        });
        defaultUpdate.EnsureSuccessStatusCode();
        betaUpdate.EnsureSuccessStatusCode();

        using var defaultJson = await ReadJsonAsync(await defaultClient.GetAsync("/api/privacy-preferences/current"));
        using var betaJson = await ReadJsonAsync(await betaClient.GetAsync("/api/privacy-preferences/current"));

        Assert.True(defaultJson.RootElement.GetProperty("preferencesEnabled").GetBoolean());
        Assert.False(defaultJson.RootElement.GetProperty("analyticsEnabled").GetBoolean());
        Assert.False(betaJson.RootElement.GetProperty("preferencesEnabled").GetBoolean());
        Assert.True(betaJson.RootElement.GetProperty("analyticsEnabled").GetBoolean());
    }
}
