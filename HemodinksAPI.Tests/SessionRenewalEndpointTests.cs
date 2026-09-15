using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HemodinksAPI.Tests;

public sealed class SessionRenewalEndpointTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static async Task<(Guid Id, int Membership, string Cookie, string Token)> Login(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/users/authenticate", new { email = "gmarcone@gmail.com", senha = TestPasswords.Valid });
        response.EnsureSuccessStatusCode();
        var login = (await response.Content.ReadFromJsonAsync<AuthenticateUserResponse>())!;
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(login.Token);
        return (Guid.Parse(jwt.Claims.Single(c => c.Type == "sid").Value),
            int.Parse(jwt.Claims.Single(c => c.Type == "usuarioClinicaId").Value),
            Cookie(response), login.Token!);
    }

    private static string Cookie(HttpResponseMessage response) => response.Headers.GetValues("Set-Cookie")
        .Single(c => c.StartsWith("hemodinks_refresh=", StringComparison.Ordinal)).Split(';')[0];

    private static Task<HttpResponseMessage> Refresh(HttpClient client, Guid id, int membership, string cookie,
        bool active = true, string origin = "https://hemodinks.gestao-saude.tec.br", bool header = true, string path = "renovar")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/session/{path}")
        { Content = JsonContent.Create(new { sessionId = id, membershipId = membership, active }) };
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("Origin", origin);
        if (header) request.Headers.Add("X-Session-Refresh", "1");
        return client.SendAsync(request);
    }

    [Fact]
    public async Task Active_session_rotates_cookie_extends_activity_and_preserves_membership()
    {
        var clock = new Clock();
        using var factory = new HemodinksApiFactory(s => s.AddSingleton<TimeProvider>(clock));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var login = await Login(client);
        clock.Now = clock.Now.AddMinutes(29);
        var response = await Refresh(client, login.Id, login.Membership, login.Cookie);
        response.EnsureSuccessStatusCode();
        Assert.NotEqual(login.Cookie, Cookie(response));
        Assert.True(response.Headers.CacheControl?.NoStore);
        var renewed = (await response.Content.ReadFromJsonAsync<Renewed>())!;
        var claims = new JwtSecurityTokenHandler().ReadJwtToken(renewed.Token).Claims;
        Assert.Contains(claims, c => c.Type == "sid" && c.Value == login.Id.ToString("D"));
        Assert.Contains(claims, c => c.Type == "usuarioClinicaId" && c.Value == login.Membership.ToString());
        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<PlatformDbContext>().AuthenticationSessions.SingleAsync(s => s.Id == login.Id);
        Assert.Equal(clock.Now.UtcDateTime, stored.LastActivityAt);
        var stale = await Refresh(client, login.Id, login.Membership, login.Cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        Assert.False(stale.Headers.Contains("Set-Cookie"));
        clock.Now = clock.Now.AddMinutes(29);
        (await Refresh(client, login.Id, login.Membership, Cookie(response))).EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData("origin", HttpStatusCode.Forbidden)]
    [InlineData("header", HttpStatusCode.Forbidden)]
    [InlineData("session", HttpStatusCode.Unauthorized)]
    [InlineData("membership", HttpStatusCode.Unauthorized)]
    [InlineData("idle", HttpStatusCode.Unauthorized)]
    [InlineData("revoked", HttpStatusCode.Unauthorized)]
    [InlineData("disabled", HttpStatusCode.Unauthorized)]
    [InlineData("security", HttpStatusCode.Unauthorized)]
    public async Task Invalid_refresh_cannot_revive_or_switch_a_session(string scenario, HttpStatusCode expected)
    {
        var clock = new Clock();
        using var factory = new HemodinksApiFactory(s => s.AddSingleton<TimeProvider>(clock));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var login = await Login(client);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var session = await db.AuthenticationSessions.Include(s => s.UsuarioClinica).ThenInclude(m => m.UsuarioGlobal).SingleAsync(s => s.Id == login.Id);
            if (scenario == "idle") clock.Now = clock.Now.AddMinutes(30);
            if (scenario == "revoked") session.RevokedAt = clock.Now.UtcDateTime;
            if (scenario == "disabled") session.UsuarioClinica.Ativo = false;
            if (scenario == "security") session.UsuarioClinica.UsuarioGlobal.SecurityVersion = Guid.NewGuid();
            await db.SaveChangesAsync();
        }
        var response = await Refresh(client, scenario == "session" ? Guid.NewGuid() : login.Id,
            scenario == "membership" ? login.Membership + 10000 : login.Membership, login.Cookie,
            origin: scenario == "origin" ? "https://attacker.example" : "https://hemodinks.gestao-saude.tec.br", header: scenario != "header");
        Assert.Equal(expected, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Background_refresh_does_not_extend_idle_and_logout_revokes_cookie()
    {
        var clock = new Clock();
        using var factory = new HemodinksApiFactory(s => s.AddSingleton<TimeProvider>(clock));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var login = await Login(client);
        var initial = clock.Now;
        clock.Now = clock.Now.AddMinutes(10);
        var refreshed = await Refresh(client, login.Id, login.Membership, login.Cookie, active: false);
        refreshed.EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope())
            Assert.Equal(initial.UtcDateTime, (await scope.ServiceProvider.GetRequiredService<PlatformDbContext>()
                .AuthenticationSessions.SingleAsync(s => s.Id == login.Id)).LastActivityAt);
        (await Refresh(client, login.Id, login.Membership, Cookie(refreshed), path: "sair")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(client, login.Id, login.Membership, Cookie(refreshed))).StatusCode);
    }

    [Fact]
    public async Task Team_renewal_preserves_identity_and_rejects_revoked_team_version()
    {
        using var factory = new HemodinksApiFactory();
        using var admin = factory.CreateClient();
        var login = await Login(admin);
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(login.Token);
        var clinic = int.Parse(jwt.Claims.Single(c => c.Type == "clinicaId").Value);
        var email = $"renew-team-{Guid.NewGuid():N}@example.com";
        (await admin.PutAsJsonAsync($"/api/platform/clinicas/{clinic}", new {
            novaEquipe = new { nome = "Renew team", email, senha = TestPasswords.Valid, modoIdentificacao = "Nenhuma" }
        })).EnsureSuccessStatusCode();
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/users/authenticate", new { email, senha = TestPasswords.Valid });
        var token = (await response.Content.ReadFromJsonAsync<AuthenticateUserResponse>())!.Token!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var renewed = await client.PostAsJsonAsync("/api/session/renovar-equipe", new { });
        renewed.EnsureSuccessStatusCode();
        var result = (await renewed.Content.ReadFromJsonAsync<Renewed>())!;
        Assert.Contains(new JwtSecurityTokenHandler().ReadJwtToken(result.Token).Claims,
            c => c.Type == "identificacaoConfiavel" && c.Value == "false");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            (await db.Equipes.SingleAsync(t => t.UsuarioLogin.Email == email)).VersaoSessao++;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/session/renovar-equipe", new { })).StatusCode);
    }

    private sealed record Renewed(string Token, int IdleTimeoutMinutes);

    [Fact]
    public async Task Concurrent_activity_returns_retryable_conflict_without_clearing_cookie()
    {
        var failures = 1;
        using var factory = new HemodinksApiFactory(services => services.AddScoped<
            HemodinksAPI.Application.Features.Sessions.IAuthenticationSessionStore>(provider =>
            new ConflictStore(new EfAuthenticationSessionStore(provider.GetRequiredService<PlatformDbContext>()),
                () => Interlocked.Exchange(ref failures, 0) == 1)));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var login = await Login(client);
        var conflict = await Refresh(client, login.Id, login.Membership, login.Cookie);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.False(conflict.Headers.Contains("Set-Cookie"));
        (await Refresh(client, login.Id, login.Membership, login.Cookie)).EnsureSuccessStatusCode();
    }

    private sealed class ConflictStore(HemodinksAPI.Application.Features.Sessions.IAuthenticationSessionStore inner,
        Func<bool> conflict) : HemodinksAPI.Application.Features.Sessions.IAuthenticationSessionStore
    {
        public Task<HemodinksAPI.Domain.Models.UsuarioClinica?> FindActiveMembershipAsync(int globalId, int userId, int clinicId, CancellationToken ct) => inner.FindActiveMembershipAsync(globalId, userId, clinicId, ct);
        public Task<HemodinksAPI.Domain.Models.AuthenticationSession?> FindByRefreshTokenHashAsync(string hash, CancellationToken ct) => inner.FindByRefreshTokenHashAsync(hash, ct);
        public Task<HemodinksAPI.Domain.Models.AuthenticationSession?> FindByIdAsync(Guid id, CancellationToken ct) => inner.FindByIdAsync(id, ct);
        public void Add(HemodinksAPI.Domain.Models.AuthenticationSession session) => inner.Add(session);
        public Task SaveChangesAsync(CancellationToken ct) => inner.SaveChangesAsync(ct);
        public Task<bool> TrySaveChangesAsync(CancellationToken ct) => conflict() ? Task.FromResult(false) : inner.TrySaveChangesAsync(ct);
    }

    [Fact]
    public async Task Foreground_activity_keeps_session_alive_without_rotating_access_token()
    {
        var clock = new Clock();
        using var factory = new HemodinksApiFactory(s => s.AddSingleton<TimeProvider>(clock));
        using var client = factory.CreateClient();
        var login = await Login(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        clock.Now = clock.Now.AddMinutes(29);
        var response = await client.PostAsJsonAsync("/api/session/atividade", new { });
        response.EnsureSuccessStatusCode();
        Assert.False(response.Headers.Contains("Set-Cookie"));
        using var scope = factory.Services.CreateScope();
        Assert.Equal(clock.Now.UtcDateTime, (await scope.ServiceProvider.GetRequiredService<PlatformDbContext>()
            .AuthenticationSessions.SingleAsync(s => s.Id == login.Id)).LastActivityAt);
    }

    [Fact]
    public async Task Valid_refresh_cookie_can_renew_after_access_token_expiration()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var login = await Login(client);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(login.Token);
        var settings = factory.Services.GetRequiredService<HemodinksAPI.Application.Authentication.JwtSettings>();
        var expired = new JwtSecurityToken(jwt.Issuer, jwt.Audiences.Single(),
            jwt.Claims.Where(c => c.Type is not "exp" and not "iat" and not "nbf"),
            DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddMinutes(-1),
            new Microsoft.IdentityModel.Tokens.SigningCredentials(
                new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(settings.SecretKey)),
                Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(expired));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/session/clinicas")).StatusCode);
        (await Refresh(client, login.Id, login.Membership, login.Cookie)).EnsureSuccessStatusCode();
    }
}
