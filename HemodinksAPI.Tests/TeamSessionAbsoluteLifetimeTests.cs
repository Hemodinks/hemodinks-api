using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Features.Teams;
using HemodinksAPI.Application.Features.Users.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using HemodinksAPI.Infrastructure.Data;

namespace HemodinksAPI.Tests;

public sealed class TeamSessionAbsoluteLifetimeTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Theory]
    [InlineData("Pin")]
    [InlineData("Selecao")]
    [InlineData("Nenhuma")]
    public async Task RenewalAndPinChangePreserveOriginalTeamAuthentication(string mode)
    {
        var clock = new Clock();
        using var factory = new HemodinksApiFactory(s => s.AddSingleton<TimeProvider>(clock));
        using var client = factory.CreateClient();
        factory.Services.GetRequiredService<JwtSettings>().ExpirationMinutes = 1440;
        var fixture = await TeamLoginFixture.SeedAsync(factory.Services);
        var team = mode == "Pin" ? fixture.Pin : mode == "Selecao" ? fixture.Selection : fixture.Anonymous;
        var started = clock.Now.ToUnixTimeSeconds().ToString();
        var login = await TeamLoginSecurityTests.LoginAsync(client, team);
        if (mode != "Nenhuma")
        {
            clock.Now = clock.Now.AddMinutes(2);
            var identification = await TeamLoginSecurityTests.IdentifyAsync(client, login.EquipeDesafio!.Token,
                team.OperatorId, mode == "Pin" ? TeamLoginFixture.PinValue : null);
            Assert.True(identification.IsSuccessStatusCode, await identification.Content.ReadAsStringAsync());
            login = (await identification.Content.ReadFromJsonAsync<AuthenticateUserResponse>())!;
        }
        Assert.Equal(started, AuthenticationTime(login.Token!));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        if (mode == "Pin")
        {
            var pinChange = await client.PutAsJsonAsync("/api/equipe-auth/pin", new { pinAtual = TeamLoginFixture.PinValue, novoPin = "987654" });
            pinChange.EnsureSuccessStatusCode();
            var changed = (await pinChange.Content.ReadFromJsonAsync<ChangeTeamPinResponse>())!;
            Assert.Equal(started, AuthenticationTime(changed.Token));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", changed.Token);
        }
        clock.Now = DateTimeOffset.FromUnixTimeSeconds(long.Parse(started)).AddHours(12).AddTicks(-1);
        // Persistent team sessions now enforce idle time too; simulate continuous activity.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var sid = Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(login.Token).Claims.Single(c => c.Type == "sid").Value);
            (await db.AuthenticationSessions.SingleAsync(s => s.Id == sid)).LastActivityAt = clock.Now.UtcDateTime;
            await db.SaveChangesAsync();
        }
        var renewed = await client.PostAsJsonAsync("/api/session/renovar-equipe", new { });
        renewed.EnsureSuccessStatusCode();
        var token = (await renewed.Content.ReadFromJsonAsync<Renewed>())!.Token;
        Assert.Equal(started, AuthenticationTime(token));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        clock.Now = clock.Now.AddTicks(1);
        var expired = await client.PostAsJsonAsync("/api/session/renovar-equipe", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.Contains("session_absolute_expired", await expired.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/events/")).StatusCode);
    }

    private static string AuthenticationTime(string token) => new JwtSecurityTokenHandler().ReadJwtToken(token)
        .Claims.Single(c => c.Type == "auth_time").Value;
    private sealed record Renewed(string Token);
}
