using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using Microsoft.IdentityModel.Tokens;

namespace HemodinksAPI.Infrastructure.Authentication;

/// <summary>
/// Implementação do serviço JWT
/// </summary>
public class JwtTokenService : IJwtTokenService
{
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<JwtTokenService> _logger;

    public JwtTokenService(JwtSettings jwtSettings, ILogger<JwtTokenService> logger)
    {
        _jwtSettings = jwtSettings;
        _logger = logger;
    }

    public string GenerateToken(User user)
    {
        var legacyIdentity = new UsuarioGlobal
        {
            Id = user.Id,
            Nome = user.Nome,
            Email = user.Email,
            Senha = user.Senha,
            Ativo = user.Ativo
        };
        var legacyMembership = new UsuarioClinica
        {
            Id = user.Id,
            UsuarioGlobalId = user.Id,
            ClinicaId = user.ClinicaId,
            UserId = user.Id,
            PerfilId = user.PerfilId,
            Ativo = user.Ativo
        };

        return GenerateToken(legacyIdentity, legacyMembership, user);
    }

    public string GenerateToken(User user, Guid? sessionId)
    {
        var globalIdentity = new UsuarioGlobal
        {
            Id = user.Id,
            Nome = user.Nome,
            Email = user.Email,
            Senha = user.Senha,
            Ativo = user.Ativo
        };
        var clinicMembership = new UsuarioClinica
        {
            Id = user.Id,
            UsuarioGlobalId = user.Id,
            ClinicaId = user.ClinicaId,
            UserId = user.Id,
            PerfilId = user.PerfilId,
            Ativo = user.Ativo
        };

        return GenerateToken(globalIdentity, clinicMembership, user, sessionId);
    }

    public string GenerateToken(
        UsuarioGlobal usuarioGlobal,
        UsuarioClinica usuarioClinica,
        User user,
        Guid? sessionId)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(_jwtSettings.SecretKey);

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, usuarioGlobal.Email),
                new Claim(ClaimTypes.Name, usuarioGlobal.Nome),
                new Claim(GlobalIdentityClaimTypes.UsuarioGlobalId, usuarioGlobal.Id.ToString()),
                new Claim(GlobalIdentityClaimTypes.UsuarioClinicaId, usuarioClinica.Id.ToString()),
                new Claim("cpf", user.Cpf ?? string.Empty),
                new Claim(ClaimTypes.Role, user.Perfil?.Nome ?? string.Empty),
                new Claim("perfilId", usuarioClinica.PerfilId.ToString()),
                new Claim("perfilNome", user.Perfil?.Nome ?? string.Empty),
                new Claim(ClinicaClaimTypes.ClinicaId, usuarioClinica.ClinicaId.ToString()),
                new Claim(ClinicaClaimTypes.ClinicaSlug, user.Clinica?.Slug ?? Clinica.DefaultSlug),
                new Claim("precisaTrocarSenha", user.PrecisaTrocarSenha.ToString().ToLowerInvariant()),
            };

            if (sessionId.HasValue)
            {
                claims.Add(new Claim(AuthenticationSessionClaimTypes.SessionId, sessionId.Value.ToString("D")));
            }

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes),
                Issuer = _jwtSettings.Issuer,
                Audience = _jwtSettings.Audience,
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(key),
                    SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Erro ao gerar token JWT para identidade {UsuarioGlobalId} na clinica {ClinicaId}",
                usuarioGlobal.Id,
                usuarioClinica.ClinicaId);
            throw;
        }
    }

    public string GenerateToken(
        UsuarioGlobal usuarioGlobal,
        UsuarioClinica usuarioClinica,
        User user,
        Equipe? equipe = null,
        EquipeOperador? operador = null,
        bool identificacaoConfiavel = false)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(_jwtSettings.SecretKey);

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, usuarioGlobal.Email),
                new Claim(ClaimTypes.Name, usuarioGlobal.Nome),
                new Claim(GlobalIdentityClaimTypes.UsuarioGlobalId, usuarioGlobal.Id.ToString()),
                new Claim(GlobalIdentityClaimTypes.UsuarioClinicaId, usuarioClinica.Id.ToString()),
                new Claim("cpf", user.Cpf ?? string.Empty),
                new Claim(ClaimTypes.Role, user.Perfil?.Nome ?? string.Empty),
                new Claim("perfilId", usuarioClinica.PerfilId.ToString()),
                new Claim("perfilNome", user.Perfil?.Nome ?? string.Empty),
                new Claim(ClinicaClaimTypes.ClinicaId, usuarioClinica.ClinicaId.ToString()),
                new Claim(ClinicaClaimTypes.ClinicaSlug, user.Clinica?.Slug ?? Clinica.DefaultSlug),
                new Claim("precisaTrocarSenha", user.PrecisaTrocarSenha.ToString().ToLowerInvariant()),
            };

            if (equipe != null)
            {
                claims.Add(new Claim(GlobalIdentityClaimTypes.EquipeId, equipe.Id.ToString()));
                claims.Add(new Claim(GlobalIdentityClaimTypes.EquipeVersaoSessao, equipe.VersaoSessao.ToString()));
                claims.Add(new Claim(GlobalIdentityClaimTypes.IdentificacaoConfiavel, identificacaoConfiavel.ToString().ToLowerInvariant()));
            }

            if (operador != null)
            {
                var precisaTrocarPin = equipe?.ModoIdentificacao.Equals(
                    EquipeModosIdentificacao.Pin,
                    StringComparison.OrdinalIgnoreCase) == true
                    && operador.PrecisaTrocarPin;
                claims.Add(new Claim(GlobalIdentityClaimTypes.EquipeOperadorId, operador.Id.ToString()));
                claims.Add(new Claim(GlobalIdentityClaimTypes.OperadorVersaoSessao, operador.VersaoSessao.ToString()));
                claims.Add(new Claim("precisaTrocarPin", precisaTrocarPin.ToString().ToLowerInvariant()));
            }

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes),
                Issuer = _jwtSettings.Issuer,
                Audience = _jwtSettings.Audience,
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(key),
                    SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Erro ao gerar token JWT para identidade {UsuarioGlobalId} na clinica {ClinicaId}",
                usuarioGlobal.Id,
                usuarioClinica.ClinicaId);
            throw;
        }
    }
}
