using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Authentication;

/// <summary>
/// Servico para gerar tokens JWT.
/// </summary>
public interface IJwtTokenService
{
    string GenerateToken(User user);
}
