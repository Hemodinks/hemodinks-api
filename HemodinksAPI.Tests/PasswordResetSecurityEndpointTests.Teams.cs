using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Tests;

public sealed partial class PasswordResetSecurityEndpointTests
{
    [Theory]
    [InlineData("Selecao")]
    [InlineData("Pin")]
    [InlineData("Nenhuma")]
    public async Task TokenRecovery_PreservesUnifiedLogin_AndRejectsPreviousTeamSessionAndChallenge(string mode)
    {
        var sender = new RecordingPasswordResetNotificationSender();
        using var factory = CreateFactory(sender);
        using var client = CreateClient(factory);
        var fixture = await TeamLoginFixture.SeedAsync(factory.Services);
        var team = mode == "Pin" ? fixture.Pin : mode == "Selecao" ? fixture.Selection : fixture.Anonymous;
        await AssertLoginContext(client, team, TeamLoginFixture.Password);
        var previous = await LoginTeam(client, team, TeamLoginFixture.Password);
        Assert.Equal(HttpStatusCode.OK, (await TeamRequest(client, previous.Token, "/api/events/")).StatusCode);
        string? pendingChallenge = null;
        if (mode != "Nenhuma")
        {
            var pending = await client.PostAsJsonAsync("/api/users/authenticate", new { email = team.Email, senha = TeamLoginFixture.Password });
            pending.EnsureSuccessStatusCode();
            pendingChallenge = (await pending.Content.ReadFromJsonAsync<AuthenticateUserResponse>())!.EquipeDesafio!.Token;
        }

        (await client.PostAsJsonAsync("/api/users/password/reset", new { email = team.Email })).EnsureSuccessStatusCode();
        var token = Assert.Single(sender.Notifications).Token;
        (await client.PostAsJsonAsync("/api/users/password/reset/confirm", new { token, novaSenha = NewPassword })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await TeamRequest(client, previous.Token, "/api/events/")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(client, previous)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await TeamRequest(client, previous.Token, "/api/session/renovar-equipe")).StatusCode);
        if (pendingChallenge != null)
            Assert.Equal(HttpStatusCode.Unauthorized, (await TeamLoginSecurityTests.IdentifyAsync(client, pendingChallenge,
                team.OperatorId, mode == "Pin" ? TeamLoginFixture.PinValue : null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/users/login-context",
            new { email = team.Email, senha = TeamLoginFixture.Password })).StatusCode);

        await AssertLoginContext(client, team, NewPassword);
        var current = await LoginTeam(client, team, NewPassword);
        Assert.NotEqual(previous.Version, current.Version);
        Assert.Equal(previous.User.UsuarioGlobalId, current.User.UsuarioGlobalId);
        Assert.Equal(previous.MembershipId, current.MembershipId);
        Assert.Equal(team.UserId, current.User.Id);
        Assert.Equal(team.ClinicId, current.User.ClinicaId);
        Assert.Equal(Perfil.EquipeId, current.User.PerfilId);
        var claims = new JwtSecurityTokenHandler().ReadJwtToken(current.Token).Claims.ToArray();
        Assert.Contains(claims, c => c.Type == "equipeId" && c.Value == team.Id.ToString());
        if (mode != "Nenhuma")
            Assert.Contains(claims, c => c.Type == "equipeOperadorId" && c.Value == team.OperatorId.ToString());
        Assert.Equal(HttpStatusCode.OK, (await TeamRequest(client, current.Token, "/api/events/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await TeamRequest(client, current.Token, "/api/session/renovar-equipe")).StatusCode);
    }

    private static async Task AssertLoginContext(HttpClient client, LoginTeam team, string password)
    {
        client.DefaultRequestHeaders.Remove("X-Clinica-Slug");
        var response = await client.PostAsJsonAsync("/api/users/login-context", new { email = team.Email, senha = password });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var clinic = Assert.Single(json.RootElement.GetProperty("clinicas").EnumerateArray());
        Assert.Equal(team.ClinicId, clinic.GetProperty("clinicaId").GetInt32());
        Assert.Equal(team.Slug, clinic.GetProperty("slug").GetString());
        client.DefaultRequestHeaders.Add("X-Clinica-Slug", team.Slug);
    }

    private static async Task<LoginResult> LoginTeam(HttpClient client, LoginTeam team, string password)
    {
        var response = await client.PostAsJsonAsync("/api/users/authenticate", new { email = team.Email, senha = password });
        response.EnsureSuccessStatusCode();
        var user = (await response.Content.ReadFromJsonAsync<AuthenticateUserResponse>())!;
        if (user.EquipeDesafio != null)
        {
            response = await TeamLoginSecurityTests.IdentifyAsync(client, user.EquipeDesafio.Token, team.OperatorId,
                team.Mode == "Pin" ? TeamLoginFixture.PinValue : null);
            response.EnsureSuccessStatusCode();
            user = (await response.Content.ReadFromJsonAsync<AuthenticateUserResponse>())!;
        }
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(user.Token);
        var cookie = response.Headers.GetValues("Set-Cookie")
            .Single(x => x.StartsWith("hemodinks_refresh=", StringComparison.Ordinal)).Split(';')[0];
        return new(user, cookie, Guid.Parse(jwt.Claims.Single(x => x.Type == "sid").Value),
            int.Parse(jwt.Claims.Single(x => x.Type == "usuarioClinicaId").Value),
            Guid.Parse(jwt.Claims.Single(x => x.Type == "security_version").Value));
    }

    private static Task<HttpResponseMessage> TeamRequest(HttpClient client, string token, string path)
    {
        var request = new HttpRequestMessage(path == "/api/events/" ? HttpMethod.Get : HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }
}
