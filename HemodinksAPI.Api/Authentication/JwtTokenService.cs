using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HemodinksAPI.Api.Models;
using Microsoft.IdentityModel.Tokens;

namespace HemodinksAPI.Api.Authentication;

/// <summary>
/// Serviço para gerar e validar tokens JWT
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    /// Gera um token JWT para um usuário
    /// </summary>
    string GenerateToken(User user);
}

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
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(_jwtSettings.SecretKey);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Name, user.Nome),
                new Claim("cpf", user.Cpf ?? string.Empty),
                new Claim(ClaimTypes.Role, user.Perfil?.Nome ?? string.Empty),
                new Claim("perfilId", user.PerfilId.ToString()),
                new Claim("perfilNome", user.Perfil?.Nome ?? string.Empty),
                new Claim("precisaTrocarSenha", user.PrecisaTrocarSenha.ToString().ToLowerInvariant()),
            };

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
            _logger.LogError(ex, "Erro ao gerar token JWT para usuário {UserId}", user.Id);
            throw;
        }
    }
}
