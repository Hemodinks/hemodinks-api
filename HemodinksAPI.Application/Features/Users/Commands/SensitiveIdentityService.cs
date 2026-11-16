using System.Net.Mail;
using System.Security.Cryptography;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Sessions;
using HemodinksAPI.Application.Security;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Application.Utils;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using HemodinksAPI.Domain.Exceptions;

namespace HemodinksAPI.Application.Features.Users.Commands;

public sealed class SensitiveIdentityOptions
{
    public int EmailConfirmationMinutes { get; set; } = 10;
    // #322: código curto exige limite de tentativas; esgotado, a solicitação é invalidada.
    public int EmailConfirmationMaxAttempts { get; set; } = 5;
}

public sealed record EmailChangeStarted(Guid RequestId, DateTime ExpiresAt);

public sealed class SensitiveIdentityService(
    ISensitiveIdentityDbContext db, IPasswordHasher hasher, ILoginAccountProtection protection,
    SessionLifetimePolicy lifetime, IEmailChangeNotificationSender sender, IOptions<SensitiveIdentityOptions> options,
    IEmailChangeCodeHasher codeHasher)
{
    private const int MaximumCurrentPasswordLength = 1024;
    internal const int EmailCodeDigits = 6;

    private async Task<AuthenticationSession> RequireSessionAsync(CurrentUserContext? actor, CancellationToken ct)
    {
        if (actor == null || actor.SessionId == null) throw SensitiveIdentityException.Invalid();
        if (actor.IsEquipe || actor.EquipeId.HasValue)
            throw new SensitiveIdentityException("individual_identity_required", "Entre com sua conta individual para alterar senha ou email.");
        var session = await db.AuthenticationSessions.Include(x => x.UsuarioGlobal)
            .Include(x => x.UsuarioClinica).ThenInclude(x => x.User)
            .Include(x => x.UsuarioClinica).ThenInclude(x => x.Clinica)
            .SingleOrDefaultAsync(x => x.Id == actor.SessionId && x.UsuarioGlobalId == actor.UsuarioGlobalId
                && x.UsuarioClinicaId == actor.UsuarioClinicaId, ct);
        if (session == null || session.RevokedAt != null || session.EquipeId.HasValue
            || session.UsuarioClinica.User.PerfilId == Perfil.EquipeId
            || session.UsuarioClinica.UserId != actor.Id || session.UsuarioClinica.ClinicaId != actor.ClinicaId
            || session.UsuarioClinica.User.ClinicaId != actor.ClinicaId
            || !session.UsuarioGlobal.Ativo || !session.UsuarioClinica.Ativo || !session.UsuarioClinica.User.Ativo
            || !session.UsuarioClinica.Clinica.Ativa || session.UsuarioGlobal.TemporaryPasswordRecovery
            || session.SecurityVersion != session.UsuarioGlobal.SecurityVersion
            || lifetime.Failure(session.CreatedAt, session.LastActivityAt) != null)
            throw SensitiveIdentityException.Invalid();
        return session;
    }

    private async Task<AuthenticationSession> VerifyCurrentPasswordAsync(CurrentUserContext? actor, string password, CancellationToken ct)
    {
        var session = await RequireSessionAsync(actor, ct);
        if (password.Length > MaximumCurrentPasswordLength || await protection.IsLockedAsync(session.UsuarioGlobalId, ct))
            throw SensitiveIdentityException.Invalid();
        if (!hasher.VerifyPassword(password, session.UsuarioGlobal.Senha))
        {
            await protection.RegisterFailureAsync(session.UsuarioGlobalId, ct);
            throw SensitiveIdentityException.Invalid();
        }
        return session;
    }

    internal async Task ChangePasswordAsync(CurrentUserContext? actor, int userId, string currentPassword, string newPassword, CancellationToken ct)
    {
        if (actor?.Id != userId) throw new UnauthorizedAccessException("Sem permissão para alterar esta senha.");
        var session = await VerifyCurrentPasswordAsync(actor, currentPassword, ct);
        if (hasher.VerifyPassword(newPassword, session.UsuarioGlobal.Senha))
            throw new BusinessRuleException("A nova senha nao pode ser igual a senha atual");
        PasswordCommandMutations.ApplyNewPassword(session.UsuarioClinica.User, hasher, newPassword, false, lifetime.UtcNow);
        session.UsuarioGlobal.Senha = session.UsuarioClinica.User.Senha;
        session.UsuarioGlobal.DataAtualizacao = lifetime.UtcNow;
        await InvalidateAsync(session, ct);
        await SaveAsync(ct);
    }

    public async Task<EmailChangeStarted> RequestEmailAsync(CurrentUserContext actor, string password, string newEmail, CancellationToken ct)
    {
        var session = await VerifyCurrentPasswordAsync(actor, password, ct);
        var email = GlobalIdentityService.NormalizeEmail(newEmail);
        if (email.Length > 255 || !MailAddress.TryCreate(email, out var address) || address.Address != email)
            throw new SensitiveIdentityException("invalid_new_email", "Informe um endereço de email válido.");
        await EnsureAvailableAsync(session.UsuarioGlobalId, email, ct);
        var code = GenerateEmailCode();
        var requestId = Guid.NewGuid();
        var pending = new EmailChangeRequest
        {
            Id = requestId, SessionId = session.Id, UsuarioGlobalId = session.UsuarioGlobalId,
            UsuarioClinicaId = session.UsuarioClinicaId, ClinicaId = actor.ClinicaId,
            SecurityVersion = session.SecurityVersion, ContextVersion = session.ContextVersion,
            NewEmail = email, CodeHash = codeHasher.Hash(requestId, code),
            ExpiresAt = lifetime.UtcNow.AddMinutes(options.Value.EmailConfirmationMinutes)
        };
        // Send before persisting: a failed delivery cannot leave a usable request.
        // A later persistence failure may deliver an unusable code, never change the address.
        try { await sender.SendConfirmationAsync(email, code, options.Value.EmailConfirmationMinutes, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { throw new SensitiveIdentityException("email_confirmation_unavailable", "Não foi possível enviar a confirmação. Tente novamente mais tarde."); }
        if (lifetime.Failure(session.CreatedAt, session.LastActivityAt) != null) throw SensitiveIdentityException.Invalid();
        db.EmailChangeRequests.Add(pending);
        await SaveAsync(ct);
        return new(pending.Id, pending.ExpiresAt);
    }

    public async Task ConfirmEmailAsync(CurrentUserContext actor, Guid requestId, string code, CancellationToken ct)
    {
        var session = await RequireSessionAsync(actor, ct);
        var pending = await RequireRequestAsync(session, requestId, ct);
        // Reserva a tentativa ANTES de comparar: requisições paralelas não somam palpites além do limite.
        await ReserveConfirmationAttemptAsync(pending.Id, ct);
        var normalized = NormalizeEmailCode(code);
        if (normalized == null || !CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(pending.CodeHash), Convert.FromHexString(codeHasher.Hash(pending.Id, normalized))))
            await RejectWrongCodeAsync(pending.Id, ct);
        await EnsureAvailableAsync(session.UsuarioGlobalId, pending.NewEmail, ct);
        // Explicit global identity scope, never clinic-administrator authority over other identities.
        var users = await db.Users.Where(u => db.UsuariosClinicas.Any(m => m.UserId == u.Id
            && m.UsuarioGlobalId == session.UsuarioGlobalId)).ToListAsync(ct);
        foreach (var user in users) user.Email = pending.NewEmail;
        session.UsuarioGlobal.Email = pending.NewEmail;
        session.UsuarioGlobal.DataAtualizacao = lifetime.UtcNow;
        if (pending.ExpiresAt <= lifetime.UtcNow) throw SensitiveIdentityException.Invalid();
        pending.UsedAt = lifetime.UtcNow;
        await InvalidateAsync(session, ct);
        await SaveAsync(ct);
    }

    public async Task CancelEmailAsync(CurrentUserContext actor, Guid requestId, CancellationToken ct)
    {
        var session = await RequireSessionAsync(actor, ct);
        var pending = await RequireRequestAsync(session, requestId, ct);
        pending.UsedAt = lifetime.UtcNow;
        await SaveAsync(ct);
    }

    private async Task<EmailChangeRequest> RequireRequestAsync(AuthenticationSession session, Guid id, CancellationToken ct)
    {
        var pending = await db.EmailChangeRequests.SingleOrDefaultAsync(x => x.Id == id
            && x.SessionId == session.Id && x.UsuarioGlobalId == session.UsuarioGlobalId
            && x.UsuarioClinicaId == session.UsuarioClinicaId && x.ClinicaId == session.UsuarioClinica.ClinicaId
            && x.SecurityVersion == session.SecurityVersion && x.ContextVersion == session.ContextVersion
            && x.UsedAt == null && x.ExpiresAt > lifetime.UtcNow, ct);
        return pending ?? throw SensitiveIdentityException.Invalid();
    }

    private async Task EnsureAvailableAsync(int globalId, string email, CancellationToken ct)
    {
        if (await db.UsuariosGlobais.AnyAsync(x => x.Email == email, ct)
            || await db.Users.AnyAsync(u => u.Email == email && !db.UsuariosClinicas.Any(m =>
                m.UserId == u.Id && m.UsuarioGlobalId == globalId), ct))
            throw new SensitiveIdentityException("email_change_unavailable", "Não foi possível utilizar este endereço para a alteração.");
    }

    private async Task InvalidateAsync(AuthenticationSession session, CancellationToken ct)
    {
        if (lifetime.Failure(session.CreatedAt, session.LastActivityAt) != null) throw SensitiveIdentityException.Invalid();
        session.UsuarioGlobal.SecurityVersion = Guid.NewGuid();
        await PasswordCommandMutations.RevokeSessionsAndResetTokensAsync(db, session.UsuarioGlobalId, lifetime.UtcNow, ct);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw SensitiveIdentityException.Invalid(); }
        catch (DbUpdateException) { throw new SensitiveIdentityException("identity_change_conflict", "A alteração não foi concluída. Atualize os dados e tente novamente."); }
    }

    // Uniforme em 000000..999999 (RandomNumberGenerator.GetInt32 não tem viés de módulo).
    internal static string GenerateEmailCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

    // Aceita o código colado com espaços; qualquer outro formato conta como tentativa errada.
    internal static string? NormalizeEmailCode(string? code)
    {
        if (string.IsNullOrEmpty(code) || code.Length > 32) return null;
        var digits = string.Concat(code.Where(c => !char.IsWhiteSpace(c)));
        return digits.Length == EmailCodeDigits && digits.All(char.IsAsciiDigit) ? digits : null;
    }

    private int MaxConfirmationAttempts => Math.Max(1, options.Value.EmailConfirmationMaxAttempts);

    // Incremento atômico com teto no WHERE: no máximo MaxConfirmationAttempts comparações por solicitação,
    // mesmo com requisições paralelas. Sem vaga, recusa sem comparar o código.
    private async Task ReserveConfirmationAttemptAsync(Guid requestId, CancellationToken ct)
    {
        var max = MaxConfirmationAttempts;
        var reserved = await db.EmailChangeRequests
            .Where(x => x.Id == requestId && x.UsedAt == null && x.ConfirmationAttempts < max)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ConfirmationAttempts, x => x.ConfirmationAttempts + 1), ct);
        if (reserved == 0) await ThrowAttemptFailureAsync(requestId, ct);
    }

    private Task RejectWrongCodeAsync(Guid requestId, CancellationToken ct) => ThrowAttemptFailureAsync(requestId, ct);

    // Esgotado o limite, a solicitação é invalidada (UsedAt) e um novo código é exigido.
    private async Task ThrowAttemptFailureAsync(Guid requestId, CancellationToken ct)
    {
        var max = MaxConfirmationAttempts;
        var invalidated = await db.EmailChangeRequests
            .Where(x => x.Id == requestId && x.UsedAt == null && x.ConfirmationAttempts >= max)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedAt, lifetime.UtcNow), ct);
        var exhausted = invalidated > 0 || await db.EmailChangeRequests.AsNoTracking()
            .AnyAsync(x => x.Id == requestId && x.ConfirmationAttempts >= max, ct);
        throw exhausted ? SensitiveIdentityException.EmailConfirmationAttemptsExceeded() : SensitiveIdentityException.Invalid();
    }
}
