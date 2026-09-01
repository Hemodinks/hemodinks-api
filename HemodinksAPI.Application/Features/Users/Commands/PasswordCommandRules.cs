using System.Security.Cryptography;
using System.Text;

namespace HemodinksAPI.Application.Features.Users.Commands;

internal static class PasswordCommandRules
{
    private static readonly byte[] RetiredSharedCredentialHash =
        Convert.FromHexString("A2CA37FE6FDC490B8F7CE841E1701A169D2B1697C6B5B5C63F94ABB8F9B6D6DD");

    public static void ValidatePasswordChangeCandidate(string? newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            throw new InvalidOperationException("A nova senha deve ter pelo menos 8 caracteres");
        }

        var candidateHash = SHA256.HashData(Encoding.UTF8.GetBytes(newPassword));
        if (CryptographicOperations.FixedTimeEquals(candidateHash, RetiredSharedCredentialHash))
        {
            throw new InvalidOperationException("A nova senha informada nao pode ser utilizada");
        }
    }
}
