using HemodinksAPI.Api.Models;

namespace HemodinksAPI.Api.Authentication;

/// <summary>
/// Servico para gerar tokens JWT.
/// </summary>
public interface IJwtTokenService
{
    string GenerateToken(User user);
}
