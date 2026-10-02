using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HemodinksAPI.Tests;

public sealed partial class PasswordResetSecurityEndpointTests
{
    private const string Email = "gmarcone@gmail.com";
    private const string NewPassword = "RecoveredPassword@456";

    [Theory]
    [InlineData("access")]
    [InlineData("refresh")]
    [InlineData("stateless-access")]
    public async Task TokenRecovery_RejectsPreviousCredentials_AndAllowsNewLogin(string credential)
    {
        var sender = new RecordingPasswordResetNotificationSender();
        using var factory = CreateFactory(sender);
        using var client = CreateClient(factory);
        var previous = await Login(client, Email, TestPasswords.Valid);
        var previousAccessToken = previous.Token;
        if (credential == "stateless-access")
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var membership = await db.UsuariosClinicas.Include(x => x.UsuarioGlobal)
                .Include(x => x.User).ThenInclude(x => x.Perfil).Include(x => x.User).ThenInclude(x => x.Clinica)
                .SingleAsync(x => x.Id == previous.MembershipId);
            previousAccessToken = scope.ServiceProvider.GetRequiredService<IJwtTokenService>()
                .GenerateToken(membership.UsuarioGlobal, membership, membership.User, sessionId: null);
            Assert.DoesNotContain(new JwtSecurityTokenHandler().ReadJwtToken(previousAccessToken).Claims, x => x.Type == "sid");
        }
        Assert.Equal(HttpStatusCode.OK, (await GetUsers(client, previousAccessToken)).StatusCode);
        (await client.PostAsJsonAsync("/api/users/password/reset", new { email = Email })).EnsureSuccessStatusCode();
        var token = Assert.Single(sender.Notifications).Token;

        var reset = await client.PostAsJsonAsync("/api/users/password/reset/confirm", new { token, novaSenha = NewPassword });
        reset.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (credential == "refresh"
            ? await Refresh(client, previous)
            : await GetUsers(client, previousAccessToken)).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var global = await db.UsuariosGlobais.SingleAsync(x => x.Id == previous.User.UsuarioGlobalId);
            Assert.NotEqual(previous.Version, global.SecurityVersion);
            Assert.All(await db.AuthenticationSessions.Where(x => x.UsuarioGlobalId == global.Id).ToListAsync(),
                session => Assert.NotNull(session.RevokedAt));
        }

        var oldLogin = await client.PostAsJsonAsync("/api/users/authenticate", new { email = Email, senha = TestPasswords.Valid });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        var current = await Login(client, Email, NewPassword);
        Assert.NotEqual(previous.Version, current.Version);
        Assert.Equal(previous.User.PerfilId, current.User.PerfilId);
        Assert.Equal(previous.User.ClinicaId, current.User.ClinicaId);
        Assert.Equal(previous.User.UsuarioGlobalId, current.User.UsuarioGlobalId);
        Assert.Equal(HttpStatusCode.OK, (await GetUsers(client, current.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Refresh(client, current)).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            Assert.Equal(current.Version, (await db.UsuariosGlobais.SingleAsync(x => x.Id == current.User.UsuarioGlobalId)).SecurityVersion);
            Assert.Equal(current.Version, (await db.AuthenticationSessions.SingleAsync(x => x.Id == current.SessionId)).SecurityVersion);
        }

        var replay = await client.PostAsJsonAsync("/api/users/password/reset/confirm", new { token, novaSenha = "DifferentPassword@789" });
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetUsers(client, current.Token)).StatusCode);
    }

    private static HemodinksApiFactory CreateFactory(RecordingPasswordResetNotificationSender sender) =>
        new(services => services.AddSingleton<IPasswordResetNotificationSender>(sender));

    private static HttpClient CreateClient(HemodinksApiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private sealed record LoginResult(AuthenticateUserResponse User, string Cookie, Guid SessionId, int MembershipId, Guid Version)
    {
        public string Token => User.Token!;
    }

    private static async Task<LoginResult> Login(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/users/authenticate", new { email, senha = password });
        response.EnsureSuccessStatusCode();
        var user = (await response.Content.ReadFromJsonAsync<AuthenticateUserResponse>())!;
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(user.Token);
        var cookie = response.Headers.GetValues("Set-Cookie")
            .Single(x => x.StartsWith("hemodinks_refresh=", StringComparison.Ordinal)).Split(';')[0];
        return new(user, cookie, Guid.Parse(jwt.Claims.Single(x => x.Type == "sid").Value),
            int.Parse(jwt.Claims.Single(x => x.Type == "usuarioClinicaId").Value),
            Guid.Parse(jwt.Claims.Single(x => x.Type == "security_version").Value));
    }

    private static Task<HttpResponseMessage> GetUsers(HttpClient client, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/users/");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> Refresh(HttpClient client, LoginResult login)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/session/renovar")
        {
            Content = JsonContent.Create(new { sessionId = login.SessionId, membershipId = login.MembershipId, active = true })
        };
        request.Headers.Add("Cookie", login.Cookie);
        request.Headers.Add("Origin", "https://hemodinks.gestao-saude.tec.br");
        request.Headers.Add("X-Session-Refresh", "1");
        return client.SendAsync(request);
    }
}
