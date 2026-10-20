using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Features.Sessions;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HemodinksAPI.Tests;

public sealed partial class SessionRenewalEndpointTests
{
    [Fact]
    public async Task ClinicSwitch_RejectsPreviousAccessAndRefreshContext_WithoutDestroyingCurrentSession()
    {
        using var factory = new HemodinksApiFactory();
        var target = await SeedClinicRegression(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("X-Clinica-Id", Clinica.DefaultId.ToString());
        var login = await Login(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        var switchedResponse = await client.PostAsJsonAsync("/api/session/selecionar-clinica", new { clinicaId = target.Clinic });
        switchedResponse.EnsureSuccessStatusCode();
        var switched = (await switchedResponse.Content.ReadFromJsonAsync<SelectClinicResponse>())!;
        Assert.Equal(target.Membership, switched.Clinica.UsuarioClinicaId);
        Assert.Equal(login.Id.ToString("D"), new JwtSecurityTokenHandler().ReadJwtToken(switched.Token).Claims.Single(c => c.Type == "sid").Value);

        var staleAccess = await client.GetAsync("/api/users/");
        Assert.Equal(HttpStatusCode.Unauthorized, staleAccess.StatusCode);
        Assert.Contains("session_context_mismatch", await staleAccess.Content.ReadAsStringAsync());
        Assert.False(staleAccess.Headers.Contains("Set-Cookie"));
        var staleRefresh = await Refresh(client, login.Id, login.Membership, login.Cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, staleRefresh.StatusCode);
        Assert.False(staleRefresh.Headers.Contains("Set-Cookie"));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", switched.Token);
        var visible = await client.GetAsync("/api/users/");
        visible.EnsureSuccessStatusCode();
        var body = await visible.Content.ReadAsStringAsync();
        Assert.Contains("remote-only@example.invalid", body);
        Assert.DoesNotContain("local-only@example.invalid", body);
        var currentRefresh = await Refresh(client, login.Id, target.Membership, login.Cookie);
        currentRefresh.EnsureSuccessStatusCode();
        Assert.NotEqual(login.Cookie, Cookie(currentRefresh));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(client, login.Id, target.Membership, login.Cookie)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var saved = await scope.ServiceProvider.GetRequiredService<PlatformDbContext>().AuthenticationSessions.SingleAsync(s => s.Id == login.Id);
        Assert.Equal(target.Membership, saved.UsuarioClinicaId);
        Assert.Null(saved.RevokedAt);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("missing")]
    [InlineData("membership-inactive")]
    [InlineData("user-inactive")]
    [InlineData("clinic-inactive")]
    [InlineData("team")]
    public async Task ClinicSwitch_DeniesUnavailableMembership_AndPreservesOriginalContext(string scenario)
    {
        using var factory = new HemodinksApiFactory();
        var target = await SeedClinicRegression(factory, scenario);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("X-Clinica-Id", Clinica.DefaultId.ToString());
        var login = await Login(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        var denied = await client.PostAsJsonAsync("/api/session/selecionar-clinica", new { clinicaId = scenario == "missing" ? target.Clinic + 10000 : target.Clinic });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.False(denied.Headers.Contains("Set-Cookie"));
        Assert.DoesNotContain("remote-only@example.invalid", await denied.Content.ReadAsStringAsync());
        (await client.GetAsync("/api/users/")).EnsureSuccessStatusCode();
        (await Refresh(client, login.Id, login.Membership, login.Cookie)).EnsureSuccessStatusCode();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var saved = await db.AuthenticationSessions.SingleAsync(s => s.Id == login.Id);
        Assert.Equal(login.Membership, saved.UsuarioClinicaId);
        Assert.Null(saved.RevokedAt);
        Assert.Equal(scenario != "membership-inactive", (await db.UsuariosClinicas.SingleAsync(m => m.Id == target.Membership)).Ativo);
        Assert.Equal(scenario != "user-inactive", (await db.Users.SingleAsync(u => u.Id == target.User)).Ativo);
        Assert.Equal(scenario != "clinic-inactive", (await db.Clinicas.SingleAsync(c => c.Id == target.Clinic)).Ativa);
    }

    [Fact]
    public async Task PasswordChange_RevokesBothClinicSessions_WithoutRevokingOtherIdentity()
    {
        using var factory = new HemodinksApiFactory();
        var target = await SeedClinicRegression(factory);
        using var local = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var remote = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        local.DefaultRequestHeaders.Add("X-Clinica-Id", Clinica.DefaultId.ToString());
        var first = await Login(local);
        remote.DefaultRequestHeaders.Add("X-Clinica-Id", target.Clinic.ToString());
        var second = await Login(remote);
        Assert.Equal(target.Membership, second.Membership);
        var unrelatedResponse = await remote.PostAsJsonAsync("/api/users/authenticate", new { email = "remote-only@example.invalid", senha = TestPasswords.Valid });
        unrelatedResponse.EnsureSuccessStatusCode();
        var unrelated = (await unrelatedResponse.Content.ReadFromJsonAsync<HemodinksAPI.Application.Features.Users.Commands.AuthenticateUserResponse>())!;
        var unrelatedCookie = Cookie(unrelatedResponse);
        var unrelatedClaims = new JwtSecurityTokenHandler().ReadJwtToken(unrelated.Token).Claims.ToDictionary(c => c.Type, c => c.Value);
        var originalVersion = new JwtSecurityTokenHandler().ReadJwtToken(first.Token).Claims.Single(c => c.Type == "security_version").Value;
        local.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.Token);
        var firstClaims = new JwtSecurityTokenHandler().ReadJwtToken(first.Token).Claims;
        var userId = int.Parse(firstClaims.Single(c => c.Type == "nameid").Value);
        const string newPassword = "ChangedAcrossClinics@456";
        var changed = await local.PutAsJsonAsync($"/api/users/{userId}/password", new { senhaAtual = TestPasswords.Valid, novaSenha = newPassword });
        changed.EnsureSuccessStatusCode();
        Assert.False(changed.Headers.Contains("Set-Cookie"));
        foreach (var old in new[] { first, second })
        {
            local.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", old.Token);
            Assert.Equal(HttpStatusCode.Unauthorized, (await local.GetAsync("/api/users/")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(local, old.Id, old.Membership, old.Cookie)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await local.PostAsJsonAsync("/api/session/selecionar-clinica", new { clinicaId = target.Clinic })).StatusCode);
        }
        remote.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", unrelated.Token);
        (await remote.GetAsync("/api/users/")).EnsureSuccessStatusCode();
        (await Refresh(remote, Guid.Parse(unrelatedClaims["sid"]), int.Parse(unrelatedClaims["usuarioClinicaId"]), unrelatedCookie)).EnsureSuccessStatusCode();
        local.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await local.PostAsJsonAsync("/api/users/authenticate", new { email = "gmarcone@gmail.com", senha = TestPasswords.Valid })).StatusCode);
        foreach (var http in new[] { local, remote })
        {
            http.DefaultRequestHeaders.Authorization = null;
            var fresh = await http.PostAsJsonAsync("/api/users/authenticate", new { email = "gmarcone@gmail.com", senha = newPassword });
            fresh.EnsureSuccessStatusCode();
            var identity = (await fresh.Content.ReadFromJsonAsync<HemodinksAPI.Application.Features.Users.Commands.AuthenticateUserResponse>())!;
            Assert.NotEqual(originalVersion, new JwtSecurityTokenHandler().ReadJwtToken(identity.Token).Claims.Single(c => c.Type == "security_version").Value);
        }
    }

