using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Tests;

public sealed class LoginAccountProtectionTests
{
    [Fact]
    public async Task RegisterFailureAsync_WhenThresholdIsReached_LocksGlobalIdentity()
    {
        await using var context = TestDbContextFactory.Create();
        var account = new UsuarioGlobal
        {
            Nome = "Conta protegida",
            Email = "protected@example.com",
            Senha = "hash"
        };
        context.UsuariosGlobais.Add(account);
        await context.SaveChangesAsync();
        var protection = new EfLoginAccountProtection(
            context,
            Options.Create(new LoginAccountProtectionOptions
            {
                MaximumFailedAttempts = 3,
                AttemptWindowMinutes = 15,
                LockoutMinutes = 15
            }),
            TimeProvider.System);

        await protection.RegisterFailureAsync(account.Id, CancellationToken.None);
        await protection.RegisterFailureAsync(account.Id, CancellationToken.None);
        Assert.False(await protection.IsLockedAsync(account.Id, CancellationToken.None));

        await protection.RegisterFailureAsync(account.Id, CancellationToken.None);

        context.ChangeTracker.Clear();
        var stored = await context.UsuariosGlobais.SingleAsync(item => item.Id == account.Id);
        Assert.Equal(3, stored.TentativasLoginFalhas);
        Assert.True(stored.BloqueadoAte > DateTime.UtcNow);
        Assert.True(await protection.IsLockedAsync(account.Id, CancellationToken.None));
    }
}
