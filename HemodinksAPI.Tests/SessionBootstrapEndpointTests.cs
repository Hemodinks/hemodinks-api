using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;

namespace HemodinksAPI.Tests;

public sealed partial class SessionRenewalEndpointTests
{
    [Theory]
    [InlineData("https://attacker.example", true)]
    [InlineData("https://hemodinks.gestao-saude.tec.br", false)]
    public async Task BrowserLogin_RejectsCsrfBeforeIssuingCookie(string origin, bool header)
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/users/authenticate")
        { Content = JsonContent.Create(new { email = "gmarcone@gmail.com", senha = TestPasswords.Valid }) };
        request.Headers.Add("Origin", origin);
        if (header) request.Headers.Add("X-Session-Refresh", "1");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData("Pin")]
    [InlineData("Selecao")]
    [InlineData("Nenhuma")]
    public async Task TeamBootstrap_PreservesOperatorAndLogoutRevokesAccess(string mode)
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        var fixture = await TeamLoginFixture.SeedAsync(factory.Services);
        var team = mode == "Pin" ? fixture.Pin : mode == "Selecao" ? fixture.Selection : fixture.Anonymous;
        var login = await TeamLoginSecurityTests.LoginAsync(client, team);
        if (mode != "Nenhuma")
        {
            var identified = await TeamLoginSecurityTests.IdentifyAsync(client, login.EquipeDesafio!.Token,
                team.OperatorId, mode == "Pin" ? TeamLoginFixture.PinValue : null);
            identified.EnsureSuccessStatusCode();
            login = (await identified.Content.ReadFromJsonAsync<AuthenticateUserResponse>())!;
        }
        client.DefaultRequestHeaders.Remove("X-Clinica-Slug");
        client.DefaultRequestHeaders.Add("X-Session-Refresh", "1");
        client.DefaultRequestHeaders.Add("Origin", "https://hemodinks.gestao-saude.tec.br");
        var restoredResponse = await client.PostAsJsonAsync("/api/session/restaurar", new { clinicaId = fixture.Other.ClinicId });
        restoredResponse.EnsureSuccessStatusCode();
        var restored = (await restoredResponse.Content.ReadFromJsonAsync<AuthenticateUserResponse>())!;
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(restored.Token);
        var claims = jwt.Claims.ToDictionary(c => c.Type, c => c.Value);
        Assert.Equal(login.ClinicaId, restored.ClinicaId);
        Assert.Equal(team.Id.ToString(), claims["equipeId"]);
        if (mode != "Nenhuma") Assert.Equal(team.OperatorId.ToString(), claims["equipeOperadorId"]);
        Assert.Equal(mode == "Pin" ? "true" : "false", claims["identificacaoConfiavel"]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", restored.Token);
        var logout = await client.PostAsJsonAsync("/api/session/sair", new { sessionId = claims["sid"],
            membershipId = int.Parse(claims["usuarioClinicaId"]), active = false });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/events/")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/session/restaurar", new { })).StatusCode);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("revoked")]
    [InlineData("inactive")]
    public async Task Bootstrap_RejectsInvalidServerState(string state)
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        var login = await Login(client);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var session = await db.AuthenticationSessions.Include(s => s.UsuarioClinica).SingleAsync(s => s.Id == login.Id);
            if (state == "expired") session.CreatedAt = DateTime.UtcNow.AddHours(-13);
            if (state == "revoked") session.RevokedAt = DateTime.UtcNow;
            if (state == "inactive") session.UsuarioClinica.Ativo = false;
            await db.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Add("X-Session-Refresh", "1");
        var response = await client.PostAsJsonAsync("/api/session/restaurar", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData(true, "https://hemodinks.gestao-saude.tec.br", HttpStatusCode.OK)]
    [InlineData(false, "https://hemodinks.gestao-saude.tec.br", HttpStatusCode.Forbidden)]
    [InlineData(true, "https://attacker.example", HttpStatusCode.Forbidden)]
    public async Task Bootstrap_UsesCookieWithoutClientIdentityAndRejectsCsrf(bool header, string origin, HttpStatusCode expected)
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var login = await Login(client);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/session/restaurar") { Content = JsonContent.Create(new { }) };
        request.Headers.Add("Cookie", login.Cookie);
        request.Headers.Add("Origin", origin);
        if (header) request.Headers.Add("X-Session-Refresh", "1");
        var response = await client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("token").GetString()));
            Assert.True(body.GetProperty("id").GetInt32() > 0);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        }
    }
}
