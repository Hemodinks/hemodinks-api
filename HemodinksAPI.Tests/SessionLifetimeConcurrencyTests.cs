using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Features.Sessions;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Authentication;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public sealed class SessionLifetimeConcurrencyTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Theory]
    [Trait("Category", "SqlServer")]
    [InlineData("logout")]
    [InlineData("deadline")]
    [InlineData("activity")]
    [InlineData("deadline-during-save")]
    public async Task ConcurrentRevocationOrDeadlineCannotResurrectSession(string scenario)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HEMODINKS_TEST_SQLSERVER_CONNECTION_STRING"))
            && Environment.GetEnvironmentVariable("HEMODINKS_TEST_LOCALDB") != "1")
            Assert.Skip("Configure an isolated SQL Server to verify native rowversion concurrency.");
        var databaseName = $"HemodinksSessionLifetime_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(SqlServerTestConnection.Create(databaseName)).Options;
        await using var setup = new PlatformDbContext(options);
        try
        {
            await setup.Database.EnsureCreatedAsync();
            var user = new User { ClinicaId = Clinica.DefaultId, Nome = "Session concurrency",
                Email = "session-concurrency@example.com", Telefone = "11999998765", Senha = "test-hash",
                PerfilId = Perfil.MedicosId, Ativo = true };
            setup.Users.Add(user);
            await setup.SaveChangesAsync();
            var member = await GlobalIdentityService.EnsureForUserAsync(setup, user, default);
            var clock = new Clock();
            var initial = await Service(new EfAuthenticationSessionStore(setup), clock)
                .StartAsync(member.UsuarioGlobalId, user.Id, user.ClinicaId, null, null, default);
            Assert.NotNull(initial);
            var sessionId = (await setup.AuthenticationSessions.SingleAsync()).Id;
            var originalStart = clock.Now.UtcDateTime;

            await using var first = new PlatformDbContext(options);
            await using var second = new PlatformDbContext(options);
            var secondStore = new EfAuthenticationSessionStore(second);
            var contestedStore = new InterleavingStore(new EfAuthenticationSessionStore(first), async () =>
            {
                if (scenario.StartsWith("deadline", StringComparison.Ordinal)) clock.Now = clock.Now.AddHours(12);
                // Native SQL Server rowversion changes on this atomic update.
                if (scenario != "deadline-during-save")
                    await secondStore.RevokeByIdAsync(sessionId, clock.Now.UtcDateTime, default);
            });
            clock.Now = clock.Now.AddMinutes(1);
            var contested = Service(contestedStore, clock);
            if (scenario == "activity")
            {
                var result = await contested.ValidateAndTouchAsync(sessionId, default);
                Assert.False(result.IsValid);
            }
            else if (scenario.StartsWith("deadline", StringComparison.Ordinal))
                await Assert.ThrowsAsync<SessionAbsoluteExpiredException>(() => contested.RefreshAsync(initial.RefreshToken, default));
            else
                await Assert.ThrowsAsync<SessionRefreshConflictException>(() => contested.RefreshAsync(initial.RefreshToken, default));

            // Failed optimistic writes must also be removed from the request's unit of work.
            await first.SaveChangesAsync();
            await using var verify = new PlatformDbContext(options);
            var stored = await verify.AuthenticationSessions.SingleAsync();
            Assert.NotNull(stored.RevokedAt);
            Assert.Equal(originalStart, stored.CreatedAt);
            Assert.Null(await Service(new EfAuthenticationSessionStore(verify), clock)
                .RefreshAsync(initial.RefreshToken, default));
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    private static AuthenticationSessionService Service(IAuthenticationSessionStore store, TimeProvider clock) => new(
        store, new JwtTokenService(new JwtSettings { SecretKey = new string('s', 64), Issuer = "test", Audience = "test",
            ExpirationMinutes = 30 }, NullLogger<JwtTokenService>.Instance, clock), new(), clock,
        NullLogger<AuthenticationSessionService>.Instance);

    private sealed class InterleavingStore(IAuthenticationSessionStore inner, Func<Task> beforeSave) : IAuthenticationSessionStore
    {
        public Task<UsuarioClinica?> FindMembershipAsync(int globalId, int membershipId, CancellationToken ct) => inner.FindMembershipAsync(globalId, membershipId, ct);
        public Task<UsuarioClinica?> FindActiveMembershipAsync(int globalId, int userId, int clinicId, CancellationToken ct) => inner.FindActiveMembershipAsync(globalId, userId, clinicId, ct);
        public Task<AuthenticationSession?> FindByRefreshTokenHashAsync(string hash, CancellationToken ct) => inner.FindByRefreshTokenHashAsync(hash, ct);
        public Task<AuthenticationSession?> FindByIdAsync(Guid id, CancellationToken ct) => inner.FindByIdAsync(id, ct);
        public Task<HemodinksAPI.Application.Features.Sessions.SessionTeamBinding?> FindTeamBindingAsync(HemodinksAPI.Domain.Models.AuthenticationSession session, DateTime now, CancellationToken ct) => inner.FindTeamBindingAsync(session, now, ct);
        public void Add(AuthenticationSession session) => inner.Add(session);
        public Task SaveChangesAsync(CancellationToken ct) => inner.SaveChangesAsync(ct);
        public async Task<bool> TrySaveChangesAsync(CancellationToken ct) { await beforeSave(); return await inner.TrySaveChangesAsync(ct); }
        public Task<bool> RevokeByIdAsync(Guid id, DateTime at, CancellationToken ct) => inner.RevokeByIdAsync(id, at, ct);
    }
}

