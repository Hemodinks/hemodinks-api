using System.Security.Cryptography;
using System.Text;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Features.Sessions;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Authentication;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

// #389: familia de refresh tokens no SQL Server real (rowversion nativa, transacao do SaveChanges, ExecuteDelete e FK).
public sealed class RefreshTokenFamilySqlServerTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class BeforeFirstSave(Func<Task> action) : SaveChangesInterceptor
    {
        private int called;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref called) == 1) await action();
            return result;
        }
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static async Task<(string Connection, DbContextOptions<AppDbContext> Options, PlatformDbContext Setup, int GlobalId, User User)> CreateAsync(string name)
    {
        SqlServerTestConnection.RequireConfigured();
        var connection = SqlServerTestConnection.Create($"HemodinksRefreshFamily_{name}_{Guid.NewGuid():N}");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options;
        var setup = new PlatformDbContext(options);
        await setup.Database.EnsureCreatedAsync();
        var user = new User { ClinicaId = Clinica.DefaultId, Nome = "Refresh family", Email = $"family-{name}@example.invalid",
            Telefone = "11999998770", Senha = "test-hash", PerfilId = Perfil.MedicosId, Ativo = true };
        setup.Users.Add(user);
        await setup.SaveChangesAsync();
        var member = await GlobalIdentityService.EnsureForUserAsync(setup, user, default);
        return (connection, options, setup, member.UsuarioGlobalId, user);
    }

    private static AuthenticationSessionService Service(PlatformDbContext db, Clock clock) => new(new EfAuthenticationSessionStore(db),
        new JwtTokenService(new JwtSettings { SecretKey = new string('s', 64), Issuer = "test", Audience = "test", ExpirationMinutes = 30 },
            NullLogger<JwtTokenService>.Instance, clock), new AuthenticationSessionOptions(), clock,
        NullLogger<AuthenticationSessionService>.Instance);

    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task SqlServer_TokenTwoRotationsBack_RevokesSessionAndFamilyHoldsOnlyHashes()
    {
        var (_, options, setup, globalId, user) = await CreateAsync("reuse");
        await using var disposeSetup = setup;
        try
        {
            var clock = new Clock();
            var t0 = (await Service(setup, clock).StartAsync(globalId, user.Id, user.ClinicaId, null, null, default))!.RefreshToken;
            clock.Now = clock.Now.AddMinutes(1);
            string t1, t2;
            await using (var db = new PlatformDbContext(options)) t1 = (await Service(db, clock).RefreshAsync(t0, default))!.RefreshToken;
            clock.Now = clock.Now.AddMinutes(1);
            await using (var db = new PlatformDbContext(options)) t2 = (await Service(db, clock).RefreshAsync(t1, default))!.RefreshToken;

            clock.Now = clock.Now.AddMinutes(1);
            await using (var db = new PlatformDbContext(options)) Assert.Null(await Service(db, clock).RefreshAsync(t0, default));
            await using (var db = new PlatformDbContext(options)) Assert.Null(await Service(db, clock).RefreshAsync(t2, default));

            await using var verify = new PlatformDbContext(options);
            Assert.NotNull((await verify.AuthenticationSessions.SingleAsync()).RevokedAt);
            var family = await verify.AuthenticationSessionRefreshHistory.OrderBy(h => h.Id).Select(h => h.Hash).ToListAsync();
            Assert.Equal(new[] { Hash(t0), Hash(t1) }, family);
            Assert.DoesNotContain(t0, family);
            Assert.DoesNotContain(t1, family);
        }
        finally { await setup.Database.EnsureDeletedAsync(); }
    }

    // Duas abas renovam com o mesmo token: a perdedora recebe conflito (nao 500) e nao deixa linha de familia.
    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task SqlServer_ConcurrentRotation_LoserGetsConflictAndLeavesNoFamilyRow()
    {
        var (connection, options, setup, globalId, user) = await CreateAsync("race");
        await using var disposeSetup = setup;
        try
        {
            var clock = new Clock();
            var t0 = (await Service(setup, clock).StartAsync(globalId, user.Id, user.ClinicaId, null, null, default))!.RefreshToken;
            clock.Now = clock.Now.AddMinutes(1);
            string? winner = null;
            var contested = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(connection)
                .AddInterceptors(new BeforeFirstSave(async () =>
                {
                    await using var competing = new PlatformDbContext(options);
                    winner = (await Service(competing, clock).RefreshAsync(t0, default))!.RefreshToken;
                })).Options;

            await using (var loser = new PlatformDbContext(contested))
                await Assert.ThrowsAsync<SessionRefreshConflictException>(() => Service(loser, clock).RefreshAsync(t0, default));

            await using var verify = new PlatformDbContext(options);
            var session = await verify.AuthenticationSessions.SingleAsync();
            Assert.Null(session.RevokedAt);
            Assert.Equal(Hash(winner!), session.RefreshTokenHash);
            Assert.Equal(1, await verify.AuthenticationSessionRefreshHistory.CountAsync(h => h.Hash == Hash(t0)));
        }
        finally { await setup.Database.EnsureDeletedAsync(); }
    }

    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task SqlServer_RefreshPurgesExpiredFamilyRows_AndDeletingSessionCascades()
    {
        var (_, options, setup, globalId, user) = await CreateAsync("purge");
        await using var disposeSetup = setup;
        try
        {
            var clock = new Clock();
            var t0 = (await Service(setup, clock).StartAsync(globalId, user.Id, user.ClinicaId, null, null, default))!.RefreshToken;
            var sessionId = (await setup.AuthenticationSessions.SingleAsync()).Id;
            setup.AuthenticationSessionRefreshHistory.Add(new AuthenticationSessionRefreshHistory
            {
                SessionId = sessionId, Hash = new string('B', 64), RotatedAt = clock.Now.UtcDateTime.AddHours(-13)
            });
            await setup.SaveChangesAsync();

            clock.Now = clock.Now.AddMinutes(1);
            await using (var db = new PlatformDbContext(options)) Assert.NotNull(await Service(db, clock).RefreshAsync(t0, default));

            await using var verify = new PlatformDbContext(options);
            Assert.Equal(new[] { Hash(t0) }, await verify.AuthenticationSessionRefreshHistory.Select(h => h.Hash).ToListAsync());
            await verify.AuthenticationSessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync();
            Assert.False(await verify.AuthenticationSessionRefreshHistory.AnyAsync());
        }
        finally { await setup.Database.EnsureDeletedAsync(); }
    }

    // Tenant: o token antigo da familia de uma sessao da Clinica A nunca afeta outra sessao. (1) Usuario da
    // Clinica B enviando-o com o proprio par sessao/vinculo: 401, nada revogado. (2) Restauracao sem ids:
    // revoga so a dona, nem a sessao do mesmo usuario em outra clinica.
    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task SqlServer_FamilyTokenFromClinicA_NeverRevokesOrRenewsSessionsOfClinicB()
    {
        var (_, options, setup, globalId, user) = await CreateAsync("tenant");
        await using var disposeSetup = setup;
        try
        {
            var clinicB = new Clinica { Nome = "Clinica B refresh family", Slug = "clinica-b-refresh-family" };
            setup.Clinicas.Add(clinicB);
            await setup.SaveChangesAsync();
            var sameUserInB = new User { ClinicaId = clinicB.Id, Nome = user.Nome, Email = user.Email,
                Telefone = "11999998771", Senha = "test-hash", PerfilId = Perfil.MedicosId, Ativo = true };
            var otherUserInB = new User { ClinicaId = clinicB.Id, Nome = "Outro B", Email = "outro-b@example.invalid",
                Telefone = "11999998772", Senha = "test-hash", PerfilId = Perfil.MedicosId, Ativo = true };
            setup.Users.AddRange(sameUserInB, otherUserInB);
            await setup.SaveChangesAsync();
            var sameMemberB = await GlobalIdentityService.EnsureForUserAsync(setup, sameUserInB, default);
            var otherMemberB = await GlobalIdentityService.EnsureForUserAsync(setup, otherUserInB, default);
            Assert.Equal(globalId, sameMemberB.UsuarioGlobalId);
            Assert.NotEqual(globalId, otherMemberB.UsuarioGlobalId);

            var clock = new Clock();
            var t0A = (await Service(setup, clock).StartAsync(globalId, user.Id, user.ClinicaId, null, null, default))!.RefreshToken;
            var tSameB = (await Service(setup, clock).StartAsync(globalId, sameUserInB.Id, clinicB.Id, null, null, default))!.RefreshToken;
            var tOtherB = (await Service(setup, clock).StartAsync(otherMemberB.UsuarioGlobalId, otherUserInB.Id, clinicB.Id, null, null, default))!.RefreshToken;
            var sessions = await setup.AuthenticationSessions.AsNoTracking().ToListAsync();
            var sessionA = sessions.Single(s => s.UsuarioClinicaId != sameMemberB.Id && s.UsuarioClinicaId != otherMemberB.Id).Id;
            var sessionSameB = sessions.Single(s => s.UsuarioClinicaId == sameMemberB.Id).Id;
            var sessionOtherB = sessions.Single(s => s.UsuarioClinicaId == otherMemberB.Id).Id;

            string t1A;
            clock.Now = clock.Now.AddMinutes(1);
            await using (var db = new PlatformDbContext(options)) t1A = (await Service(db, clock).RefreshAsync(t0A, default))!.RefreshToken;
            clock.Now = clock.Now.AddMinutes(1);
            await using (var db = new PlatformDbContext(options)) Assert.NotNull(await Service(db, clock).RefreshAsync(t1A, default));
            clock.Now = clock.Now.AddMinutes(1);

            await using (var db = new PlatformDbContext(options))
                Assert.Null(await Service(db, clock).RefreshAsync(t0A, default, sessionOtherB, otherMemberB.Id));
            await using (var check = new PlatformDbContext(options))
                Assert.False(await check.AuthenticationSessions.AnyAsync(s => s.RevokedAt != null));

            await using (var db = new PlatformDbContext(options)) Assert.Null(await Service(db, clock).RefreshAsync(t0A, default));
            await using var verify = new PlatformDbContext(options);
            var revoked = await verify.AuthenticationSessions.Where(s => s.RevokedAt != null).Select(s => s.Id).ToListAsync();
            Assert.Equal(new[] { sessionA }, revoked);
            await using (var db = new PlatformDbContext(options)) Assert.NotNull(await Service(db, clock).RefreshAsync(tSameB, default, sessionSameB, sameMemberB.Id));
            await using (var db = new PlatformDbContext(options)) Assert.NotNull(await Service(db, clock).RefreshAsync(tOtherB, default, sessionOtherB, otherMemberB.Id));
        }
        finally { await setup.Database.EnsureDeletedAsync(); }
    }
}
