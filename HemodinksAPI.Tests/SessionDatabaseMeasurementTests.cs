using System.Data.Common;
using System.Diagnostics;
using System.Security.Claims;
using HemodinksAPI.Api;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Features.Sessions;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Authentication;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public sealed class SessionDatabaseMeasurementTests(ITestOutputHelper output)
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class SavedRows : SaveChangesInterceptor
    {
        public int Rows;
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
        { Interlocked.Add(ref Rows, result); return ValueTask.FromResult(result); }
    }

    private sealed class Commands : DbCommandInterceptor
    {
        public int Reads, Writes;
        private void Count(DbCommand command)
        {
            // Classify commands only; never retain SQL, parameters, tokens or identities.
            if (command.CommandText.Contains("UPDATE ", StringComparison.OrdinalIgnoreCase)) Interlocked.Increment(ref Writes);
            else Interlocked.Increment(ref Reads);
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        { Count(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        { Count(command); return ValueTask.FromResult(result); }
    }

    [Theory]
    [Trait("Category", "SqlServer")]
    [InlineData(0)]
    [InlineData(30)]
    public async Task MeasureSessionPipeline(int interval)
    {
        if (Environment.GetEnvironmentVariable("HEMODINKS_TEST_LOCALDB") != "1"
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HEMODINKS_TEST_SQLSERVER_CONNECTION_STRING")))
            Assert.Skip("Requires isolated SQL Server.");
        var connection = SqlServerTestConnection.Create($"HemodinksSessionMeasurement_{Guid.NewGuid():N}");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options;
        await using var setup = new PlatformDbContext(options);
        try
        {
            await setup.Database.EnsureCreatedAsync();
            var user = new User { ClinicaId = Clinica.DefaultId, Nome = "Measurement", Email = "measurement@example.invalid",
                Telefone = "11999998766", Senha = "test-hash", PerfilId = Perfil.MedicosId, Ativo = true };
            setup.Users.Add(user);
            await setup.SaveChangesAsync();
            var member = await GlobalIdentityService.EnsureForUserAsync(setup, user, default);
            var clock = new Clock();
            var sessionOptions = new AuthenticationSessionOptions { ActivityPersistenceIntervalSeconds = interval };
            AuthenticationSessionService Service(PlatformDbContext db) => new(new EfAuthenticationSessionStore(db),
                new JwtTokenService(new JwtSettings { SecretKey = new string('s', 64), Issuer = "test", Audience = "test",
                    ExpirationMinutes = 30 }, NullLogger<JwtTokenService>.Instance, clock), sessionOptions, clock,
                NullLogger<AuthenticationSessionService>.Instance);
            var ids = new List<Guid>();
            var refreshTokens = new List<string>();
            for (var i = 0; i < 20; i++)
            {
                var issued = await Service(setup).StartAsync(member.UsuarioGlobalId, user.Id, user.ClinicaId, null, null, default);
                Assert.NotNull(issued);
                refreshTokens.Add(issued.RefreshToken);
                ids.Add(setup.ChangeTracker.Entries<AuthenticationSession>().Last().Entity.Id);
            }
            var otherClinic = new Clinica { Nome = "Measurement destination", Slug = "measurement-destination" };
            setup.Clinicas.Add(otherClinic);
            await setup.SaveChangesAsync();
            var otherUser = new User { ClinicaId = otherClinic.Id, Nome = user.Nome, Email = user.Email,
                Telefone = "11999998770", Senha = "test-hash", PerfilId = Perfil.MedicosId, Ativo = true };
            setup.Users.Add(otherUser);
            await setup.SaveChangesAsync();
            var otherMember = await GlobalIdentityService.EnsureForUserAsync(setup, otherUser, default);
            var teamUser = new User { ClinicaId = user.ClinicaId, Nome = "Measurement team", Email = "measurement-team@example.invalid",
                Telefone = "11999998771", Senha = "test-hash", PerfilId = Perfil.EquipeId, Ativo = true };
            setup.Users.Add(teamUser);
            await setup.SaveChangesAsync();
            var teamMember = await GlobalIdentityService.EnsureForUserAsync(setup, teamUser, default);
            var team = new Equipe { ClinicaId = user.ClinicaId, Nome = "Measurement team", UsuarioLoginId = teamUser.Id,
                ModoIdentificacao = EquipeModosIdentificacao.Pin };
            setup.Equipes.Add(team);
            await setup.SaveChangesAsync();
            setup.EquipeMembros.Add(new EquipeMembro { ClinicaId = user.ClinicaId, EquipeId = team.Id, UserId = user.Id });
            var op = new EquipeOperador { ClinicaId = user.ClinicaId, EquipeId = team.Id, UserId = user.Id, PinHash = "test-hash" };
            setup.EquipeOperadores.Add(op);
            await setup.SaveChangesAsync();
            Assert.NotNull(await Service(setup).StartAsync(teamMember.UsuarioGlobalId, teamUser.Id, teamUser.ClinicaId,
                null, null, default, team: new(team.Id, op.Id, team.VersaoSessao, op.VersaoSessao, true, clock.Now.UtcDateTime)));
            var teamSessionId = setup.ChangeTracker.Entries<AuthenticationSession>().Last().Entity.Id;
            foreach (var scenario in new[] { "sequential", "concurrent-same", "concurrent-distinct",
                "refresh-passive-service", "refresh-active-service", "switch-membership-service", "team-sequential" })
            {
                var commands = new Commands();
                var rows = new SavedRows();
                var measuredOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).AddInterceptors(commands, rows).Options;
                var nextMembership = otherMember.Id;
                async Task Request(Guid id)
                {
                    await using var db = new PlatformDbContext(measuredOptions);
                    if (scenario.StartsWith("refresh-", StringComparison.Ordinal))
                    {
                        var issued = await Service(db).RefreshAsync(refreshTokens[0], default,
                            touchActivity: scenario == "refresh-active-service");
                        Assert.NotNull(issued);
                        refreshTokens[0] = issued.RefreshToken;
                        return;
                    }
                    if (scenario == "switch-membership-service")
                    {
                        Assert.True(await Service(db).ChangeMembershipAsync(id, nextMembership, default));
                        nextMembership = nextMembership == member.Id ? otherMember.Id : member.Id;
                        return;
                    }
                    var identity = scenario == "team-sequential" ? teamUser : user;
                    var membership = scenario == "team-sequential" ? teamMember : member;
                    var claims = new List<Claim> {
                        new(ClaimTypes.NameIdentifier, identity.Id.ToString()), new("sid", (scenario == "team-sequential" ? teamSessionId : id).ToString()),
                        new("usuarioGlobalId", membership.UsuarioGlobalId.ToString()), new("usuarioClinicaId", membership.Id.ToString()),
                        new("clinicaId", identity.ClinicaId.ToString()), new("perfilId", identity.PerfilId.ToString()),
                        new("security_version", membership.UsuarioGlobal.SecurityVersion.ToString()) };
                    if (scenario == "team-sequential") claims.AddRange(new[] {
                        new Claim("equipeId", team.Id.ToString()), new Claim("equipeVersaoSessao", team.VersaoSessao.ToString()),
                        new Claim("equipeOperadorId", op.Id.ToString()), new Claim("operadorVersaoSessao", op.VersaoSessao.ToString()),
                        new Claim("identificacaoConfiavel", "true") });
                    var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
                    http.Request.Path = "/api/session/atividade";
                    var reached = false;
                    var clinic = new ClinicaResolutionMiddleware(_ => { reached = true; return Task.CompletedTask; });
                    var recovery = new PasswordRecoveryMiddleware(ctx => clinic.InvokeAsync(ctx, new ClinicaContext(),
                        new ClinicaResolutionService(db), db, NullLogger<ClinicaResolutionMiddleware>.Instance));
                    var session = new AuthenticationSessionMiddleware(ctx => recovery.InvokeAsync(ctx, db));
                    await session.InvokeAsync(http, Service(db), new SessionLifetimePolicy(sessionOptions, clock));
                    Assert.True(reached, $"Pipeline rejected with {http.Response.StatusCode}");
                }
                // Warm up query compilation outside the counters and latency sample.
                await Request(ids[0]);
                commands.Reads = commands.Writes = 0;
                rows.Rows = 0;
                clock.Now = clock.Now.AddMinutes(1);
                var timer = Stopwatch.StartNew();
                if (scenario.EndsWith("sequential", StringComparison.Ordinal) || scenario.EndsWith("-service", StringComparison.Ordinal))
                    for (var i = 0; i < 20; i++) { clock.Now = clock.Now.AddSeconds(1); await Request(ids[0]); }
                else await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Request(ids[scenario == "concurrent-same" ? 0 : i])));
                timer.Stop();
                if (scenario == "sequential")
                {
                    Assert.Equal(20, commands.Reads);
                    Assert.Equal(interval == 0 ? 20 : 1, commands.Writes);
                    Assert.Equal(commands.Writes, rows.Rows);
                }
                if (scenario == "team-sequential")
                {
                    Assert.Equal(60, commands.Reads);
                    Assert.Equal(interval == 0 ? 20 : 1, commands.Writes);
                }
                output.WriteLine($"interval={interval}; {scenario}: reads={commands.Reads}; updateCommands={commands.Writes}; savedRows={rows.Rows}; elapsedMs={timer.Elapsed.TotalMilliseconds:F2}; requests=20");
            }
        }
        finally { await setup.Database.EnsureDeletedAsync(); }
    }
}
