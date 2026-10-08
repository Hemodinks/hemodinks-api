using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Authentication;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Tests;

public sealed class LoginAccountProtectionWindowTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task ExpiredLock_StartsNewAttemptWindow_AndActiveLockCannotBeClearedByLateSuccess()
    {
        await using var db = TestDbContextFactory.Create();
        var account = new UsuarioGlobal { Nome = "Test", Email = "test@example.invalid", Senha = "test-hash" };
        db.UsuariosGlobais.Add(account);
        await db.SaveChangesAsync();
        var clock = new Clock();
        var protection = new EfLoginAccountProtection(db, Options.Create(new LoginAccountProtectionOptions
        { MaximumFailedAttempts = 3, AttemptWindowMinutes = 15, LockoutMinutes = 1 }), clock);
        for (var i = 0; i < 3; i++) await protection.RegisterFailureAsync(account.Id, default);
        var blockedUntil = account.BloqueadoAte;
        await protection.RegisterFailureAsync(account.Id, default);
        Assert.Equal(blockedUntil, account.BloqueadoAte);
        await protection.RegisterSuccessAsync(account.Id, default);
        Assert.True(await protection.IsLockedAsync(account.Id, default));
        clock.Now = clock.Now.AddMinutes(1);
        Assert.False(await protection.IsLockedAsync(account.Id, default));
        await protection.RegisterFailureAsync(account.Id, default);
        Assert.Equal(1, (await db.UsuariosGlobais.SingleAsync()).TentativasLoginFalhas);
        Assert.False(await protection.IsLockedAsync(account.Id, default));
        await protection.RegisterSuccessAsync(account.Id, default);
        Assert.Equal(0, account.TentativasLoginFalhas);
    }

    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task ConcurrentFailures_AreAtomic_AndLateSuccessCannotClearActiveSqlLock()
    {
        if (Environment.GetEnvironmentVariable("HEMODINKS_TEST_LOCALDB") != "1"
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HEMODINKS_TEST_SQLSERVER_CONNECTION_STRING")))
            Assert.Skip("Requires isolated SQL Server.");

        var connection = new SqlConnectionStringBuilder(
            SqlServerTestConnection.Create($"HemodinksLoginProtection_{Guid.NewGuid():N}"))
        { MultipleActiveResultSets = false }.ConnectionString;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options;
        await using var setup = new PlatformDbContext(options);
        try
        {
            await setup.Database.EnsureCreatedAsync();
            var account = new UsuarioGlobal { Nome = "SQL Test", Email = "lock@example.invalid", Senha = "test-hash" };
            setup.UsuariosGlobais.Add(account);
            await setup.SaveChangesAsync();
            var clock = new Clock();
            var protectionOptions = Options.Create(new LoginAccountProtectionOptions
            { MaximumFailedAttempts = 3, AttemptWindowMinutes = 15, LockoutMinutes = 1 });
            // This context loaded the account before the other requests acquired the lock.
            await using var lateSuccess = new PlatformDbContext(options);
            await lateSuccess.UsuariosGlobais.SingleAsync(item => item.Id == account.Id);
            await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
            {
                await using var request = new PlatformDbContext(options);
                await new EfLoginAccountProtection(request, protectionOptions, clock)
                    .RegisterFailureAsync(account.Id, default);
            }));
            var protection = new EfLoginAccountProtection(lateSuccess, protectionOptions, clock);
            await protection.RegisterSuccessAsync(account.Id, default);
            setup.ChangeTracker.Clear();
            var locked = await setup.UsuariosGlobais.SingleAsync(item => item.Id == account.Id);
            Assert.Equal(3, locked.TentativasLoginFalhas);
            Assert.Equal(clock.Now.UtcDateTime.AddMinutes(1), locked.BloqueadoAte);
            Assert.True(await protection.IsLockedAsync(account.Id, default));

            clock.Now = clock.Now.AddMinutes(1);
            await protection.RegisterFailureAsync(account.Id, default);
            setup.ChangeTracker.Clear();
            var restarted = await setup.UsuariosGlobais.SingleAsync(item => item.Id == account.Id);
            Assert.Equal(1, restarted.TentativasLoginFalhas);
            Assert.Null(restarted.BloqueadoAte);
            await protection.RegisterSuccessAsync(account.Id, default);
            setup.ChangeTracker.Clear();
            Assert.Equal(0, (await setup.UsuariosGlobais.SingleAsync(item => item.Id == account.Id)).TentativasLoginFalhas);
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }
}
