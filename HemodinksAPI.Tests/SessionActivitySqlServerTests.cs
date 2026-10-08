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
using Microsoft.Extensions.Logging;

namespace HemodinksAPI.Tests;

public sealed class SessionActivitySqlServerTests
{
    private sealed class CaptureLogger : ILogger<AuthenticationSessionMiddleware>
    {
        public readonly List<string> Messages = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Interleave(Func<Task> action, bool repeat = false) : SaveChangesInterceptor
    {
        private int called;
        public int Calls => called;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref called) == 1 || repeat) await action();
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
    [InlineData("repeated-touch")]
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
            var settings = new AuthenticationSessionOptions { ActivityPersistenceIntervalSeconds = scenario == "repeated-touch" ? 0 : 30 };
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
                    case "repeated-touch":
                        // A concurrent request changes rowversion before each attempt commits.
                        await competing.AuthenticationSessions.Where(s => s.Id == id).ExecuteUpdateAsync(
                            setters => setters.SetProperty(s => s.LastActivityAt, requestedAt.UtcDateTime.AddTicks(-1)));
                        break;
                    case "save-failure": throw new InvalidOperationException("Simulated persistence failure");
                }
            }, repeat: scenario == "repeated-touch");
            var contestedOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).AddInterceptors(interceptor).Options;
            await using var contested = new PlatformDbContext(contestedOptions);
            var reached = false;
            var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim("sid", id.ToString()), new Claim("usuarioClinicaId", member.Id.ToString()) }, "test")) };
            http.TraceIdentifier = "session-race-request";
            http.Response.Body = new MemoryStream();
            var logger = new CaptureLogger();
            var middleware = new AuthenticationSessionMiddleware(_ => { reached = true; return Task.CompletedTask; }, logger);
            Task Invoke() => middleware.InvokeAsync(http, Service(contested), new SessionLifetimePolicy(settings, clock));
            if (scenario == "save-failure") await Assert.ThrowsAsync<InvalidOperationException>(Invoke);
            else await Invoke();
            Assert.Equal(scenario is "newer-touch" or "refresh", reached);
            if (scenario is "repeated-touch" or "membership" or "revocation")
            {
                http.Response.Body.Position = 0;
                using var failure = await System.Text.Json.JsonDocument.ParseAsync(http.Response.Body);
                Assert.Equal(http.TraceIdentifier, failure.RootElement.GetProperty("requestId").GetString());
                var code = failure.RootElement.GetProperty("code").GetString();
                Assert.Equal(scenario == "repeated-touch" ? "session_validation_busy"
                    : scenario == "membership" ? "session_context_mismatch" : "session_invalid", code);
                var log = Assert.Single(logger.Messages);
                Assert.Contains(code!, log);
                Assert.Contains(http.TraceIdentifier, log);
                Assert.DoesNotContain(id.ToString(), log);
                Assert.DoesNotContain(issued.RefreshToken, log);
                Assert.DoesNotContain(issued.AccessToken, log);
                Assert.False(http.Response.Headers.ContainsKey("Set-Cookie"));
                if (scenario != "repeated-touch") Assert.Equal(StatusCodes.Status401Unauthorized, http.Response.StatusCode);
            }
            if (scenario == "repeated-touch")
            {
                Assert.Equal(3, interceptor.Calls);
                Assert.Equal(StatusCodes.Status503ServiceUnavailable, http.Response.StatusCode);
                Assert.Equal("1", http.Response.Headers.RetryAfter.ToString());
                Assert.Equal("no-store", http.Response.Headers.CacheControl.ToString());
                http.Response.Body.Position = 0;
                using var body = await System.Text.Json.JsonDocument.ParseAsync(http.Response.Body);
                Assert.Equal("session_validation_busy", body.RootElement.GetProperty("code").GetString());
                Assert.False(http.Response.Headers.ContainsKey("Set-Cookie"));
                // Once contention stops, the same session can authorize a new request.
                await using var retry = new PlatformDbContext(options);
                Assert.True((await Service(retry).ValidateAndTouchAsync(id, default)).IsValid);
            }
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