    private static async Task<(int Clinic, int Membership, int User)> SeedClinicRegression(HemodinksApiFactory factory, string scenario = "active")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var source = await db.Users.SingleAsync(u => u.Email == "gmarcone@gmail.com");
        source.PerfilId = Perfil.AdministradorId;
        source.PrecisaTrocarSenha = false;
        await db.SaveChangesAsync();
        var sourceMember = await GlobalIdentityService.EnsureForUserAsync(db, source, default);
        sourceMember.PerfilId = Perfil.AdministradorId;
        var clinic = new Clinica { Nome = "Credential regression", Slug = "credential-regression" };
        db.Clinicas.Add(clinic);
        await db.SaveChangesAsync();
        var target = new User { ClinicaId = clinic.Id, Nome = "Remote identity", Email = scenario == "foreign" ? "foreign@example.invalid" : source.Email,
            Telefone = "11999999991", Senha = source.Senha, PerfilId = scenario == "team" ? Perfil.EquipeId : Perfil.AdministradorId,
            Ativo = true, PrecisaTrocarSenha = false };
        db.Users.Add(target);
        await db.SaveChangesAsync();
        var member = await GlobalIdentityService.EnsureForUserAsync(db, target, default);
        foreach (var (email, clinicId) in new[] { ("local-only@example.invalid", source.ClinicaId), ("remote-only@example.invalid", clinic.Id) })
        {
            var user = new User { ClinicaId = clinicId, Nome = email, Email = email, Telefone = "11999999992", Senha = source.Senha,
                PerfilId = Perfil.AdministradorId, Ativo = true, PrecisaTrocarSenha = false };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            await GlobalIdentityService.EnsureForUserAsync(db, user, default);
        }
        if (scenario == "membership-inactive") member.Ativo = false;
        if (scenario == "user-inactive") target.Ativo = false;
        if (scenario == "clinic-inactive") clinic.Ativa = false;
        await db.SaveChangesAsync();
        return (clinic.Id, member.Id, target.Id);
    }
}
