using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public sealed class PasswordResetSecurityPersistenceTests
{
    [Theory]
    [InlineData("invalid")]
    [InlineData("expired")]
    [InlineData("used")]
    [InlineData("password")]
    public async Task InvalidRecovery_PreservesPasswordVersionAndSession(string scenario)
    {
        await using var database = await PasswordResetSecurityTestDatabase.CreateAsync();
        await using var db = database.Open();
        var token = await db.PasswordResetTokens.SingleAsync();
        if (scenario == "expired") token.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        if (scenario == "used") token.UsedAt = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
        var before = await Snapshot(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(db).Handle(new ConfirmPasswordResetCommand
        {
            Token = scenario == "invalid" ? "wrong-token" : PasswordResetSecurityTestDatabase.Token,
            NovaSenha = scenario == "password" ? "short" : PasswordResetSecurityTestDatabase.NewPassword
        }, default));
        await using var verify = database.Open();
        Assert.Equal(before, await Snapshot(verify));
    }

    [Theory]
    [InlineData("Users")]
    [InlineData("UsuariosGlobais")]
    [InlineData("PasswordResetTokens")]
    [InlineData("AuthenticationSessions")]
    public async Task PersistenceFailure_RollsBackAllCredentialChanges(string table)
    {
        await using var database = await PasswordResetSecurityTestDatabase.CreateAsync();
        await using var db = database.Open();
        var before = await Snapshot(db);
        // Fail an actual SQL UPDATE, after EF has started its transaction.
        var trigger = table switch
        {
            "Users" => "CREATE TRIGGER fail_recovery BEFORE UPDATE ON Users WHEN OLD.Senha <> NEW.Senha BEGIN SELECT RAISE(ABORT, 'simulated persistence failure'); END;",
            "UsuariosGlobais" => "CREATE TRIGGER fail_recovery BEFORE UPDATE ON UsuariosGlobais WHEN OLD.Senha <> NEW.Senha BEGIN SELECT RAISE(ABORT, 'simulated persistence failure'); END;",
            "PasswordResetTokens" => "CREATE TRIGGER fail_recovery BEFORE UPDATE ON PasswordResetTokens WHEN NEW.UsedAt IS NOT NULL BEGIN SELECT RAISE(ABORT, 'simulated persistence failure'); END;",
            "AuthenticationSessions" => "CREATE TRIGGER fail_recovery BEFORE UPDATE ON AuthenticationSessions WHEN NEW.RevokedAt IS NOT NULL BEGIN SELECT RAISE(ABORT, 'simulated persistence failure'); END;",
            _ => throw new ArgumentOutOfRangeException(nameof(table))
        };
        await db.Database.ExecuteSqlRawAsync(trigger);
        await Assert.ThrowsAsync<DbUpdateException>(() => Reset(db));
        await using var verify = database.Open();
        Assert.Equal(before, await Snapshot(verify));
    }

    [Fact]
    public async Task ConcurrentConfirmations_OnlyOnePasswordCommits()
    {
        await using var database = await PasswordResetSecurityTestDatabase.CreateAsync();
        await using var loser = database.Open();
        await using var winner = database.Open();
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        loser.BeforeCredentialSave = async () => { loaded.SetResult(); await resume.Task; };
        var losingReset = Reset(loser, "LosingPassword@789");
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(15));
        try { await Reset(winner); }
        finally { resume.TrySetResult(); }
        await Assert.ThrowsAsync<InvalidOperationException>(() => losingReset);
        await using var verify = database.Open();
        var user = await verify.Users.SingleAsync();
        var global = await verify.UsuariosGlobais.SingleAsync();
        Assert.True(new PasswordHasher().VerifyPassword(PasswordResetSecurityTestDatabase.NewPassword, global.Senha));
        Assert.Equal(user.Senha, global.Senha);
        Assert.NotNull((await verify.PasswordResetTokens.SingleAsync()).UsedAt);
        Assert.NotNull((await verify.AuthenticationSessions.SingleAsync()).RevokedAt);
    }

    [Fact]
    public async Task TokenReadBeforeAnotherReset_CannotBeConsumedWithFreshGlobalVersion()
    {
        await using var database = await PasswordResetSecurityTestDatabase.CreateAsync();
        await using var stale = database.Open();
        var token = await stale.PasswordResetTokens.SingleAsync();
        await using (var winner = database.Open()) await Reset(winner);
        // Interleaving: token read before the winner, identity read after the winner.
        // A concurrency check on the global identity alone cannot catch this stale token.
        var global = await stale.UsuariosGlobais.SingleAsync();
        var committedVersion = global.SecurityVersion;
        global.SecurityVersion = Guid.NewGuid();
        global.Senha = new PasswordHasher().HashPassword("LosingPassword@789");
        token.UsedAt = DateTime.UtcNow;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        await using var verify = database.Open();
        Assert.Equal(committedVersion, (await verify.UsuariosGlobais.SingleAsync()).SecurityVersion);
        Assert.True(new PasswordHasher().VerifyPassword(PasswordResetSecurityTestDatabase.NewPassword,
            (await verify.UsuariosGlobais.SingleAsync()).Senha));
    }

    [Fact]
    public async Task ResetRequestRacingWithConfirmation_PreservesGenericResponseAndDoesNotSendRolledBackToken()
    {
        await using var database = await PasswordResetSecurityTestDatabase.CreateAsync();
        await using var requestDb = database.Open();
        await using var confirmDb = database.Open();
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        requestDb.BeforeCredentialSave = async () => { loaded.SetResult(); await resume.Task; };
        var sender = new RecordingPasswordResetNotificationSender();
        var handler = new ResetUserPasswordByEmailCommandHandler(requestDb, sender,
            NullLogger<ResetUserPasswordByEmailCommandHandler>.Instance);
        var pending = handler.Handle(new ResetUserPasswordByEmailCommand { Email = "recovery@example.com" }, default);
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(15));
        try { await Reset(confirmDb); }
        finally { resume.TrySetResult(); }
        var response = await pending;
        Assert.Equal("Se o email estiver cadastrado, enviaremos as instrucoes para redefinir a senha.", response.Message);
        Assert.Empty(sender.Notifications);
        // The HTTP idempotency layer can save the same unit of work after a generic response.
        await requestDb.SaveChangesAsync();
        await using var verify = database.Open();
        Assert.NotNull((await verify.PasswordResetTokens.SingleAsync()).UsedAt);
        Assert.True(new PasswordHasher().VerifyPassword(PasswordResetSecurityTestDatabase.NewPassword,
            (await verify.UsuariosGlobais.SingleAsync()).Senha));
    }

    private static ConfirmPasswordResetCommandHandler Handler(PasswordResetSecurityDbContext db) =>
        new(TestPasswordPolicy.Instance, db, new PasswordHasher(), NullLogger<ConfirmPasswordResetCommandHandler>.Instance);

    private static Task<ResetUserPasswordResponse> Reset(PasswordResetSecurityDbContext db,
        string password = PasswordResetSecurityTestDatabase.NewPassword) => Handler(db).Handle(
            new ConfirmPasswordResetCommand { Token = PasswordResetSecurityTestDatabase.Token, NovaSenha = password }, default);

    private sealed record CredentialSnapshot(string LocalPassword, string GlobalPassword, Guid Version, bool MustChange,
        DateTime? UsedAt, DateTime? RevokedAt, string RefreshHash);

    private static async Task<CredentialSnapshot> Snapshot(PasswordResetSecurityDbContext db)
    {
        var user = await db.Users.AsNoTracking().SingleAsync();
        var global = await db.UsuariosGlobais.AsNoTracking().SingleAsync();
        var token = await db.PasswordResetTokens.AsNoTracking().SingleAsync();
        var session = await db.AuthenticationSessions.AsNoTracking().SingleAsync();
        return new(user.Senha, global.Senha, global.SecurityVersion, user.PrecisaTrocarSenha, token.UsedAt, session.RevokedAt, session.RefreshTokenHash);
    }
}
