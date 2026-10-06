using System.Security.Claims;
using HemodinksAPI.Api;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Features.Sessions;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Authentication;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public sealed class SessionActivitySqlServerTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Interleave(Func<Task> action) : SaveChangesInterceptor
    {
        private int called;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref called) == 1) await action();
            return result;
        }
    }

    [Theory]
    [Trait("Category", "SqlServer")]
    [InlineData("newer-touch")]
    [InlineData("revocation")]
    [InlineData("membership")]
    [InlineData("refresh")]
    [InlineData("save-failure")]
    public async Task ActivityRacePreservesCommittedSecurityState(string scenario)
    {
        if (Environment.GetEnvironmentVariable("HEMODINKS_TEST_LOCALDB") != "1"
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HEMODINKS_TEST_SQLSERVER_CONNECTION_STRING")))
            Assert.Skip("Requires isolated SQL Server with native rowversion.");
        var connection = SqlServerTestConnection.Create($"HemodinksActivityRace_{Guid.NewGuid():N}");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options;
        await using var setup = new PlatformDbContext(options);
        try
        {
            await setup.Database.EnsureCreatedAsync();
            var user = new User { ClinicaId = Clinica.DefaultId, Nome = "Activity race", Email = "race@example.invalid",
                Telefone = "11999998768", Senha = "test-hash", PerfilId = Perfil.MedicosId, Ativo = true };
            setup.Users.Add(user);
            await setup.SaveChangesAsync();
            var member = await GlobalIdentityService.EnsureForUserAsync(setup, user, default);
            var clock = new Clock();
            var settings = new AuthenticationSessionOptions { ActivityPersistenceIntervalSeconds = 30 };
            AuthenticationSessionService Service(PlatformDbContext db) => new(new EfAuthenticationSessionStore(db),
                new JwtTokenService(new JwtSettings { SecretKey = new string('s', 64), Issuer = "test", Audience = "test",
                    ExpirationMinutes = 30 }, NullLogger<JwtTokenService>.Instance, clock), settings, clock,
                NullLogger<AuthenticationSessionService>.Instance);
            var issued = await Service(setup).StartAsync(member.UsuarioGlobalId, user.Id, user.ClinicaId, null, null, default);
            Assert.NotNull(issued);
            var original = await setup.AuthenticationSessions.SingleAsync();
            var id = original.Id;
            var started = original.CreatedAt;
            var targetMember = member.Id;
            if (scenario == "membership")
            {
                var clinic = new Clinica { Nome = "Other activity clinic", Slug = "other-activity" };
                setup.Clinicas.Add(clinic);
                await setup.SaveChangesAsync();
                var targetUser = new User { ClinicaId = clinic.Id, Nome = user.Nome, Email = user.Email,
                    Telefone = "11999998769", Senha = "test-hash", PerfilId = Perfil.MedicosId, Ativo = true };
                setup.Users.Add(targetUser);
                await setup.SaveChangesAsync();
                targetMember = (await GlobalIdentityService.EnsureForUserAsync(setup, targetUser, default)).Id;
            }
            clock.Now = clock.Now.AddMinutes(1);
            var requestedAt = clock.Now;
            var interceptor = new Interleave(async () =>
            {
                await using var competing = new PlatformDbContext(options);
                switch (scenario)
                {
                    case "newer-touch":
                        clock.Now = requestedAt.AddSeconds(30);
                        Assert.True((await Service(competing).ValidateAndTouchAsync(id, default)).IsValid);
                        clock.Now = requestedAt; // Older request resumes after the newer write.
                        break;
                    case "revocation":
                        Assert.True(await new EfAuthenticationSessionStore(competing).RevokeByIdAsync(id, clock.Now.UtcDateTime, default));
                        break;
                    case "membership":
                        Assert.True(await Service(competing).ChangeMembershipAsync(id, targetMember, default));
                        break;
                    case "refresh":
                        Assert.NotNull(await Service(competing).RefreshAsync(issued.RefreshToken, default, touchActivity: false));
                        break;
                    case "save-failure": throw new InvalidOperationException("Simulated persistence failure");
                }
            });
            var contestedOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).AddInterceptors(interceptor).Options;
            await using var contested = new PlatformDbContext(contestedOptions);
            var reached = false;
            var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim("sid", id.ToString()), new Claim("usuarioClinicaId", member.Id.ToString()) }, "test")) };
            var middleware = new AuthenticationSessionMiddleware(_ => { reached = true; return Task.CompletedTask; });
            Task Invoke() => middleware.InvokeAsync(http, Service(contested), new SessionLifetimePolicy(settings, clock));
            if (scenario == "save-failure") await Assert.ThrowsAsync<InvalidOperationException>(Invoke);
            else await Invoke();
            Assert.Equal(scenario is "newer-touch" or "refresh", reached);
            await using var verify = new PlatformDbContext(options);
            var saved = await verify.AuthenticationSessions.SingleAsync(s => s.Id == id);
            Assert.Equal(started, saved.CreatedAt);
            Assert.Equal(targetMember, saved.UsuarioClinicaId);
            Assert.Equal(scenario == "revocation", saved.RevokedAt.HasValue);
            if (scenario == "newer-touch") Assert.Equal(requestedAt.AddSeconds(30).UtcDateTime, saved.LastActivityAt);
            if (scenario == "refresh") Assert.Equal(requestedAt.UtcDateTime, saved.LastActivityAt);
            if (scenario == "save-failure") Assert.Equal(original.LastActivityAt, saved.LastActivityAt);
        }
        finally { await setup.Database.EnsureDeletedAsync(); }
    }
}
