using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public sealed class PasswordHashUpgradeConcurrencyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleUpgrade_CannotOverwriteCommittedPasswordChangeOrRecovery(bool reset)
    {
        await using var database = await SeedAsync();
        await using var login = database.Open();
        var authentication = await AuthenticateAsync(login);
        var oldVersion = authentication.UsuarioGlobal.SecurityVersion;
        await using (var winner = database.Open())
        {
            if (reset)
                await new ConfirmPasswordResetCommandHandler(winner, new PasswordHasher(),
                    NullLogger<ConfirmPasswordResetCommandHandler>.Instance).Handle(new ConfirmPasswordResetCommand
                    { Token = PasswordResetSecurityTestDatabase.Token, NovaSenha = PasswordResetSecurityTestDatabase.NewPassword }, default);
            else
                await new ChangePasswordCommandHandler(winner, new PasswordHasher(),
                    NullLogger<ChangePasswordCommandHandler>.Instance).Handle(new ChangePasswordCommand
                    { UserId = (await winner.Users.SingleAsync()).Id, SenhaAtual = PasswordResetSecurityTestDatabase.OldPassword,
                        NovaSenha = PasswordResetSecurityTestDatabase.NewPassword }, default);
        }

        Assert.False(await PasswordHashUpgrade.TryUpgradeAsync(login, new PasswordHasher(), authentication,
            PasswordResetSecurityTestDatabase.OldPassword, default));
        // A later save on the request context must not resurrect the discarded upgrade.
        await login.SaveChangesAsync();
        await using var verify = database.Open();
        var global = await verify.UsuariosGlobais.SingleAsync();
        Assert.True(new PasswordHasher().VerifyPassword(PasswordResetSecurityTestDatabase.NewPassword, global.Senha));
        Assert.Equal((await verify.Users.SingleAsync()).Senha, global.Senha);
        Assert.Equal(!reset, global.SecurityVersion == oldVersion);
        Assert.Equal(reset, (await verify.AuthenticationSessions.SingleAsync()).RevokedAt.HasValue);
    }

    [Fact]
    public async Task TwoLoginsReadingSameCredential_BothSucceedWithoutOverwritingWinner()
    {
        await using var database = await SeedAsync();
        await using var first = database.Open();
        await using var second = database.Open();
        var firstAuth = await AuthenticateAsync(first);
        var secondAuth = await AuthenticateAsync(second);
        var oldHash = firstAuth.UsuarioGlobal.Senha;
        var version = firstAuth.UsuarioGlobal.SecurityVersion;
        Assert.True(await PasswordHashUpgrade.TryUpgradeAsync(first, new PasswordHasher(), firstAuth,
            PasswordResetSecurityTestDatabase.OldPassword, default));
        var winningHash = firstAuth.UsuarioGlobal.Senha;
        Assert.True(await PasswordHashUpgrade.TryUpgradeAsync(second, new PasswordHasher(), secondAuth,
            PasswordResetSecurityTestDatabase.OldPassword, default));
        await second.SaveChangesAsync();
        await using var verify = database.Open();
        var global = await verify.UsuariosGlobais.SingleAsync();
        Assert.Equal(winningHash, global.Senha);
        Assert.Equal(version, global.SecurityVersion);
        Assert.Equal(oldHash, (await verify.Users.SingleAsync()).Senha);
        Assert.Null((await verify.AuthenticationSessions.SingleAsync()).RevokedAt);
        Assert.StartsWith("PBKDF2-SHA256$600000$", global.Senha);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("inactive")]
    [InlineData("locked")]
    public async Task ConcurrentUpgrade_DoesNotAcceptChangedSecurityState(string state)
    {
        await using var database = await SeedAsync();
        await using var login = database.Open();
        var auth = await AuthenticateAsync(login);
        await using (var writer = database.Open())
        {
            var global = await writer.UsuariosGlobais.SingleAsync();
            global.Senha = new PasswordHasher().HashPassword(PasswordResetSecurityTestDatabase.OldPassword);
            if (state == "version") global.SecurityVersion = Guid.NewGuid();
            if (state == "inactive") global.Ativo = false;
            if (state == "locked") global.BloqueadoAte = DateTime.UtcNow.AddMinutes(5);
            await writer.SaveChangesAsync();
        }
        Assert.False(await PasswordHashUpgrade.TryUpgradeAsync(login, new PasswordHasher(), auth,
            PasswordResetSecurityTestDatabase.OldPassword, default));
    }

    private static async Task<PasswordResetSecurityTestDatabase> SeedAsync()
    {
        var database = await PasswordResetSecurityTestDatabase.CreateAsync();
        await using var context = database.Open();
        var hash = PasswordHashTestData.Create(PasswordResetSecurityTestDatabase.OldPassword, 210_000);
        (await context.Users.SingleAsync()).Senha = hash;
        (await context.UsuariosGlobais.SingleAsync()).Senha = hash;
        await context.SaveChangesAsync();
        return database;
    }

    private static async Task<GlobalAuthenticationContext> AuthenticateAsync(PasswordResetSecurityDbContext db)
    {
        var result = await GlobalIdentityService.AuthenticateAsync(db, new PasswordHasher(),
            await db.Users.SingleAsync(), PasswordResetSecurityTestDatabase.OldPassword, default);
        Assert.NotNull(result);
        Assert.True(result.NeedsPasswordRehash);
        return result;
    }
}
