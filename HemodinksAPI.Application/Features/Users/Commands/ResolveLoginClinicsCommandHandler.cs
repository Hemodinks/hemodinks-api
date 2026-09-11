using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Utils;
using MediatR;
using Microsoft.EntityFrameworkCore;

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
        var normalizedEmail = GlobalIdentityService.NormalizeEmail(request.Email);
        var maskedEmail = HemodinksAPI.Application.Security.SensitiveDataMasking.MaskEmail(normalizedEmail);

        var users = await _context.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(item => item.Clinica)
            .Where(item => item.Ativo
                && item.Clinica.Ativa
                && item.Email.ToLower() == normalizedEmail)
            .ToListAsync(cancellationToken);

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
        var options = new Dictionary<int, LoginClinicOptionDto>();
        var failureGlobalIds = new HashSet<int>();

        foreach (var user in users)
        {
            var membership = memberships.FirstOrDefault(item =>
                item.UserId == user.Id
                && item.ClinicaId == user.ClinicaId
                && item.Ativo
                && item.UsuarioGlobal.Ativo);

            var credentialValid = false;

            if (membership != null)
            {
                failureGlobalIds.Add(membership.UsuarioGlobalId);

                if (await _loginProtection.IsLockedAsync(membership.UsuarioGlobalId, cancellationToken))
                {
                    continue;
                }

                var global = membership.UsuarioGlobal;
                if (global.TemporaryPasswordRecovery)
                {
                    var temporary = await _context.TemporaryAccessCredentials
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(item => item.UsuarioGlobalId == global.Id
                            && item.UserId == user.Id
                            && item.ClinicaId == user.ClinicaId
                            && item.UsedAtUtc == null
                            && item.RevokedAtUtc == null,
                            cancellationToken);

                    credentialValid = temporary != null
                        && now < temporary.ExpiresAtUtc
                        && _passwordHasher.VerifyPassword(request.Senha, temporary.PasswordHash);
                }
                else
                {
                    credentialValid = _passwordHasher.VerifyPassword(request.Senha, global.Senha)
                        || (!global.DataAtualizacao.HasValue
                            && _passwordHasher.VerifyPassword(request.Senha, user.Senha));
                }
            }
            else
            {
                // Compatibilidade com usuários ainda não migrados para a identidade global.
                // O vínculo será criado pelo fluxo de autenticação tenant-scoped existente.
                credentialValid = _passwordHasher.VerifyPassword(request.Senha, user.Senha);
            }

            if (!credentialValid)
            {
                continue;
            }

            options[user.ClinicaId] = new LoginClinicOptionDto(
                user.ClinicaId,
                user.Clinica.Nome,
                user.Clinica.Slug);
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
            "Contexto de login resolvido para {MaskedEmail}: {ClinicCount} clínica(s) autorizada(s)",
            maskedEmail,
            options.Count);

        return new ResolveLoginClinicsResponse(options.Values
            .OrderBy(item => item.Nome)
            .ToArray());
    }
}
