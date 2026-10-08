using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Domain.Models;
using Microsoft.IdentityModel.Tokens;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HemodinksAPI.Tests;

public sealed partial class SessionRenewalEndpointTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyPersistedSession_UsesDatabaseStartWithoutAuthenticationTimeClaim(bool expired)
    {
        var clock = new Clock();
        using var factory = new HemodinksApiFactory(s => s.AddSingleton<TimeProvider>(clock));
        using var client = factory.CreateClient();
        var login = await Login(client);
        var claims = new JwtSecurityTokenHandler().ReadJwtToken(login.Token).Claims
            .Where(c => c.Type is not ("auth_time" or "exp" or "nbf" or "iat"));
        var settings = factory.Services.GetRequiredService<JwtSettings>();
        var signed = new JwtSecurityToken(settings.Issuer, settings.Audience, claims,
            expires: DateTime.UtcNow.AddHours(24), signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SecretKey)), SecurityAlgorithms.HmacSha256));
        if (expired)
        {
            clock.Now = clock.Now.AddHours(12);
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            (await db.AuthenticationSessions.SingleAsync(s => s.Id == login.Id)).LastActivityAt = clock.Now.UtcDateTime;
            await db.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(signed));
        var response = await client.GetAsync("/api/session/clinicas");
        Assert.Equal(expired ? HttpStatusCode.Unauthorized : HttpStatusCode.OK, response.StatusCode);
        if (expired) Assert.Contains("session_absolute_expired", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AbsoluteLimit_ClinicSwitchPreservesSessionStartAndDeadline()
    {
        var clock = new Clock();
        using var factory = new HemodinksApiFactory(s => s.AddSingleton<TimeProvider>(clock));
        using var client = factory.CreateClient();
        factory.Services.GetRequiredService<JwtSettings>().ExpirationMinutes = 1440;
        var login = await Login(client);
        var originalClaims = new JwtSecurityTokenHandler().ReadJwtToken(login.Token).Claims;
        var started = clock.Now;
        int clinicId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var clinic = new Clinica { Nome = "Lifetime isolation", Slug = "lifetime-isolation" };
            db.Clinicas.Add(clinic);
            await db.SaveChangesAsync();
            clinicId = clinic.Id;
        }
        clock.Now = clock.Now.AddMinutes(29);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        var response = await client.PostAsJsonAsync("/api/session/selecionar-clinica", new { clinicaId = clinicId });
        response.EnsureSuccessStatusCode();
        var switched = await response.Content.ReadFromJsonAsync<HemodinksAPI.Application.Features.Sessions.SelectClinicResponse>();
        var claims = new JwtSecurityTokenHandler().ReadJwtToken(switched!.Token).Claims;
        Assert.Equal(originalClaims.Single(c => c.Type == "auth_time").Value, claims.Single(c => c.Type == "auth_time").Value);
        Assert.Equal(login.Id.ToString("D"), claims.Single(c => c.Type == "sid").Value);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var stored = await db.AuthenticationSessions.SingleAsync(s => s.Id == login.Id);
            Assert.Equal(started.UtcDateTime, stored.CreatedAt);
            Assert.Equal(switched.Clinica.UsuarioClinicaId, stored.UsuarioClinicaId);
            clock.Now = started.AddHours(12);
            stored.LastActivityAt = clock.Now.UtcDateTime;
            await db.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", switched.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/session/clinicas")).StatusCode);
    }

    [Theory]
    [InlineData(false, -1, false)]
    [InlineData(false, 0, true)]
    [InlineData(false, 1, true)]
    [InlineData(true, -1, false)]
    [InlineData(true, 0, true)]
    [InlineData(true, 1, true)]
    public async Task AbsoluteLimit_RejectsActiveSessionAtTwelveHours(bool refresh, long ticks, bool expired)
    {
        var clock = new Clock();
        using var factory = new HemodinksApiFactory(s => s.AddSingleton<TimeProvider>(clock));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        factory.Services.GetRequiredService<JwtSettings>().ExpirationMinutes = 1440;
        var login = await Login(client);
        clock.Now = clock.Now.AddHours(12).AddTicks(ticks);
        Assert.True(new JwtSecurityTokenHandler().ReadJwtToken(login.Token).ValidTo > clock.Now.UtcDateTime);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            (await db.AuthenticationSessions.SingleAsync(s => s.Id == login.Id)).LastActivityAt = clock.Now.UtcDateTime;
            await db.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        var response = refresh ? await Refresh(client, login.Id, login.Membership, login.Cookie)
            : await client.GetAsync("/api/session/clinicas");
        Assert.Equal(expired ? HttpStatusCode.Unauthorized : HttpStatusCode.OK, response.StatusCode);
        if (expired)
        {
            Assert.Contains("session_absolute_expired", await response.Content.ReadAsStringAsync());
            Assert.False(response.Headers.Contains("Set-Cookie"));
        }
    }

    [Fact]
    public async Task AbsoluteLimit_RenewalCapsCookieAndPreservesOriginalStart_NewLoginStartsNewWindow()
    {
        var clock = new Clock();
        using var factory = new HemodinksApiFactory(s => s.AddSingleton<TimeProvider>(clock));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var login = await Login(client);
        var started = clock.Now;
        clock.Now = clock.Now.AddMinutes(29);
        var renewed = await Refresh(client, login.Id, login.Membership, login.Cookie);
        renewed.EnsureSuccessStatusCode();
        var cookie = Microsoft.Net.Http.Headers.SetCookieHeaderValue.Parse(renewed.Headers.GetValues("Set-Cookie").Single());
        Assert.InRange((started.AddHours(12) - cookie.Expires!.Value).TotalSeconds, 0, 1);
        using (var scope = factory.Services.CreateScope())
            Assert.Equal(started.UtcDateTime, (await scope.ServiceProvider.GetRequiredService<PlatformDbContext>()
                .AuthenticationSessions.SingleAsync(x => x.Id == login.Id)).CreatedAt);
        clock.Now = started.AddHours(12);
        // Anonymous authentication must remain available even when a browser sends its old bearer.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        var fresh = await Login(client);
        Assert.NotEqual(login.Id, fresh.Id);
        var staleLogout = await Refresh(client, login.Id, login.Membership, fresh.Cookie, path: "sair");
        Assert.Equal(HttpStatusCode.NoContent, staleLogout.StatusCode);
        Assert.False(staleLogout.Headers.Contains("Set-Cookie"));
        using var verify = factory.Services.CreateScope();
        var current = await verify.ServiceProvider.GetRequiredService<PlatformDbContext>().AuthenticationSessions.SingleAsync(x => x.Id == fresh.Id);
        Assert.Equal(clock.Now.UtcDateTime, current.CreatedAt);
        Assert.Null(current.RevokedAt);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("malformed")]
    [InlineData("future")]
    [InlineData("expired")]
    public async Task StatelessTokens_RequireTrustedOriginalAuthenticationTime(string scenario)
    {
        var clock = new Clock();
        using var factory = new HemodinksApiFactory(s => s.AddSingleton<TimeProvider>(clock));
        using var client = factory.CreateClient();
        var login = await Login(client);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(login.Token);
        var claims = jwt.Claims.Where(c => c.Type is not ("sid" or "auth_time" or "exp" or "nbf" or "iat")).ToList();
        if (scenario != "missing") claims.Add(new Claim("auth_time", scenario switch
        {
            "malformed" => "not-a-timestamp",
            "future" => clock.Now.AddMinutes(1).ToUnixTimeSeconds().ToString(),
            _ => clock.Now.AddHours(-12).ToUnixTimeSeconds().ToString()
        }));
        var settings = factory.Services.GetRequiredService<JwtSettings>();
        var signed = new JwtSecurityToken(settings.Issuer, settings.Audience, claims,
            expires: DateTime.UtcNow.AddHours(24), signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SecretKey)), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(signed));
        var response = await client.GetAsync("/api/session/clinicas");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(scenario == "expired" ? "session_absolute_expired" : "session_reauthentication_required", await response.Content.ReadAsStringAsync());
    }
}
