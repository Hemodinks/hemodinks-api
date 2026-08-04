using System.Security.Cryptography;
using System.Text;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Authentication;

public static class EquipeAuthenticationRules
{
    public static string NormalizeModo(string? modo)
    {
        var normalized = string.IsNullOrWhiteSpace(modo) ? EquipeModosIdentificacao.Pin : modo.Trim();
        return EquipeModosIdentificacao.Todos.FirstOrDefault(item => item.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Modo de identificacao da equipe invalido");
    }

    public static string GenerateChallengeToken()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    }

    public static string HashChallengeToken(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim()))).ToLowerInvariant();
    }

    public static string GeneratePin()
    {
        return RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
    }

    public static bool IsValidPinFormat(string? pin)
    {
        return pin is { Length: 6 } && pin.All(char.IsDigit);
    }
}
