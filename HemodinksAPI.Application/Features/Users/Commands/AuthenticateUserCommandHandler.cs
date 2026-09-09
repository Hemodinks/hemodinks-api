using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Licencas;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Application.Utils;
using HemodinksAPI.Domain.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Users.Commands;

/// <summary>
/// Handler para autenticar usuario.
/// </summary>
public class AuthenticateUserCommandHandler : IRequestHandler<AuthenticateUserCommand, AuthenticateUserResponse>
{
    private readonly IUserFeatureDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ILicencaService _licencaService;
    private readonly IClinicaContext _clinicaContext;
    private readonly ILogger<AuthenticateUserCommandHandler> _logger;
    private readonly ILoginAccountProtection _loginProtection;
    private readonly TemporaryAccessService? _temporaryAccess;

    internal AuthenticateUserCommandHandler(
        IUserFeatureDbContext context,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        ILicencaService licencaService,
        ILogger<AuthenticateUserCommandHandler> logger)
        : this(
            context,
            passwordHasher,
            jwtTokenService,
            licencaService,
            ClinicaContextFactory.CreateDefaultResolved(),
            new NoOpLoginAccountProtection(),
            logger)
    {
    }

    public AuthenticateUserCommandHandler(
        IUserFeatureDbContext context,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        ILicencaService licencaService,
        IClinicaContext clinicaContext,
        ILoginAccountProtection loginProtection,
        ILogger<AuthenticateUserCommandHandler> logger,
        TemporaryAccessService? temporaryAccess = null)
    {
        _temporaryAccess = temporaryAccess;
        _context = context;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _licencaService = licencaService;
        _clinicaContext = clinicaContext;
        _loginProtection = loginProtection;
        _logger = logger;
    }

