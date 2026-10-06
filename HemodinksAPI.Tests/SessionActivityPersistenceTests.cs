using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Features.Sessions;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Authentication;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public sealed class SessionActivityPersistenceTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(61)]
    [InlineData(int.MaxValue)]
    public void InvalidIntervalIsRejected(int seconds) => Assert.Throws<InvalidOperationException>(() =>
        SessionLifetimePolicy.ValidateOptions(new() { ActivityPersistenceIntervalSeconds = seconds }));

    [Fact]
    public void ToleranceIsBoundedAndAbsoluteLimitStillWins()
    {
        var clock = new Clock();
        var start = clock.Now;
        var policy = new SessionLifetimePolicy(new() { ActivityPersistenceIntervalSeconds = 30 }, clock);
        clock.Now = start.AddMinutes(30).AddSeconds(29);
        Assert.Null(policy.Failure(start.UtcDateTime, start.UtcDateTime));
        clock.Now = start.AddMinutes(30).AddSeconds(30);
        Assert.Equal(SessionLifetimePolicy.IdleExpired, policy.Failure(start.UtcDateTime, start.UtcDateTime));
        clock.Now = start.AddHours(12);
        Assert.Equal(SessionLifetimePolicy.AbsoluteExpired, policy.Failure(start.UtcDateTime, clock.Now.UtcDateTime));
    }

    [Fact]
    public async Task ActivityIsPersistedAtIntervalAndSurvivesNewContexts()
    {
        await using var fixture = await Fixture.Create();
        var start = fixture.Clock.Now;
        fixture.Clock.Now = start.AddSeconds(29);
        Assert.True((await fixture.Validate()).IsValid);
        Assert.Equal(start.UtcDateTime, (await fixture.Stored()).LastActivityAt);
        fixture.Clock.Now = start.AddSeconds(30);
        Assert.True((await fixture.Validate()).IsValid);
        Assert.Equal(fixture.Clock.Now.UtcDateTime, (await fixture.Stored()).LastActivityAt);
        // A fresh context/service on every request represents process/repllica changes.
        for (var i = 1; i <= 65; i++)
        {
            fixture.Clock.Now = fixture.Clock.Now.AddSeconds(29);
            Assert.True((await fixture.Validate()).IsValid);
        }
        var latest = fixture.Clock.Now;
        fixture.Clock.Now = latest.AddMinutes(30).AddTicks(-1);
        Assert.True((await fixture.Validate()).IsValid);
    }

    [Fact]
    public async Task InactiveSessionCannotBeResurrectedByActivity()
    {
        await using var fixture = await Fixture.Create();
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(30).AddSeconds(30);
        Assert.False((await fixture.Validate()).IsValid);
        Assert.NotNull((await fixture.Stored()).RevokedAt);
        Assert.False((await fixture.Validate()).IsValid);
    }

    [Theory]
    [InlineData("revoked")]
    [InlineData("global")]
    [InlineData("user")]
    [InlineData("membership")]
    [InlineData("clinic")]
    [InlineData("security-version")]
    public async Task SecurityChangesAreReadEvenInsidePersistenceInterval(string change)
    {
        await using var fixture = await Fixture.Create();
        Assert.True((await fixture.Validate()).IsValid);
        await using (var db = new PlatformDbContext(fixture.Options))
        {
            var session = (await new EfAuthenticationSessionStore(db).FindByIdAsync(fixture.Id, default))!;
            switch (change)
            {
                case "revoked": session.RevokedAt = fixture.Clock.Now.UtcDateTime; break;
                case "global": session.UsuarioClinica.UsuarioGlobal.Ativo = false; break;
                case "user": session.UsuarioClinica.User.Ativo = false; break;
                case "membership": session.UsuarioClinica.Ativo = false; break;
                case "clinic": session.UsuarioClinica.Clinica.Ativa = false; break;
                case "security-version": session.UsuarioClinica.UsuarioGlobal.SecurityVersion = Guid.NewGuid(); break;
            }
            await db.SaveChangesAsync();
        }
        fixture.Clock.Now = fixture.Clock.Now.AddSeconds(1);
        Assert.False((await fixture.Validate()).IsValid);
    }

    [Fact]
    public async Task ProfileChangesAreReflectedWithoutWaitingForActivityInterval()
    {
        await using var fixture = await Fixture.Create();
        await using (var db = new PlatformDbContext(fixture.Options))
        {
            var session = (await new EfAuthenticationSessionStore(db).FindByIdAsync(fixture.Id, default))!;
            session.UsuarioClinica.User.PerfilId = Perfil.PacientesId;
            await db.SaveChangesAsync();
        }
        var validation = await fixture.Validate();
        Assert.True(validation.IsValid);
        Assert.Equal(Perfil.PacientesId, validation.PerfilId);
    }

    [Fact]
    public async Task OutOfOrderClockCannotRegressActivity()
    {
        await using var fixture = await Fixture.Create();
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(1);
        Assert.True((await fixture.Validate()).IsValid);
        var latest = fixture.Clock.Now.UtcDateTime;
        fixture.Clock.Now = fixture.Clock.Now.AddSeconds(-10);
        Assert.True((await fixture.Validate()).IsValid);
        Assert.Equal(latest, (await fixture.Stored()).LastActivityAt);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public readonly Clock Clock = new();
        public readonly DbContextOptions<AppDbContext> Options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"session-activity-{Guid.NewGuid()}").Options;
        public Guid Id;
        public AuthenticationSessionService Service(PlatformDbContext db) => new(new EfAuthenticationSessionStore(db),
            new JwtTokenService(new JwtSettings { SecretKey = new string('s', 64), Issuer = "test", Audience = "test",
                ExpirationMinutes = 30 }, NullLogger<JwtTokenService>.Instance, Clock),
            new() { ActivityPersistenceIntervalSeconds = 30 }, Clock, NullLogger<AuthenticationSessionService>.Instance);
        public static async Task<Fixture> Create()
        {
            var fixture = new Fixture();
            await using var db = new PlatformDbContext(fixture.Options);
            await db.Database.EnsureCreatedAsync();
            var user = new User { ClinicaId = Clinica.DefaultId, Nome = "Activity", Email = "activity@example.invalid",
                Telefone = "11999998767", Senha = "test-hash", PerfilId = Perfil.MedicosId, Ativo = true };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            var member = await GlobalIdentityService.EnsureForUserAsync(db, user, default);
            Assert.NotNull(await fixture.Service(db).StartAsync(member.UsuarioGlobalId, user.Id, user.ClinicaId, null, null, default));
            fixture.Id = (await db.AuthenticationSessions.SingleAsync()).Id;
            return fixture;
        }
        public async Task<AuthenticationSessionValidation> Validate()
        {
            await using var db = new PlatformDbContext(Options);
            return await Service(db).ValidateAndTouchAsync(Id, default);
        }
        public async Task<AuthenticationSession> Stored()
        {
            await using var db = new PlatformDbContext(Options);
            return await db.AuthenticationSessions.SingleAsync(s => s.Id == Id);
        }
        public async ValueTask DisposeAsync()
        {
            await using var db = new PlatformDbContext(Options);
            await db.Database.EnsureDeletedAsync();
        }
    }
}
