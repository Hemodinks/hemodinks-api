using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Application.Features.Teams;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HemodinksAPI.Tests;

public sealed class TeamLoginSecurityTests
{
    internal static async Task<AuthenticateUserResponse> LoginAsync(HttpClient client, LoginTeam team)
    {
        client.DefaultRequestHeaders.Remove("X-Clinica-Slug");
        client.DefaultRequestHeaders.Add("X-Clinica-Slug", team.Slug);
        var response = await client.PostAsJsonAsync("/api/users/authenticate", new { email = team.Email, senha = TeamLoginFixture.Password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthenticateUserResponse>())!;
    }

    internal static Task<HttpResponseMessage> IdentifyAsync(HttpClient client, string challenge, int operatorId, string? pin = null) =>
        client.PostAsJsonAsync("/api/equipe-auth/identificar", new { token = challenge, operadorId = operatorId, pin });

    [Theory]
    [InlineData("Selecao")]
    [InlineData("Pin")]
    [InlineData("Nenhuma")]
    public async Task AllModes_KeepTeamIdentityAndCanRead(string mode)
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        var fixture = await TeamLoginFixture.SeedAsync(factory.Services);
        var team = mode == "Pin" ? fixture.Pin : mode == "Selecao" ? fixture.Selection : fixture.Anonymous;
        var login = await LoginAsync(client, team);
        if (mode == "Nenhuma") Assert.Null(login.EquipeDesafio);
        else
        {
            Assert.Null(login.Token);
            var op = Assert.Single(login.EquipeDesafio!.Operadores);
            Assert.Equal(team.OperatorId, op.Id);
            Assert.Equal(mode == "Pin", op.ExigePin);
            var response = await IdentifyAsync(client, login.EquipeDesafio.Token, op.Id, mode == "Pin" ? TeamLoginFixture.PinValue : null);
            response.EnsureSuccessStatusCode();
            login = (await response.Content.ReadFromJsonAsync<AuthenticateUserResponse>())!;
        }
        Assert.Equal(team.UserId, login.Id);
        Assert.Equal(Perfil.EquipeId, login.PerfilId);
        Assert.Equal(team.ClinicId, login.ClinicaId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        (await client.GetAsync("/api/events/")).EnsureSuccessStatusCode();
        // The global clinic switch must not strip the authenticated team/operator context.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/session/selecionar-clinica", new { clinicaId = team.ClinicId })).StatusCode);
    }

    [Theory]
    [InlineData("POST", "/api/events/")]
    [InlineData("PUT", "/api/events/1")]
    [InlineData("PATCH", "/api/pacientes/1")]
    [InlineData("DELETE", "/api/events/1")]
    [InlineData("POST", "/api/pacientes/")]
    public async Task AnonymousTeam_CannotWrite(string method, string path)
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        var fixture = await TeamLoginFixture.SeedAsync(factory.Services);
        var login = await LoginAsync(client, fixture.Anonymous);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) };
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("used")]
    [InlineData("team-inactive")]
    [InlineData("user-inactive")]
    [InlineData("member-inactive")]
    [InlineData("operator-inactive")]
    [InlineData("login-inactive")]
    [InlineData("global-inactive")]
    [InlineData("security-version")]
    [InlineData("missing-pin")]
    [InlineData("locked")]
    [InlineData("other-team")]
    [InlineData("other-clinic")]
    [InlineData("wrong-clinic-header")]
    [InlineData("membership-inactive")]
    [InlineData("team-config-changed")]
    public async Task Identification_RejectsInvalidContext(string scenario)
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        var fixture = await TeamLoginFixture.SeedAsync(factory.Services);
        var login = await LoginAsync(client, fixture.Pin);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var challenge = await db.EquipeLoginDesafios.SingleAsync(x => x.EquipeId == fixture.Pin.Id);
            var op = await db.EquipeOperadores.Include(x => x.User).SingleAsync(x => x.Id == fixture.Pin.OperatorId);
            if (scenario == "expired") challenge.ExpiraEm = DateTime.UtcNow.AddMinutes(-1);
            if (scenario == "used") challenge.UtilizadoEm = DateTime.UtcNow;
            if (scenario == "team-inactive") (await db.Equipes.FindAsync(fixture.Pin.Id))!.Ativa = false;
            if (scenario == "team-config-changed") (await db.Equipes.FindAsync(fixture.Pin.Id))!.DataAtualizacao = DateTime.UtcNow.AddSeconds(1);
            if (scenario == "user-inactive") op.User.Ativo = false;
            if (scenario == "member-inactive") (await db.EquipeMembros.SingleAsync(x => x.EquipeId == fixture.Pin.Id)).Ativo = false;
            if (scenario == "operator-inactive") op.Ativo = false;
            if (scenario == "login-inactive") (await db.Users.FindAsync(fixture.Pin.UserId))!.Ativo = false;
            var membership = await db.UsuariosClinicas.Include(x => x.UsuarioGlobal).SingleAsync(x => x.UserId == fixture.Pin.UserId);
            if (scenario == "global-inactive") membership.UsuarioGlobal.Ativo = false;
            if (scenario == "membership-inactive") membership.Ativo = false;
            if (scenario == "security-version") membership.UsuarioGlobal.SecurityVersion = Guid.NewGuid();
            if (scenario == "missing-pin") op.PinHash = null;
            if (scenario == "locked") op.BloqueadoAte = DateTime.UtcNow.AddMinutes(10);
            await db.SaveChangesAsync();
        }
        if (scenario == "wrong-clinic-header")
        {
            client.DefaultRequestHeaders.Remove("X-Clinica-Slug");
            client.DefaultRequestHeaders.Add("X-Clinica-Slug", fixture.Other.Slug);
        }
        var operatorId = scenario == "other-team" ? fixture.Selection.OperatorId
            : scenario == "other-clinic" ? fixture.Other.OperatorId : fixture.Pin.OperatorId;
        var response = await IdentifyAsync(client, login.EquipeDesafio!.Token, operatorId, TeamLoginFixture.PinValue);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PinFailures_LockOperator_AndKeepHttpRateLimit()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        var fixture = await TeamLoginFixture.SeedAsync(factory.Services);
        var login = await LoginAsync(client, fixture.Pin);
        foreach (var invalid in new[] { "000000", "123", "abcdef", "1234567", "999999" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await IdentifyAsync(client, login.EquipeDesafio!.Token, fixture.Pin.OperatorId, invalid)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await IdentifyAsync(client, login.EquipeDesafio!.Token, fixture.Pin.OperatorId, TeamLoginFixture.PinValue)).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var op = await db.EquipeOperadores.FindAsync(fixture.Pin.OperatorId);
            Assert.True(op!.BloqueadoAte > DateTime.UtcNow);
            Assert.Equal(2, op.VersaoSessao);
            scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetCurrent(fixture.Pin.ClinicId, fixture.Pin.Slug);
            var result = await scope.ServiceProvider.GetRequiredService<TeamUseCases>().IdentifyOperatorAsync(
                login.EquipeDesafio!.Token, fixture.Pin.OperatorId, TeamLoginFixture.PinValue, CancellationToken.None);
            Assert.Equal(TeamUseCaseStatus.Unauthorized, result.Status);
        }
    }

    [Fact]
    public async Task ChallengeCannotBeReplayed()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        var fixture = await TeamLoginFixture.SeedAsync(factory.Services);
        var selection = await LoginAsync(client, fixture.Selection);
        (await IdentifyAsync(client, selection.EquipeDesafio!.Token, fixture.Selection.OperatorId)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await IdentifyAsync(client, selection.EquipeDesafio.Token, fixture.Selection.OperatorId)).StatusCode);
    }

    [Fact]
    public async Task TemporaryPin_RequiresChange_AndOldTokenIsInvalidated()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        var fixture = await TeamLoginFixture.SeedAsync(factory.Services);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            (await db.EquipeOperadores.FindAsync(fixture.Pin.OperatorId))!.PrecisaTrocarPin = true;
            await db.SaveChangesAsync();
        }
        var login = await LoginAsync(client, fixture.Pin);
        var response = await IdentifyAsync(client, login.EquipeDesafio!.Token, fixture.Pin.OperatorId, TeamLoginFixture.PinValue);
        response.EnsureSuccessStatusCode();
        var session = (await response.Content.ReadFromJsonAsync<AuthenticateUserResponse>())!;
        Assert.True(session.PrecisaTrocarPin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/events/")).StatusCode);
        var changed = await client.PutAsJsonAsync("/api/equipe-auth/pin", new { pinAtual = TeamLoginFixture.PinValue, novoPin = "294857" });
        changed.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/events/")).StatusCode);
        var newPin = (await changed.Content.ReadFromJsonAsync<ChangeTeamPinResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newPin.Token);
        (await client.GetAsync("/api/events/")).EnsureSuccessStatusCode();
    }
}
