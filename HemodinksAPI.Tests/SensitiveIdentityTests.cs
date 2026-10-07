using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Security;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.Extensions.Logging.Abstractions;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Tests;

public sealed class SensitiveIdentityTests
{
    private static async Task<CurrentUserContext> ActorAsync(PasswordResetSecurityDbContext db) =>
        SensitiveIdentityTestSupport.Actor(await db.AuthenticationSessions.Include(x => x.UsuarioClinica).ThenInclude(x => x.User).SingleAsync());

    [Fact]
    public async Task Email_RequiresConfirmation_UpdatesOnlyGlobalIdentityAliases_RevokesSessionsAndRecovery()
    {
        await using var database = await PasswordResetSecurityTestDatabase.CreateAsync();
        await using var db = database.Open();
        var actor = await ActorAsync(db);
        var global = await db.UsuariosGlobais.SingleAsync();
        var oldVersion = global.SecurityVersion;
        var otherClinic = new Clinica { Id = 2, Nome = "Other" };
        var alias = new User { Nome = "Alias", Email = global.Email, Telefone = "1", Senha = global.Senha, ClinicaId = 2 };
        db.UsuariosClinicas.Add(new UsuarioClinica { UsuarioGlobal = global, User = alias, Clinica = otherClinic, ClinicaId = 2 });
        var unrelated = new UsuarioGlobal { Nome = "Unrelated", Email = "unrelated@example.com", Senha = global.Senha };
        db.UsuariosGlobais.Add(unrelated);
        await db.SaveChangesAsync();
        var sender = new EmailConfirmationRecorder();
        var service = SensitiveIdentityTestSupport.Service(db, sender);
        var pending = await service.RequestEmailAsync(actor, PasswordResetSecurityTestDatabase.OldPassword, "new@example.com", default);
        Assert.Equal("recovery@example.com", global.Email);
        var stored = await db.EmailChangeRequests.SingleAsync();
        Assert.NotEqual(sender.Code, stored.CodeHash);
        Assert.Equal(64, sender.Code.Length);
        await service.ConfirmEmailAsync(actor, pending.RequestId, sender.Code, default);
        Assert.Equal("new@example.com", global.Email);
        Assert.All(await db.Users.ToListAsync(), u => Assert.Equal("new@example.com", u.Email));
        Assert.Equal("unrelated@example.com", unrelated.Email);
        Assert.NotEqual(oldVersion, global.SecurityVersion);
        Assert.All(await db.AuthenticationSessions.ToListAsync(), s => Assert.NotNull(s.RevokedAt));
        Assert.All(await db.PasswordResetTokens.ToListAsync(), t => Assert.NotNull(t.UsedAt));
        await Assert.ThrowsAsync<SensitiveIdentityException>(() => service.ConfirmEmailAsync(actor, pending.RequestId, sender.Code, default));
    }

