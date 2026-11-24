using System.Security.Cryptography;
using System.Text;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Auditing;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Security;
using HemodinksAPI.Application.Utils;
using HemodinksAPI.Domain.Exceptions;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Users.Mfa;

// #150: segredo TOTP cifrado pelo Data Protection (mesmo anel de chaves protegido por certificado do CPF,
// proposito proprio). Nunca em claro no banco, em log ou em resposta depois do setup.
public interface IMfaSecretProtector
{
    string Protect(string secret);
    string? TryUnprotect(string protectedSecret);
}

public interface IMfaDbContext : IUnitOfWork
{
    DbSet<UsuarioGlobal> UsuariosGlobais { get; }
    DbSet<UsuarioClinica> UsuariosClinicas { get; }
    DbSet<User> Users { get; }
    DbSet<MfaCodigoRecuperacao> MfaCodigosRecuperacao { get; }
    DbSet<MfaLoginDesafio> MfaLoginDesafios { get; }
}

public sealed record MfaStatusDto(bool Elegivel, bool Habilitado, DateTime? HabilitadoEm, int CodigosRecuperacaoRestantes);
public sealed record MfaSetupDto(string Segredo, string OtpAuthUri);
public sealed record MfaRecoveryCodesDto(IReadOnlyList<string> CodigosRecuperacao);
public sealed record MfaLoginChallengeDto(string Token, DateTime ExpiraEm);
public sealed record MfaVerifiedLogin(User User, GlobalAuthenticationContext Authentication);

