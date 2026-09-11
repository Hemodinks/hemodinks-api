using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Utils;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace HemodinksAPI.Application.Features.Users.Commands;

/// <summary>
/// Resolve somente as clínicas nas quais a credencial informada é válida.
/// Esta etapa não cria sessão nem concede acesso a APIs de negócio.
/// </summary>
public sealed class ResolveLoginClinicsCommandHandler : IRequestHandler<ResolveLoginClinicsCommand, ResolveLoginClinicsResponse>
{
    private readonly ITemporaryAccessDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILoginAccountProtection _loginProtection;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ResolveLoginClinicsCommandHandler> _logger;

    public ResolveLoginClinicsCommandHandler(
        ITemporaryAccessDbContext context,
        IPasswordHasher passwordHasher,
        ILoginAccountProtection loginProtection,
        TimeProvider timeProvider,
        ILogger<ResolveLoginClinicsCommandHandler> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _loginProtection = loginProtection;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ResolveLoginClinicsResponse> Handle(
        ResolveLoginClinicsCommand request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalizedEmail = GlobalIdentityService.NormalizeEmail(request.Email);
        var maskedEmail = HemodinksAPI.Application.Security.SensitiveDataMasking.MaskEmail(normalizedEmail);

        var users = await _context.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.Ativo
                && item.Clinica.Ativa
                && item.Email.ToLower() == normalizedEmail)
            .Select(item => new { item.Id, item.ClinicaId, item.Senha, ClinicName = item.Clinica.Nome, ClinicSlug = item.Clinica.Slug })
            .ToListAsync(cancellationToken);
        var userLookupMs = stopwatch.Elapsed.TotalMilliseconds;

        if (users.Count == 0)
        {
            _logger.LogWarning("Falha ao resolver contexto de login para {MaskedEmail}", maskedEmail);
            throw new UnauthorizedAccessException("Email ou senha invalidos");
        }

        var userIds = users.Select(item => item.Id).ToArray();
        var memberships = await _context.UsuariosClinicas
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(item => item.UsuarioGlobal)
            .Where(item => userIds.Contains(item.UserId))
            .ToListAsync(cancellationToken);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var membershipLookupMs = stopwatch.Elapsed.TotalMilliseconds - userLookupMs;
        var membershipLookup = memberships.ToLookup(item => (item.UserId, item.ClinicaId));
        var recoveryGlobalIds = memberships.Where(item => item.UsuarioGlobal.TemporaryPasswordRecovery)
            .Select(item => item.UsuarioGlobalId).Distinct().ToArray();
        var temporaryCredentials = recoveryGlobalIds.Length == 0
            ? []
            : await _context.TemporaryAccessCredentials.IgnoreQueryFilters().AsNoTracking()
                .Where(item => recoveryGlobalIds.Contains(item.UsuarioGlobalId)
                    && item.UsedAtUtc == null && item.RevokedAtUtc == null && now < item.ExpiresAtUtc)
                .ToListAsync(cancellationToken);
        var temporaryLookup = temporaryCredentials.ToDictionary(item => item.UsuarioGlobalId);
        // Request-local reuse: never cache passwords, hashes or authentication decisions across requests.
        var lockedAccounts = new Dictionary<int, bool>();
        var verifiedHashes = new Dictionary<string, bool>(StringComparer.Ordinal);
        bool VerifyPassword(string hash)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!verifiedHashes.TryGetValue(hash, out var valid))
                verifiedHashes[hash] = valid = _passwordHasher.VerifyPassword(request.Senha, hash);
            return valid;
        }
        var options = new Dictionary<int, LoginClinicOptionDto>();
        var failureGlobalIds = new HashSet<int>();

        foreach (var user in users)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var membership = membershipLookup[(user.Id, user.ClinicaId)].FirstOrDefault(item =>
                item.Ativo
                && item.UsuarioGlobal.Ativo);

            var credentialValid = false;

            if (membership != null)
            {
                failureGlobalIds.Add(membership.UsuarioGlobalId);

                if (!lockedAccounts.TryGetValue(membership.UsuarioGlobalId, out var locked))
                    lockedAccounts[membership.UsuarioGlobalId] = locked = await _loginProtection.IsLockedAsync(membership.UsuarioGlobalId, cancellationToken);
                if (locked)
                {
                    continue;
                }

                var global = membership.UsuarioGlobal;
                if (global.TemporaryPasswordRecovery)
                {
                    credentialValid = temporaryLookup.TryGetValue(global.Id, out var temporary)
                        && temporary.UserId == user.Id && temporary.ClinicaId == user.ClinicaId
                        && VerifyPassword(temporary.PasswordHash);
                }
                else
                {
                    credentialValid = VerifyPassword(global.Senha)
                        || (!global.DataAtualizacao.HasValue
                            && VerifyPassword(user.Senha));
                }
            }
            else
            {
                // Compatibilidade com usuários ainda não migrados para a identidade global.
                // O vínculo será criado pelo fluxo de autenticação tenant-scoped existente.
                credentialValid = VerifyPassword(user.Senha);
            }

            if (!credentialValid)
            {
                continue;
            }

            options[user.ClinicaId] = new LoginClinicOptionDto(
                user.ClinicaId,
                user.ClinicName,
                user.ClinicSlug);
        }

        if (options.Count == 0)
        {
            foreach (var globalId in failureGlobalIds)
            {
                await _loginProtection.RegisterFailureAsync(globalId, cancellationToken);
            }

            _logger.LogWarning("Credencial inválida ao resolver contexto de login para {MaskedEmail}", maskedEmail);
            throw new UnauthorizedAccessException("Email ou senha invalidos");
        }

        _logger.LogInformation(
            "Contexto de login resolvido para {MaskedEmail}: {ClinicCount} clínica(s) autorizada(s) em {ElapsedMs} ms. Usuarios: {UserLookupMs} ms; vinculos: {MembershipLookupMs} ms; hashes verificados: {HashVerificationCount}",
            maskedEmail,
            options.Count, stopwatch.Elapsed.TotalMilliseconds, userLookupMs, membershipLookupMs, verifiedHashes.Count);

        return new ResolveLoginClinicsResponse(options.Values
            .OrderBy(item => item.Nome)
            .ToArray());
    }
}
