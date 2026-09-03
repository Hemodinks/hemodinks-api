using System.Net;
using System.Net.Http.Json;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HemodinksAPI.Tests;

public partial class ApiEndpointIntegrationTests
{
    [Fact]
    public async Task LegalAcceptances_RequireAuthentication()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/legal-acceptances/current");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LegalAcceptances_PersistCurrentVersions_AndRemainIdempotent()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);

        var initialResponse = await client.GetAsync("/api/legal-acceptances/current");
        initialResponse.EnsureSuccessStatusCode();
        using (var initialJson = await ReadJsonAsync(initialResponse))
        {
            Assert.True(initialJson.RootElement.GetProperty("requiresAcceptance").GetBoolean());
        }

        var payload = new { TermsOfUseVersion = "1.1", PrivacyNoticeVersion = "1.1" };
        var firstResponse = await client.PostAsJsonAsync("/api/legal-acceptances/current", payload);
        var secondResponse = await client.PostAsJsonAsync("/api/legal-acceptances/current", payload);
        firstResponse.EnsureSuccessStatusCode();
        secondResponse.EnsureSuccessStatusCode();

        using (var json = await ReadJsonAsync(secondResponse))
        {
            Assert.False(json.RootElement.GetProperty("requiresAcceptance").GetBoolean());
            Assert.True(json.RootElement.GetProperty("termsOfUse").GetProperty("isCurrent").GetBoolean());
            Assert.True(json.RootElement.GetProperty("privacyNotice").GetProperty("isCurrent").GetBoolean());
        }

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<HemodinksAPI.Application.Tenancy.ClinicaContext>().SetPlatformScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var records = await context.UserLegalAcceptances.ToListAsync();
        Assert.Equal(2, records.Count);
        Assert.All(records, item =>
        {
            Assert.Equal(Clinica.DefaultId, item.ClinicaId);
            Assert.Equal("1.1", item.DocumentVersion);
            Assert.Equal(DateTimeKind.Utc, item.AcceptedAtUtc.Kind);
        });
    }

    [Fact]
    public async Task LegalAcceptances_RejectStaleVersions()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client);

        var response = await client.PostAsJsonAsync("/api/legal-acceptances/current", new
        {
            TermsOfUseVersion = "1.0",
            PrivacyNoticeVersion = "1.1"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LegalAcceptances_IgnoreArbitraryIdentityAndTenantFields()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client, Clinica.DefaultSlug);

        var response = await client.PostAsJsonAsync("/api/legal-acceptances/current", new
        {
            TermsOfUseVersion = "1.1",
            PrivacyNoticeVersion = "1.1",
            UserId = int.MaxValue,
            TenantId = int.MaxValue,
            ClinicaId = int.MaxValue
        });
        response.EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<HemodinksAPI.Application.Tenancy.ClinicaContext>().SetPlatformScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var records = await context.UserLegalAcceptances.ToListAsync();

        Assert.Equal(2, records.Count);
        Assert.All(records, item =>
        {
            Assert.Equal(Clinica.DefaultId, item.ClinicaId);
            Assert.NotEqual(int.MaxValue, item.UserId);
        });
    }

    [Fact]
    public async Task LegalAcceptances_AreIsolatedAcrossClinics()
    {
        using var factory = new HemodinksApiFactory();
        using var defaultClient = factory.CreateClient();
        using var betaClient = factory.CreateClient();
        var beta = await SeedClinicaBetaAsync(factory);
        await AuthenticateAsync(defaultClient, Clinica.DefaultSlug);
        await AuthenticateAsync(betaClient, beta.Slug, beta.AdminEmail, beta.AdminPassword);

        var accepted = await defaultClient.PostAsJsonAsync("/api/legal-acceptances/current", new
        {
            TermsOfUseVersion = "1.1",
            PrivacyNoticeVersion = "1.1"
        });
        accepted.EnsureSuccessStatusCode();

        using var defaultStatus = await ReadJsonAsync(
            await defaultClient.GetAsync("/api/legal-acceptances/current"));
        using var betaStatusBefore = await ReadJsonAsync(
            await betaClient.GetAsync("/api/legal-acceptances/current"));

        Assert.False(defaultStatus.RootElement.GetProperty("requiresAcceptance").GetBoolean());
        Assert.True(betaStatusBefore.RootElement.GetProperty("requiresAcceptance").GetBoolean());

        var betaAccepted = await betaClient.PostAsJsonAsync("/api/legal-acceptances/current", new
        {
            TermsOfUseVersion = "1.1",
            PrivacyNoticeVersion = "1.1"
        });
        betaAccepted.EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<HemodinksAPI.Application.Tenancy.ClinicaContext>().SetPlatformScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var recordsByClinic = await context.UserLegalAcceptances
            .GroupBy(item => item.ClinicaId)
            .Select(group => new { ClinicaId = group.Key, Count = group.Count() })
            .ToListAsync();

        Assert.Contains(recordsByClinic, item => item.ClinicaId == Clinica.DefaultId && item.Count == 2);
        Assert.Contains(recordsByClinic, item => item.ClinicaId == beta.Id && item.Count == 2);
    }
}
