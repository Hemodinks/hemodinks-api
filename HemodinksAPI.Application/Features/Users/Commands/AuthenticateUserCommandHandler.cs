using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Licencas;
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
    private readonly ILogger<AuthenticateUserCommandHandler> _logger;

    public AuthenticateUserCommandHandler(
        IAppDbContext context,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        ILicencaService licencaService,
        ILogger<AuthenticateUserCommandHandler> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _licencaService = licencaService;
        _logger = logger;
    }

    public async Task<AuthenticateUserResponse> Handle(AuthenticateUserCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Autenticando usuario: {Email}", request.Email);

            var user = await _context.Users
                .Include(u => u.Perfil)
                .FirstOrDefaultAsync(u => u.Email == request.Email && u.Ativo, cancellationToken);

            if (user == null || !_passwordHasher.VerifyPassword(request.Senha, user.Senha))
            {
                _logger.LogWarning("Falha na autenticacao para: {Email}", request.Email);
                throw new UnauthorizedAccessException("Email ou senha invalidos");
            }

            var token = _jwtTokenService.GenerateToken(user);
            var licenca = await _licencaService.GetCurrentAsync(
                new CurrentUserContext(user.Id, user.PerfilId, user.Nome),
                cancellationToken);

            _logger.LogInformation("Usuario autenticado com sucesso: {Email}", request.Email);

            return new AuthenticateUserResponse
            {
                Id = user.Id,
                Nome = user.Nome,
                Email = user.Email,
                Token = token,
                Cpf = user.Cpf,
                Crm = user.Crm,
                CrmUf = user.CrmUf,
                FotoPerfil = user.FotoPerfil,
                PrecisaTrocarSenha = user.PrecisaTrocarSenha,
                PerfilId = user.PerfilId,
                PerfilNome = UserProfileRules.GetPerfilNome(user),
                Licenca = licenca
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao autenticar usuario: {Email}", request.Email);
            throw;
        }
    }
}
