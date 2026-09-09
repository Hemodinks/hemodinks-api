using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Utils;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Domain.Utils;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Users.Commands;

public sealed class TemporaryAccessService(
    ITemporaryAccessDbContext context,
    IPasswordHasher hasher,
    TimeProvider clock)
{
    public async Task<ResetUserPasswordResponse> GenerateAsync(int userId, CurrentUserContext? actor, CancellationToken ct)
    {
        if (actor == null || !actor.IsAdministrador)
            throw new UnauthorizedAccessException("Sem permissão para gerar senha temporária.");

        var requester = await context.Users.SingleOrDefaultAsync(x => x.Id == actor.Id
            && x.ClinicaId == actor.ClinicaId && x.Ativo, ct);
        var target = await context.Users.SingleOrDefaultAsync(x => x.Id == userId
            && x.ClinicaId == actor.ClinicaId && x.Ativo, ct);
        if (requester == null || !Perfil.IsAdministradorOuSuper(requester.PerfilId))
            throw new UnauthorizedAccessException("Sem permissão para gerar senha temporária.");

        if (target == null) throw new KeyNotFoundException("Usuário não encontrado ou indisponível.");

        var requesterMembership = await GlobalIdentityService.EnsureForUserAsync(context, requester, ct);
        var membership = await GlobalIdentityService.EnsureForUserAsync(context, target, ct);
        var global = membership.UsuarioGlobal;
        // Global credentials can affect every membership: inspect hierarchy without exposing other tenants.
        var hasHigherPrivilege = requester.PerfilId != Perfil.SuperAdministradorId
            && (target.PerfilId == Perfil.SuperAdministradorId
                || await context.Users.IgnoreQueryFilters().AnyAsync(x => x.PerfilId == Perfil.SuperAdministradorId
                    && x.Email.Trim().ToLower() == global.Email, ct)
                || await context.UsuariosClinicas.IgnoreQueryFilters()
                .AnyAsync(x => x.UsuarioGlobalId == global.Id && x.User.PerfilId == Perfil.SuperAdministradorId, ct));
        if (!requesterMembership.Ativo || !requesterMembership.UsuarioGlobal.Ativo || !membership.Ativo || !global.Ativo || hasHigherPrivilege)
        {
            Audit(requesterMembership.UsuarioGlobalId, target, "TemporaryPassword.GenerationDenied", clock.GetUtcNow().UtcDateTime, actor.Id, false);
            await context.SaveChangesAsync(ct);
            throw new UnauthorizedAccessException("Sem permissão para gerar senha temporária.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var credential = await context.TemporaryAccessCredentials.SingleOrDefaultAsync(x => x.UsuarioGlobalId == global.Id, ct);
        if (credential != null && credential.CreatedAtUtc > now.AddSeconds(-30))
            throw new InvalidOperationException("Aguarde 30 segundos antes de gerar outra senha temporária.");
        if (credential == null)
        {
            credential = new TemporaryAccessCredential { UsuarioGlobalId = global.Id };
            context.TemporaryAccessCredentials.Add(credential);
        }
        else if (credential.RevokedAtUtc == null)
        {
            Audit(requesterMembership.UsuarioGlobalId, target, "TemporaryPassword.Revoked", now, actor.Id);
        }

        var password = TemporaryPasswordGenerator.Generate();
        credential.Id = Guid.NewGuid();
        credential.UserId = target.Id;
        credential.ClinicaId = target.ClinicaId;
        credential.PasswordHash = hasher.HashPassword(password);
        credential.CreatedAtUtc = now;
        credential.ExpiresAtUtc = now.AddMinutes(5);
        credential.UsedAtUtc = null;
        credential.RevokedAtUtc = null;
        credential.CreatedByUserId = actor.Id;
        global.SecurityVersion = Guid.NewGuid();
        global.TemporaryPasswordRecovery = true;
        await RevokeSessionsAndResetTokensAsync(global.Id, now, ct);
        Audit(requesterMembership.UsuarioGlobalId, target, "TemporaryPassword.Generated", now, actor.Id);
        await SaveAsync(ct);
        return new ResetUserPasswordResponse
        {
            Id = target.Id, PrecisaTrocarSenha = true, SenhaTemporaria = password,
            ExpiresAtUtc = credential.ExpiresAtUtc,
            Message = "Senha temporária gerada. Válida por 5 minutos e de uso único."
        };
    }

    public async Task<GlobalAuthenticationContext?> AuthenticateAsync(User user, UsuarioClinica membership, string password, CancellationToken ct)
    {
        var scopedMembership = await context.UsuariosClinicas.Include(x => x.UsuarioGlobal)
            .SingleOrDefaultAsync(x => x.Id == membership.Id && x.UserId == user.Id && x.ClinicaId == user.ClinicaId, ct);
        if (scopedMembership == null) return null;
        membership = scopedMembership;
        var global = membership.UsuarioGlobal;
        if (!global.TemporaryPasswordRecovery || !global.Ativo || !membership.Ativo)
            return null;
        var credential = await context.TemporaryAccessCredentials.SingleOrDefaultAsync(x => x.UsuarioGlobalId == global.Id
            && x.UserId == user.Id && x.ClinicaId == user.ClinicaId, ct);
        if (credential == null || credential.UsedAtUtc != null || credential.RevokedAtUtc != null
            || !hasher.VerifyPassword(password, credential.PasswordHash))
            return null;
        var now = clock.GetUtcNow().UtcDateTime;
        if (now >= credential.ExpiresAtUtc)
        {
            Audit(global.Id, user, "TemporaryPassword.Expired", now, user.Id, false);
            await context.SaveChangesAsync(ct);
            return null;
        }
        credential.UsedAtUtc = now;
        // Concurrent generation, consumption and completion compete on the global security version.
        global.SecurityVersion = Guid.NewGuid();
        Audit(global.Id, user, "TemporaryPassword.Used", now, user.Id);
        try { await context.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return null; }
        return new GlobalAuthenticationContext(global, membership);
    }

    public async Task<ChangePasswordResponse> CompleteAsync(ChangeTemporaryPasswordCommand request, CancellationToken ct)
    {
        var actor = request.CurrentUser ?? throw new UnauthorizedAccessException();
        var user = await context.Users.SingleOrDefaultAsync(x => x.Id == actor.Id && x.ClinicaId == actor.ClinicaId && x.Ativo, ct)
            ?? throw new UnauthorizedAccessException();
        var membership = await context.UsuariosClinicas.Include(x => x.UsuarioGlobal)
            .SingleOrDefaultAsync(x => x.UserId == user.Id && x.Ativo, ct) ?? throw new UnauthorizedAccessException();
        var global = membership.UsuarioGlobal;
        var credential = await context.TemporaryAccessCredentials.SingleOrDefaultAsync(x => x.UsuarioGlobalId == global.Id, ct);
        if (!global.Ativo || !global.TemporaryPasswordRecovery || global.SecurityVersion != request.SecurityVersion
            || credential == null || credential.UserId != user.Id || credential.ClinicaId != actor.ClinicaId
            || credential.UsedAtUtc == null || credential.RevokedAtUtc != null)
            throw new UnauthorizedAccessException("Sessão de recuperação inválida. Autentique-se novamente.");
        PasswordCommandRules.ValidatePasswordChangeCandidate(request.NovaSenha);
        if (hasher.VerifyPassword(request.NovaSenha, global.Senha) || hasher.VerifyPassword(request.NovaSenha, credential.PasswordHash))
            throw new InvalidOperationException("A nova senha deve ser diferente das senhas anteriores.");
        var now = clock.GetUtcNow().UtcDateTime;
        PasswordCommandMutations.ApplyNewPassword(user, hasher, request.NovaSenha, false, now);
        global.Senha = user.Senha;
        global.DataAtualizacao = now;
        global.TemporaryPasswordRecovery = false;
        global.SecurityVersion = Guid.NewGuid();
        credential.RevokedAtUtc = now;
        var linkedUsers = await context.Users.IgnoreQueryFilters().Where(x => context.UsuariosClinicas
            .Any(m => m.UsuarioGlobalId == global.Id && m.UserId == x.Id)).ToListAsync(ct);
        foreach (var linkedUser in linkedUsers) linkedUser.PrecisaTrocarSenha = false;
        await RevokeSessionsAndResetTokensAsync(global.Id, now, ct);
        Audit(global.Id, user, "TemporaryPassword.Completed", now, user.Id);
        await SaveAsync(ct);
        return new ChangePasswordResponse { Id = user.Id, PrecisaTrocarSenha = false, Message = "Senha alterada com sucesso. Entre com sua nova senha." };
    }

    private async Task RevokeSessionsAndResetTokensAsync(int globalId, DateTime now, CancellationToken ct)
    {
        var sessions = await context.AuthenticationSessions.Where(x => x.UsuarioGlobalId == globalId && x.RevokedAt == null).ToListAsync(ct);
        foreach (var session in sessions) session.RevokedAt = now;
        var tokens = await context.PasswordResetTokens.IgnoreQueryFilters().Where(x => x.UsedAt == null
            && context.UsuariosClinicas.IgnoreQueryFilters().Any(m => m.UserId == x.UserId && m.UsuarioGlobalId == globalId)).ToListAsync(ct);
        foreach (var token in tokens) token.UsedAt = now;
    }

    private void Audit(int globalId, User target, string action, DateTime now, int actorId, bool success = true)
    {
        context.AuditoriasPlataforma.Add(new AuditoriaPlataforma
        {
            UsuarioGlobalId = globalId, UserId = actorId, ClinicaId = target.ClinicaId,
            Acao = action, Recurso = "TemporaryAccessCredential", EntidadeId = target.Id.ToString(),
            DataCadastro = now, Sucesso = success
        });
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await context.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("A recuperação foi alterada por outra operação. Tente novamente.");
        }
    }
}