    [Theory]
    [InlineData("wrong_code")]
    [InlineData("expired")]
    [InlineData("cancelled")]
    [InlineData("revoked")]
    [InlineData("version")]
    [InlineData("context")]
    [InlineData("clinic")]
    [InlineData("identity")]
    [InlineData("other_session")]
    [InlineData("inactive")]
    [InlineData("team")]
    [InlineData("stored_team")]
    public async Task Email_InvalidProofOrContext_CannotChangeAddress(string reason)
    {
        await using var database = await PasswordResetSecurityTestDatabase.CreateAsync();
        await using var db = database.Open();
        var actor = await ActorAsync(db);
        var sender = new EmailConfirmationRecorder();
        var service = SensitiveIdentityTestSupport.Service(db, sender);
        var pending = await service.RequestEmailAsync(actor, PasswordResetSecurityTestDatabase.OldPassword, "new@example.com", default);
        var session = await db.AuthenticationSessions.SingleAsync();
        var request = await db.EmailChangeRequests.SingleAsync();
        switch (reason)
        {
            case "wrong_code": sender = new EmailConfirmationRecorder(); break;
            case "expired": request.ExpiresAt = DateTime.UtcNow.AddMinutes(-1); break;
            case "cancelled": await service.CancelEmailAsync(actor, pending.RequestId, default); break;
            case "revoked": session.RevokedAt = DateTime.UtcNow; break;
            case "version": session.UsuarioGlobal.SecurityVersion = Guid.NewGuid(); break;
            case "context": session.ContextVersion = Guid.NewGuid(); break;
            case "clinic": actor = actor with { ClinicaId = 2 }; break;
            case "identity": actor = actor with { UsuarioGlobalId = actor.UsuarioGlobalId + 1 }; break;
            case "other_session":
                var other = new AuthenticationSession { Id = Guid.NewGuid(), UsuarioGlobalId = session.UsuarioGlobalId,
                    UsuarioClinicaId = session.UsuarioClinicaId, SecurityVersion = session.SecurityVersion, RefreshTokenHash = "other" };
                db.AuthenticationSessions.Add(other);
                actor = actor with { SessionId = other.Id }; break;
            case "inactive": session.UsuarioGlobal.Ativo = false; break;
            case "stored_team": session.EquipeId = 1; break;
            case "team": actor = actor with { EquipeId = 1, IdentificacaoConfiavel = true }; break;
        }
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<SensitiveIdentityException>(() => service.ConfirmEmailAsync(actor, pending.RequestId, sender.Code, default));
        Assert.Equal("recovery@example.com", (await db.UsuariosGlobais.SingleAsync()).Email);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Team_CannotUseSharedPassword_EvenWithTrustedOperator(bool trusted)
    {
        await using var database = await PasswordResetSecurityTestDatabase.CreateAsync();
        await using var db = database.Open();
        var actor = (await ActorAsync(db)) with { EquipeId = 42, IdentificacaoConfiavel = trusted };
        var service = SensitiveIdentityTestSupport.Service(db);
        var ex = await Assert.ThrowsAsync<SensitiveIdentityException>(() => service.ChangePasswordAsync(actor, actor.Id,
            PasswordResetSecurityTestDatabase.OldPassword, PasswordResetSecurityTestDatabase.NewPassword, default));
        Assert.Equal("individual_identity_required", ex.Code);
        await Assert.ThrowsAsync<SensitiveIdentityException>(() => service.RequestEmailAsync(actor,
            PasswordResetSecurityTestDatabase.OldPassword, "new@example.com", default));
    }

    [Fact]
    public async Task IncorrectPassword_AndDeliveryFailure_PreserveIdentityAndValidSession()
    {
        await using var database = await PasswordResetSecurityTestDatabase.CreateAsync();
        await using var db = database.Open();
        var actor = await ActorAsync(db);
        var sender = new EmailConfirmationRecorder { Fail = true };
        var service = SensitiveIdentityTestSupport.Service(db, sender);
        await Assert.ThrowsAsync<SensitiveIdentityException>(() => service.RequestEmailAsync(actor, "wrong", "new@example.com", default));
        var unavailable = await Assert.ThrowsAsync<SensitiveIdentityException>(() => service.RequestEmailAsync(actor,
            PasswordResetSecurityTestDatabase.OldPassword, "new@example.com", default));
        Assert.Equal("email_confirmation_unavailable", unavailable.Code);
        Assert.Empty(await db.EmailChangeRequests.ToListAsync());
        Assert.Null((await db.AuthenticationSessions.SingleAsync()).RevokedAt);
        Assert.Equal("recovery@example.com", (await db.UsuariosGlobais.SingleAsync()).Email);
    }

    [Fact]
    public async Task TwoConsumersReadingUnusedProof_OnlyOneCanCommit_SecondRollsBackCredentialChanges()
    {
        await using var database = await PasswordResetSecurityTestDatabase.CreateAsync();
        Guid id; CurrentUserContext actor; var sender = new EmailConfirmationRecorder();
        await using (var request = database.Open())
        {
            actor = await ActorAsync(request);
            id = (await SensitiveIdentityTestSupport.Service(request, sender).RequestEmailAsync(actor,
                PasswordResetSecurityTestDatabase.OldPassword, "new@example.com", default)).RequestId;
        }
        await using var loser = database.Open();
        // Load both credential and proof before the winner commits, reproducing a stale concurrent consumer.
        await loser.AuthenticationSessions.Include(s => s.UsuarioGlobal).Include(s => s.UsuarioClinica).ThenInclude(m => m.User)
            .Include(s => s.UsuarioClinica).ThenInclude(m => m.Clinica).LoadAsync();
        await loser.EmailChangeRequests.LoadAsync();
        loser.BeforeSensitiveSave = async () =>
        {
            await using var winner = database.Open();
            await SensitiveIdentityTestSupport.Service(winner).ConfirmEmailAsync(actor, id, sender.Code, default);
        };
        await Assert.ThrowsAsync<SensitiveIdentityException>(() => SensitiveIdentityTestSupport.Service(loser).ConfirmEmailAsync(actor, id, sender.Code, default));
        await using var verify = database.Open();
        Assert.Equal("new@example.com", (await verify.UsuariosGlobais.SingleAsync()).Email);
        Assert.NotNull((await verify.EmailChangeRequests.SingleAsync()).UsedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PasswordChangeOrRecovery_InvalidatesPendingEmailProof(bool recovery)
    {
        await using var database = await PasswordResetSecurityTestDatabase.CreateAsync();
        await using var db = database.Open();
        var actor = await ActorAsync(db);
        var sender = new EmailConfirmationRecorder();
        var service = SensitiveIdentityTestSupport.Service(db, sender);
        var pending = await service.RequestEmailAsync(actor, PasswordResetSecurityTestDatabase.OldPassword, "new@example.com", default);
        if (recovery)
            await new ConfirmPasswordResetCommandHandler(TestPasswordPolicy.Instance, db, new PasswordHasher(),
                NullLogger<ConfirmPasswordResetCommandHandler>.Instance).Handle(new ConfirmPasswordResetCommand {
                    Token = PasswordResetSecurityTestDatabase.Token, NovaSenha = PasswordResetSecurityTestDatabase.NewPassword }, default);
        else
            await new ChangePasswordCommandHandler(TestPasswordPolicy.Instance, service).Handle(new ChangePasswordCommand {
                CurrentUser = actor, UserId = actor.Id, SenhaAtual = PasswordResetSecurityTestDatabase.OldPassword,
                NovaSenha = PasswordResetSecurityTestDatabase.NewPassword }, default);
        await Assert.ThrowsAsync<SensitiveIdentityException>(() => service.ConfirmEmailAsync(actor, pending.RequestId, sender.Code, default));
        Assert.Equal("recovery@example.com", (await db.UsuariosGlobais.SingleAsync()).Email);
    }

    [Theory]
    [InlineData("taken@example.com")]
    [InlineData("not-an-email")]
    [InlineData("")]
    public async Task InvalidOrDuplicateEmail_DoesNotCreateRequest(string email)
    {
        await using var database = await PasswordResetSecurityTestDatabase.CreateAsync();
        await using var db = database.Open();
        db.UsuariosGlobais.Add(new UsuarioGlobal { Nome = "Taken", Email = "taken@example.com", Senha = "unused" });
        await db.SaveChangesAsync();
        var actor = await ActorAsync(db);
        await Assert.ThrowsAsync<SensitiveIdentityException>(() => SensitiveIdentityTestSupport.Service(db).RequestEmailAsync(actor,
            PasswordResetSecurityTestDatabase.OldPassword, email, default));
        Assert.Empty(await db.EmailChangeRequests.ToListAsync());
    }
}