public sealed class MfaService(
    IMfaDbContext db,
    IMfaSecretProtector protector,
    IPasswordHasher passwordHasher,
    ILoginAccountProtection loginProtection,
    TimeProvider timeProvider,
    ILogger<MfaService> logger,
    IPlatformAuditWriter? audit = null)
{
    public const string Issuer = "HemoDinks";
    public const int RecoveryCodeCount = 10;
    public const int MaxChallengeAttempts = 5;
    public static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);

    // Politica por perfil (#150): MFA so para Administrador e SuperAdministrador, por adesao.
    public static bool IsEligible(int perfilId) => Perfil.IsAdministradorOuSuper(perfilId);

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    public async Task<MfaStatusDto> GetStatusAsync(CurrentUserContext user, CancellationToken ct)
    {
        var global = await LoadGlobalAsync(user, ct);
        var remaining = global.MfaHabilitado
            ? await db.MfaCodigosRecuperacao.CountAsync(item => item.UsuarioGlobalId == global.Id && item.UsadoEm == null, ct)
            : 0;
        return new MfaStatusDto(IsEligible(user.PerfilId), global.MfaHabilitado, global.MfaHabilitadoEm, remaining);
    }

    public async Task<MfaSetupDto> BeginSetupAsync(CurrentUserContext user, CancellationToken ct)
    {
        EnsureEligible(user);
        var global = await LoadGlobalAsync(user, ct);
        if (global.MfaHabilitado)
            throw new BusinessRuleException("A verificação em duas etapas já está ativa.");

        var secret = Totp.GenerateSecret();
        global.MfaSegredoPendenteProtegido = protector.Protect(secret);
        global.DataAtualizacao = UtcNow;
        await db.SaveChangesAsync(ct);
        return new MfaSetupDto(secret, Totp.BuildOtpAuthUri(Issuer, global.Email, secret));
    }

    public async Task<MfaRecoveryCodesDto> ActivateAsync(CurrentUserContext user, string? code, CancellationToken ct)
    {
        EnsureEligible(user);
        var global = await LoadGlobalAsync(user, ct);
        if (global.MfaHabilitado)
            throw new BusinessRuleException("A verificação em duas etapas já está ativa.");
        var pending = global.MfaSegredoPendenteProtegido is { } protectedPending ? protector.TryUnprotect(protectedPending) : null;
        if (pending == null)
            throw new BusinessRuleException("Inicie a configuração da verificação em duas etapas novamente.");
        if (!Totp.TryVerify(pending, code, timeProvider.GetUtcNow(), null, out var step))
        {
            await WriteAuditAsync(global, user.Id, "mfa.enable", false, ct);
            throw new BusinessRuleException("Código inválido. Confira o horário do celular e tente de novo.");
        }

        var now = UtcNow;
        global.MfaSegredoProtegido = global.MfaSegredoPendenteProtegido;
        global.MfaSegredoPendenteProtegido = null;
        global.MfaHabilitado = true;
        global.MfaHabilitadoEm = now;
        global.MfaUltimoPassoUsado = step;
        global.DataAtualizacao = now;
        var codes = ReplaceRecoveryCodes(global.Id, now);
        await db.SaveChangesAsync(ct);
        await WriteAuditAsync(global, user.Id, "mfa.enable", true, ct);
        return new MfaRecoveryCodesDto(codes);
    }

    public async Task DeactivateAsync(CurrentUserContext user, string? senha, string? code, CancellationToken ct)
    {
        var global = await LoadGlobalAsync(user, ct);
        if (!global.MfaHabilitado)
            return;
        var passwordOk = !string.IsNullOrEmpty(senha) && passwordHasher.VerifyPassword(senha, global.Senha);
        var secondFactorOk = passwordOk && await VerifySecondFactorAsync(global, code, ct);
        if (!passwordOk || !secondFactorOk)
        {
            await WriteAuditAsync(global, user.Id, "mfa.disable", false, ct);
            throw new BusinessRuleException("Senha ou código inválidos.");
        }

        ClearMfa(global);
        await db.SaveChangesAsync(ct);
        await WriteAuditAsync(global, user.Id, "mfa.disable", true, ct);
    }

    // Contingencia operacional (#150): SuperAdministrador redefine o MFA de quem perdeu o dispositivo e os
    // codigos. Encerra as sessoes do titular (SecurityVersion) e fica na auditoria.
    public async Task ResetAsync(CurrentUserContext actor, int targetUserId, CancellationToken ct)
    {
        if (actor.PerfilId != Perfil.SuperAdministradorId)
            throw new UnauthorizedAccessException("Somente SuperAdministrador redefine a verificação em duas etapas.");
        // UsuarioClinica nao passa pelo filtro de tenant: o alvo sai de Users (filtro global, fail-closed), entao
        // usuario de outra clinica e 404 sem revelar existencia.
        var target = await db.Users.FirstOrDefaultAsync(item => item.Id == targetUserId, ct)
            ?? throw new ResourceNotFoundException("Usuario nao encontrado");
        var membership = await db.UsuariosClinicas.Include(item => item.UsuarioGlobal)
            .FirstOrDefaultAsync(item => item.UserId == target.Id && item.ClinicaId == target.ClinicaId, ct)
            ?? throw new ResourceNotFoundException("Usuario nao encontrado");
        var global = membership.UsuarioGlobal;
        if (!global.MfaHabilitado && global.MfaSegredoPendenteProtegido == null)
            return;
        ClearMfa(global);
        global.SecurityVersion = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        await WriteAuditAsync(global, actor.Id, "mfa.reset", true, ct, actor.UsuarioGlobalId);
    }

    public async Task<MfaLoginChallengeDto> CreateLoginChallengeAsync(User user, GlobalAuthenticationContext authentication, CancellationToken ct)
    {
        var token = EquipeAuthenticationRules.GenerateChallengeToken();
        var expiresAt = UtcNow.Add(ChallengeLifetime);
        db.MfaLoginDesafios.Add(new MfaLoginDesafio
        {
            ClinicaId = user.ClinicaId,
            UsuarioGlobalId = authentication.UsuarioGlobal.Id,
            UserId = user.Id,
            TokenHash = EquipeAuthenticationRules.HashChallengeToken(token),
            SecurityVersion = authentication.UsuarioGlobal.SecurityVersion,
            ExpiraEm = expiresAt,
            DataCadastro = UtcNow
        });
        await db.SaveChangesAsync(ct);
        return new MfaLoginChallengeDto(token, expiresAt);
    }

    /// <summary>
    /// Conclui o login: valida desafio (clinica do contexto, prazo, uso unico, tentativas, SecurityVersion) e o
    /// segundo fator. Qualquer falha e 401 generico; falhas contam no bloqueio da conta.
    /// </summary>
    public async Task<MfaVerifiedLogin> VerifyLoginChallengeAsync(string? challengeToken, string? code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(challengeToken))
            throw new UnauthorizedAccessException("Codigo invalido");
        var hash = EquipeAuthenticationRules.HashChallengeToken(challengeToken);
        var challenge = await db.MfaLoginDesafios.FirstOrDefaultAsync(item => item.TokenHash == hash, ct);
        var now = UtcNow;
        if (challenge == null || challenge.UtilizadoEm != null || challenge.ExpiraEm <= now
            || challenge.Tentativas >= MaxChallengeAttempts)
            throw new UnauthorizedAccessException("Codigo invalido");

        var membership = await db.UsuariosClinicas.Include(item => item.UsuarioGlobal)
            .FirstOrDefaultAsync(item => item.UserId == challenge.UserId && item.UsuarioGlobalId == challenge.UsuarioGlobalId, ct);
        var user = await db.Users.Include(item => item.Perfil).Include(item => item.Clinica)
            .FirstOrDefaultAsync(item => item.Id == challenge.UserId && item.Ativo && item.Clinica.Ativa, ct);
        var global = membership?.UsuarioGlobal;
        if (membership == null || user == null || global == null || !global.Ativo || !membership.Ativo
            || !global.MfaHabilitado || global.SecurityVersion != challenge.SecurityVersion
            || await loginProtection.IsLockedAsync(global.Id, ct))
            throw new UnauthorizedAccessException("Codigo invalido");

        if (!await VerifySecondFactorAsync(global, code, ct))
        {
            challenge.Tentativas++;
            // Falhas simultaneas no mesmo desafio: a perdedora so recebe o mesmo 401 (Tentativas e token de concorrencia).
            await TrySaveAsync(ct);
            await loginProtection.RegisterFailureAsync(global.Id, ct);
            await WriteAuditAsync(global, user.Id, "mfa.failure", false, ct, clinicId: user.ClinicaId);
            throw new UnauthorizedAccessException("Codigo invalido");
        }

        challenge.UtilizadoEm = now;
        // Uso unico: duas conclusoes simultaneas do mesmo desafio (ex.: TOTP e codigo de recuperacao) -> so uma abre sessao.
        if (!await TrySaveAsync(ct))
            throw new UnauthorizedAccessException("Codigo invalido");
        await loginProtection.RegisterSuccessAsync(global.Id, ct);
        return new MfaVerifiedLogin(user, new GlobalAuthenticationContext(global, membership));
    }

    // TOTP do aplicativo ou codigo de recuperacao (consumido). Concorrencia: o token de concorrencia do passo
    // e do UsadoEm faz a segunda requisicao simultanea falhar no SaveChanges.
    private async Task<bool> VerifySecondFactorAsync(UsuarioGlobal global, string? code, CancellationToken ct)
    {
        var secret = global.MfaSegredoProtegido is { } protectedSecret ? protector.TryUnprotect(protectedSecret) : null;
        if (secret == null)
        {
            logger.LogError("Segredo de MFA ilegivel para a identidade {UsuarioGlobalId}", global.Id);
            return false;
        }

        if (Totp.TryVerify(secret, code, timeProvider.GetUtcNow(), global.MfaUltimoPassoUsado, out var step))
        {
            global.MfaUltimoPassoUsado = step;
            return await TrySaveAsync(ct);
        }

        var normalized = NormalizeRecoveryCode(code);
        if (normalized.Length != 10)
            return false;
        var codeHash = HashRecoveryCode(normalized);
        var recovery = await db.MfaCodigosRecuperacao
            .FirstOrDefaultAsync(item => item.UsuarioGlobalId == global.Id && item.CodigoHash == codeHash && item.UsadoEm == null, ct);
        if (recovery == null)
            return false;
        recovery.UsadoEm = UtcNow;
        if (!await TrySaveAsync(ct))
            return false;
        await WriteAuditAsync(global, null, "mfa.recovery_code_used", true, ct);
        return true;
    }

    private async Task<bool> TrySaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogWarning("Segundo fator recusado por uso concorrente do mesmo codigo");
            return false;
        }
    }

    private List<string> ReplaceRecoveryCodes(int globalId, DateTime now)
    {
        db.MfaCodigosRecuperacao.RemoveRange(db.MfaCodigosRecuperacao.Where(item => item.UsuarioGlobalId == globalId));
        var codes = new List<string>(RecoveryCodeCount);
        for (var index = 0; index < RecoveryCodeCount; index++)
        {
            var code = Base32.Encode(RandomNumberGenerator.GetBytes(7))[..10];
            codes.Add($"{code[..5]}-{code[5..]}");
            db.MfaCodigosRecuperacao.Add(new MfaCodigoRecuperacao
            {
                UsuarioGlobalId = globalId,
                CodigoHash = HashRecoveryCode(code),
                DataCadastro = now
            });
        }

        return codes;
    }

    private void ClearMfa(UsuarioGlobal global)
    {
        global.MfaHabilitado = false;
        global.MfaSegredoProtegido = null;
        global.MfaSegredoPendenteProtegido = null;
        global.MfaHabilitadoEm = null;
        global.MfaUltimoPassoUsado = null;
        global.DataAtualizacao = UtcNow;
        db.MfaCodigosRecuperacao.RemoveRange(db.MfaCodigosRecuperacao.Where(item => item.UsuarioGlobalId == global.Id));
    }

    private static string NormalizeRecoveryCode(string? code) =>
        new string((code ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    private static string HashRecoveryCode(string normalized) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));

    private static void EnsureEligible(CurrentUserContext user)
    {
        if (!IsEligible(user.PerfilId))
            throw new UnauthorizedAccessException("Verificação em duas etapas disponível apenas para administradores.");
    }

    private async Task<UsuarioGlobal> LoadGlobalAsync(CurrentUserContext user, CancellationToken ct)
    {
        var globalId = user.UsuarioGlobalId > 0
            ? user.UsuarioGlobalId
            : throw new UnauthorizedAccessException("Identidade global ausente da sessao.");
        return await db.UsuariosGlobais.FirstOrDefaultAsync(item => item.Id == globalId && item.Ativo, ct)
            ?? throw new UnauthorizedAccessException("Identidade global ausente da sessao.");
    }

    private async Task WriteAuditAsync(UsuarioGlobal global, int? userId, string action, bool success, CancellationToken ct,
        int? actorGlobalId = null, int? clinicId = null)
    {
        if (audit == null)
            return;
        try
        {
            await audit.WriteAsync(new PlatformAuditEntry(actorGlobalId ?? global.Id, clinicId, userId, action, "mfa",
                global.Id.ToString(), null, null, null, null, success, UtcNow), ct);
        }
        catch (Exception exception)
        {
            logger.LogError("Falha ao auditar evento de MFA {Acao}: {ErrorType}", action, exception.GetType().Name);
        }
    }
}