    public async Task<AuthenticateUserResponse> Handle(AuthenticateUserCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var currentClinicaId = _clinicaContext.GetRequiredClinicaId();
            var maskedEmail = HemodinksAPI.Application.Security.SensitiveDataMasking.MaskEmail(request.Email);
            _logger.LogInformation("Autenticando usuario: {MaskedEmail}", maskedEmail);

            var user = await _context.Users
                .Include(u => u.Perfil)
                .Include(u => u.Clinica)
                .Where(u => u.Email == request.Email && u.Ativo)
                .OrderByDescending(u => _context.Equipes.Any(equipe => equipe.UsuarioLoginId == u.Id && equipe.Ativa))
                .ThenBy(u => u.Id)
                .FirstOrDefaultAsync(cancellationToken);

            var membership = user == null
                ? null
                : await GlobalIdentityService.EnsureForUserAsync(_context, user, cancellationToken);
            if (membership != null && await _loginProtection.IsLockedAsync(membership.UsuarioGlobalId, cancellationToken))
            {
                _logger.LogWarning("Conta temporariamente bloqueada para: {MaskedEmail}", maskedEmail);
                throw new UnauthorizedAccessException("Email ou senha invalidos");
            }

            var globalAuthentication = user == null || membership!.UsuarioGlobal.TemporaryPasswordRecovery ? null : await GlobalIdentityService.AuthenticateAsync(
                    _context,
                    _passwordHasher,
                    user,
                    request.Senha,
                    cancellationToken);

            if (user != null && membership != null && globalAuthentication == null && _temporaryAccess != null)
            {
                globalAuthentication = await _temporaryAccess.AuthenticateAsync(user, membership, request.Senha, cancellationToken);
            }

            if (user == null || globalAuthentication == null)
            {
                if (membership != null)
                {
                    await _loginProtection.RegisterFailureAsync(membership.UsuarioGlobalId, cancellationToken);
                }

                _logger.LogWarning("Falha na autenticacao para: {MaskedEmail}", maskedEmail);
                throw new UnauthorizedAccessException("Email ou senha invalidos");
            }

            await _loginProtection.RegisterSuccessAsync(globalAuthentication.UsuarioGlobal.Id, cancellationToken);

            Equipe? equipe = null;
            EquipeLoginChallengeDto? equipeDesafio = null;
            string? token = null;
            if (user.PerfilId == Perfil.EquipeId)
            {
                equipe = await _context.Equipes
                    .AsNoTracking()
                    .FirstOrDefaultAsync(item => item.UsuarioLoginId == user.Id && item.Ativa, cancellationToken)
                    ?? throw new UnauthorizedAccessException("Equipe inativa ou nao configurada");

                if (!equipe.ModoIdentificacao.Equals(EquipeModosIdentificacao.Nenhuma, StringComparison.OrdinalIgnoreCase))
                {
                    var challengeToken = EquipeAuthenticationRules.GenerateChallengeToken();
                    var expiresAt = DateTime.UtcNow.AddMinutes(5);
                    _context.EquipeLoginDesafios.Add(new EquipeLoginDesafio
                    {
                        ClinicaId = user.ClinicaId,
                        EquipeId = equipe.Id,
                        TokenHash = EquipeAuthenticationRules.HashChallengeToken(challengeToken),
                        SecurityVersion = globalAuthentication.UsuarioGlobal.SecurityVersion,
                        ExpiraEm = expiresAt
                    });
                    await _context.SaveChangesAsync(cancellationToken);

                    var operadores = await _context.EquipeOperadores
                        .AsNoTracking()
                        .Where(item => item.EquipeId == equipe.Id
                            && item.Ativo
                            && item.User.Ativo
                            && _context.EquipeMembros.Any(membro => membro.EquipeId == equipe.Id
                                && membro.UserId == item.UserId
                                && membro.Ativo))
                        .OrderBy(item => item.User.Nome)
                        .Select(item => new EquipeOperadorLoginDto(
                            item.Id,
                            item.User.Nome,
                            equipe.ModoIdentificacao == EquipeModosIdentificacao.Pin && item.PinHash != null))
                        .ToListAsync(cancellationToken);

                    equipeDesafio = new EquipeLoginChallengeDto(
                        challengeToken,
                        equipe.Id,
                        equipe.Nome,
                        equipe.ModoIdentificacao,
                        expiresAt,
                        operadores);
                }
                else
                {
                    token = _jwtTokenService.GenerateToken(
                        globalAuthentication.UsuarioGlobal,
                        globalAuthentication.UsuarioClinica,
                        user,
                        equipe);
                }
            }
            else
            {
                token = _jwtTokenService.GenerateToken(
                    globalAuthentication.UsuarioGlobal,
                    globalAuthentication.UsuarioClinica,
                    user);
            }
            var licenca = await _licencaService.GetCurrentAsync(
                new CurrentUserContext(
                    user.Id,
                    user.PerfilId,
                    user.Nome,
                    user.ClinicaId,
                    user.Clinica.Slug,
                    globalAuthentication.UsuarioGlobal.Id,
                    globalAuthentication.UsuarioClinica.Id),
                cancellationToken);

            _logger.LogInformation("Usuario autenticado com sucesso: {MaskedEmail}", maskedEmail);

            return new AuthenticateUserResponse
            {
                Id = user.Id,
                SecurityVersion = globalAuthentication.UsuarioGlobal.SecurityVersion,
                UsuarioGlobalId = globalAuthentication.UsuarioGlobal.Id,
                ClinicaId = currentClinicaId,
                ClinicaSlug = user.Clinica.Slug,
                Nome = user.Nome,
                Email = globalAuthentication.UsuarioGlobal.Email,
                Token = token,
                Cpf = user.Cpf,
                Crm = user.Crm,
                CrmUf = user.CrmUf,
                FotoPerfil = user.FotoPerfil,
                PrecisaTrocarSenha = user.PrecisaTrocarSenha || globalAuthentication.UsuarioGlobal.TemporaryPasswordRecovery,
                PerfilId = user.PerfilId,
                PerfilNome = UserProfileRules.GetPerfilNome(user),
                ModulosLiberados = ClinicaModulos.GetEffective(user.Clinica.Plano, user.Clinica.ModulosLiberados),
                Licenca = licenca,
                EquipeDesafio = equipeDesafio
            };
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("Autenticacao recusada devido a alteracao concorrente de credenciais");
            throw new UnauthorizedAccessException("Email ou senha invalidos");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao autenticar usuario");
            throw;
        }
    }
}
