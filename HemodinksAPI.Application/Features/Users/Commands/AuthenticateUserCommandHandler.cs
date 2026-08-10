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
    private readonly IAppDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ILicencaService _licencaService;
    private readonly IClinicaContext _clinicaContext;
    private readonly ILogger<AuthenticateUserCommandHandler> _logger;

    public AuthenticateUserCommandHandler(
        IAppDbContext context,
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
            logger)
    {
    }

    public AuthenticateUserCommandHandler(
        IAppDbContext context,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        ILicencaService licencaService,
        IClinicaContext clinicaContext,
        ILogger<AuthenticateUserCommandHandler> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _licencaService = licencaService;
        _clinicaContext = clinicaContext;
        _logger = logger;
    }

    public async Task<AuthenticateUserResponse> Handle(AuthenticateUserCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var currentClinicaId = _clinicaContext.GetRequiredClinicaId();
            _logger.LogInformation("Autenticando usuario: {Email}", request.Email);

            var user = await _context.Users
                .Include(u => u.Perfil)
                .Include(u => u.Clinica)
                .Where(u => u.Email == request.Email && u.Ativo)
                .OrderByDescending(u => _context.Equipes.Any(equipe => equipe.UsuarioLoginId == u.Id && equipe.Ativa))
                .ThenBy(u => u.Id)
                .FirstOrDefaultAsync(cancellationToken);

            var globalAuthentication = user == null
                ? null
                : await GlobalIdentityService.AuthenticateAsync(
                    _context,
                    _passwordHasher,
                    user,
                    request.Senha,
                    cancellationToken);

            if (user == null || globalAuthentication == null)
            {
                _logger.LogWarning("Falha na autenticacao para: {Email}", request.Email);
                throw new UnauthorizedAccessException("Email ou senha invalidos");
            }

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

            _logger.LogInformation("Usuario autenticado com sucesso: {Email}", request.Email);

            return new AuthenticateUserResponse
            {
                Id = user.Id,
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
                PrecisaTrocarSenha = user.PrecisaTrocarSenha,
                PerfilId = user.PerfilId,
                PerfilNome = UserProfileRules.GetPerfilNome(user),
                ModulosLiberados = ClinicaModulos.GetEffective(user.Clinica.Plano, user.Clinica.ModulosLiberados),
                Licenca = licenca,
                EquipeDesafio = equipeDesafio
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao autenticar usuario: {Email}", request.Email);
            throw;
        }
    }
}
