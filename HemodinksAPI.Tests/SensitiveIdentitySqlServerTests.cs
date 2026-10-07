using HemodinksAPI.Application.Security;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HemodinksAPI.Tests;

public sealed class SensitiveIdentitySqlServerTests
{
    private sealed class Interleave(Func<Task> action) : SaveChangesInterceptor
    {
        private int calls;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref calls) == 1) await action();
            return result;
        }
    }

    [Theory]
    [Trait("Category", "SqlServer")]
    [InlineData("consume")]
    [InlineData("revoked")]
    [InlineData("context")]
    [InlineData("cancel")]
    [InlineData("password")]
    public async Task ConfirmationRace_CannotCommitAcrossConsumedProofOrChangedSession(string scenario)
    {
        if (Environment.GetEnvironmentVariable("HEMODINKS_TEST_LOCALDB") != "1"
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HEMODINKS_TEST_SQLSERVER_CONNECTION_STRING")))
            Assert.Skip("Requires isolated SQL Server with native rowversion.");
        var connection = SqlServerTestConnection.Create($"HemodinksIdentityRace_{Guid.NewGuid():N}");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options;
        await using var setup = new PlatformDbContext(options);
        try
        {
            await setup.Database.EnsureCreatedAsync();
            var user = new User { ClinicaId = 1, Nome = "Identity race", Email = "race@example.invalid",
                Telefone = "11999999999", Senha = new PasswordHasher().HashPassword(TestPasswords.Valid), PerfilId = Perfil.MedicosId };
            setup.Users.Add(user);
            await setup.SaveChangesAsync();
            var actor = await SensitiveIdentityTestSupport.SeedActorAsync(setup, user);
            var sender = new EmailConfirmationRecorder();
            var pending = await SensitiveIdentityTestSupport.Service(setup, sender).RequestEmailAsync(actor, TestPasswords.Valid, "new@example.invalid", default);
            Assert.NotEmpty((await setup.AuthenticationSessions.SingleAsync()).RowVersion);
            var interleave = new Interleave(async () =>
            {
                await using var winner = new PlatformDbContext(options);
                if (scenario == "consume")
                    await SensitiveIdentityTestSupport.Service(winner).ConfirmEmailAsync(actor, pending.RequestId, sender.Code, default);
                else if (scenario == "cancel")
                    await SensitiveIdentityTestSupport.Service(winner).CancelEmailAsync(actor, pending.RequestId, default);
                else if (scenario == "password")
                    await SensitiveIdentityTestSupport.Service(winner).ChangePasswordAsync(actor, actor.Id, TestPasswords.Valid,
                        PasswordResetSecurityTestDatabase.NewPassword, default);
                else
                {
                    var session = await winner.AuthenticationSessions.SingleAsync();
                    if (scenario == "revoked") session.RevokedAt = DateTime.UtcNow;
                    else session.ContextVersion = Guid.NewGuid();
                    await winner.SaveChangesAsync();
                }
            });
            await using var loser = new PlatformDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(connection).AddInterceptors(interleave).Options);
            await Assert.ThrowsAsync<SensitiveIdentityException>(() => SensitiveIdentityTestSupport.Service(loser)
                .ConfirmEmailAsync(actor, pending.RequestId, sender.Code, default));
            await using var verify = new PlatformDbContext(options);
            Assert.Equal(scenario == "consume" ? "new@example.invalid" : "race@example.invalid",
                (await verify.UsuariosGlobais.SingleAsync()).Email);
            Assert.Equal(scenario is "consume" or "cancel", (await verify.EmailChangeRequests.SingleAsync()).UsedAt.HasValue);
        }
        finally { await setup.Database.EnsureDeletedAsync(); }
    }
}
