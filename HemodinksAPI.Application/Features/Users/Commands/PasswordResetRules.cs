using System.Security.Cryptography;
using System.Text;

namespace HemodinksAPI.Application.Features.Users.Commands;

internal static class PasswordResetRules
{
    private const int TokenBytes = 32;

    public static string GenerateToken()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(TokenBytes));
    }

    public static string HashToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Token de reset obrigatorio");
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim()));
        return Convert.ToHexString(bytes);
    }

    public static void ValidateNewPassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            throw new InvalidOperationException("A nova senha deve ter pelo menos 8 caracteres");
        }

    }

    public static RequestPasswordResetResponse CreateRequestResponse()
    {
        return new RequestPasswordResetResponse
        {
            Message = "Se o email estiver cadastrado, enviaremos as instrucoes para redefinir a senha."
        };
    }

    public static string? TrimRequestIp(string? requestIp)
    {
        return string.IsNullOrWhiteSpace(requestIp) ? null : requestIp.Trim()[..Math.Min(requestIp.Trim().Length, 45)];
    }
}
