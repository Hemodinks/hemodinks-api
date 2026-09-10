using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Tests;

public sealed class LoginContextEndpointTests
{
    [Fact]
    public async Task ValidIndividualCredential_ResolvesSingleClinicWithoutClinicHeader()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/users/login-context", new
        {
            email = "gmarcone@gmail.com",
            senha = TestPasswords.Valid
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var clinics = payload.RootElement.GetProperty("clinicas");
        Assert.Single(clinics.EnumerateArray());
        var clinic = clinics[0];
        Assert.Equal(Clinica.DefaultId, clinic.GetProperty("clinicaId").GetInt32());
        Assert.Equal(Clinica.DefaultSlug, clinic.GetProperty("slug").GetString());
    }

    [Fact]
    public async Task InvalidCredential_DoesNotRevealClinics()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/users/login-context", new
        {
            email = "gmarcone@gmail.com",
            senha = "senha-incorreta"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("clinicaId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("slug", body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("selection")]
    [InlineData("pin")]
    [InlineData("anonymous")]
    public async Task TeamCredential_ResolvesOnlyItsOwnClinic(string mode)
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        var fixture = await TeamLoginFixture.SeedAsync(factory.Services);
        var team = mode switch
        {
            "selection" => fixture.Selection,
            "pin" => fixture.Pin,
            "anonymous" => fixture.Anonymous,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

        var response = await client.PostAsJsonAsync("/api/users/login-context", new
        {
            email = team.Email,
            senha = TeamLoginFixture.Password
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var clinics = payload.RootElement.GetProperty("clinicas");
        Assert.Single(clinics.EnumerateArray());
        var clinic = clinics[0];
        Assert.Equal(team.ClinicId, clinic.GetProperty("clinicaId").GetInt32());
        Assert.Equal(team.Slug, clinic.GetProperty("slug").GetString());
    }

    [Fact]
    public async Task OtherTenantTeamCredential_DoesNotReturnDefaultClinic()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        var fixture = await TeamLoginFixture.SeedAsync(factory.Services);

        var response = await client.PostAsJsonAsync("/api/users/login-context", new
        {
            email = fixture.Other.Email,
            senha = TeamLoginFixture.Password
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var clinics = payload.RootElement.GetProperty("clinicas");
        Assert.Single(clinics.EnumerateArray());
        Assert.Equal(fixture.Other.ClinicId, clinics[0].GetProperty("clinicaId").GetInt32());
        Assert.NotEqual(Clinica.DefaultId, clinics[0].GetProperty("clinicaId").GetInt32());
    }
}
