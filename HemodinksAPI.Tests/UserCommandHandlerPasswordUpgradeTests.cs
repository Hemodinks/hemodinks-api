using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Application.Utils;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public partial class UserCommandHandlerTests
{
    [Theory]
    [InlineData(10_000)]
    [InlineData(210_000)]
    [InlineData(600_000)]
    [InlineData(800_000)]
    public async Task Authenticate_UpgradesOnlyWeakCanonicalHashAndPreservesSessionsAndLocalCopies(int iterations)
    {
        await using var context = TestDbContextFactory.Create();
        var oldHash = PasswordHashTestData.Create(TestPasswords.Valid, iterations);
        var user = CreateUser("upgrade@example.com", oldHash);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var membership = await GlobalIdentityService.EnsureForUserAsync(context, user, default);
        var global = membership.UsuarioGlobal;
        var version = global.SecurityVersion;
        var updatedAt = global.DataAtualizacao;
        var session = new AuthenticationSession { Id = Guid.NewGuid(), UsuarioGlobal = global,
            UsuarioClinica = membership, SecurityVersion = version, RefreshTokenHash = "session-test-hash" };
        context.AuthenticationSessions.Add(session);
        await context.SaveChangesAsync();
        var hasher = new UpgradeCountingHasher();
        var handler = new AuthenticateUserCommandHandler(context, hasher, new StubJwtTokenService("token"),
            CreateLicencaService(context), NullLogger<AuthenticateUserCommandHandler>.Instance);
        var request = new AuthenticateUserCommand { Email = user.Email, Senha = TestPasswords.Valid };

        var response = await handler.Handle(request, default);
        Assert.Equal("token", response.Token);
        Assert.Equal(iterations < 600_000 ? 1 : 0, hasher.Hashes);
        Assert.Equal(PasswordVerificationResult.Success, new PasswordHasher().VerifyPasswordWithRehash(TestPasswords.Valid, global.Senha));
        if (iterations >= 600_000) Assert.Equal(oldHash, global.Senha);
        Assert.Equal(oldHash, user.Senha);
        Assert.Equal(version, global.SecurityVersion);
        Assert.Equal(updatedAt, global.DataAtualizacao);
        Assert.Null(session.RevokedAt);
        await handler.Handle(request, default);
        Assert.Equal(iterations < 600_000 ? 1 : 0, hasher.Hashes);
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData("locked")]
    [InlineData("inactive-global")]
    [InlineData("inactive-membership")]
    [InlineData("inactive-user")]
    [InlineData("inactive-clinic")]
    [InlineData("missing-team")]
    [InlineData("missing-team-fallback")]
    public async Task Authenticate_FailureNeverDerivesOrPersistsAnUpgrade(string failure)
    {
        await using var context = TestDbContextFactory.Create();
        var oldHash = PasswordHashTestData.Create(TestPasswords.Valid, 210_000);
        var user = CreateUser("denied@example.com", oldHash,
            perfilId: failure.StartsWith("missing-team", StringComparison.Ordinal) ? Perfil.EquipeId : Perfil.MedicosId);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var member = await GlobalIdentityService.EnsureForUserAsync(context, user, default);
        if (failure == "missing-team-fallback")
        {
            oldHash = PasswordHashTestData.Create("UnconfirmedGlobal@123", 210_000);
            member.UsuarioGlobal.Senha = oldHash;
            member.UsuarioGlobal.DataAtualizacao = null;
        }
        if (failure == "inactive-global") member.UsuarioGlobal.Ativo = false;
        if (failure == "inactive-membership") member.Ativo = false;
        if (failure == "inactive-user") user.Ativo = false;
        if (failure == "inactive-clinic") (await context.Clinicas.SingleAsync()).Ativa = false;
        await context.SaveChangesAsync();
        var hasher = new UpgradeCountingHasher();
        var handler = new AuthenticateUserCommandHandler(context, hasher, new StubJwtTokenService("token"),
            CreateLicencaService(context), ClinicaContextFactory.CreateDefaultResolved(),
            new UpgradeTestProtection(failure == "locked"), NullLogger<AuthenticateUserCommandHandler>.Instance);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(
            new AuthenticateUserCommand { Email = user.Email, Senha = failure == "wrong" ? "wrong" : TestPasswords.Valid }, default));
        Assert.Equal(0, hasher.Hashes);
        Assert.Equal(oldHash, (await context.UsuariosGlobais.SingleAsync()).Senha);
        if (failure == "inactive-global") Assert.False(member.UsuarioGlobal.Ativo);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Authenticate_LocalFallbackRemainsRestrictedToUnconfirmedIdentity(bool confirmed)
    {
        await using var context = TestDbContextFactory.Create();
        var local = PasswordHashTestData.Create(TestPasswords.Valid, 210_000);
        var user = CreateUser("fallback@example.com", local);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var member = await GlobalIdentityService.EnsureForUserAsync(context, user, default);
        var canonical = PasswordHashTestData.Create("CanonicalPassword@123", 210_000);
        member.UsuarioGlobal.Senha = canonical;
        member.UsuarioGlobal.DataAtualizacao = confirmed ? DateTime.UtcNow : null;
        await context.SaveChangesAsync();
        var hasher = new UpgradeCountingHasher();
        var handler = new AuthenticateUserCommandHandler(context, hasher, new StubJwtTokenService("token"),
            CreateLicencaService(context), NullLogger<AuthenticateUserCommandHandler>.Instance);
        var request = new AuthenticateUserCommand { Email = user.Email, Senha = TestPasswords.Valid };
        if (confirmed)
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(request, default));
            Assert.Equal(canonical, member.UsuarioGlobal.Senha);
            Assert.Equal(0, hasher.Hashes);
        }
        else
        {
            await handler.Handle(request, default);
            Assert.StartsWith("PBKDF2-SHA256$600000$", member.UsuarioGlobal.Senha);
            Assert.Equal(1, hasher.Hashes);
        }
        Assert.Equal(local, user.Senha);
    }

    [Fact]
    public async Task Authenticate_ValidCanonicalCredentialClosesLegacyFallback()
    {
        await using var context = TestDbContextFactory.Create();
        var canonical = PasswordHashTestData.Create(TestPasswords.Valid, 600_000);
        var user = CreateUser("confirm@example.com", canonical);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var member = await GlobalIdentityService.EnsureForUserAsync(context, user, default);
        user.Senha = PasswordHashTestData.Create("OldLocalPassword@123", 210_000);
        member.UsuarioGlobal.DataAtualizacao = null;
        await context.SaveChangesAsync();
        var hasher = new UpgradeCountingHasher();
        var handler = new AuthenticateUserCommandHandler(context, hasher, new StubJwtTokenService("token"),
            CreateLicencaService(context), NullLogger<AuthenticateUserCommandHandler>.Instance);

        await handler.Handle(new AuthenticateUserCommand { Email = user.Email, Senha = TestPasswords.Valid }, default);
        Assert.NotNull(member.UsuarioGlobal.DataAtualizacao);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(
            new AuthenticateUserCommand { Email = user.Email, Senha = "OldLocalPassword@123" }, default));
        Assert.Equal(canonical, member.UsuarioGlobal.Senha);
        Assert.Equal(0, hasher.Hashes);
    }

    private sealed class UpgradeCountingHasher : IPasswordHasher
    {
        private readonly PasswordHasher _inner = new();
        public int Hashes { get; private set; }
        public string HashPassword(string password) { Hashes++; return _inner.HashPassword(password); }
        public bool VerifyPassword(string password, string hash) => _inner.VerifyPassword(password, hash);
        public PasswordVerificationResult VerifyPasswordWithRehash(string password, string hash) => _inner.VerifyPasswordWithRehash(password, hash);
    }

    private sealed class UpgradeTestProtection(bool locked) : ILoginAccountProtection
    {
        public Task<bool> IsLockedAsync(int id, CancellationToken ct) => Task.FromResult(locked);
        public Task RegisterFailureAsync(int id, CancellationToken ct) => Task.CompletedTask;
        public Task RegisterSuccessAsync(int id, CancellationToken ct) => Task.CompletedTask;
    }
}
