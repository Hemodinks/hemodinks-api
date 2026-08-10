using HemodinksAPI.Domain.Utils;

namespace HemodinksAPI.Application.Features.Users.Commands;

internal static class PasswordCommandRules
{
    public static void ValidatePasswordChangeCandidate(string? newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            throw new InvalidOperationException("A nova senha deve ter pelo menos 8 caracteres");
        }

        if (newPassword == DefaultUserPassword.Value)
        {
            throw new InvalidOperationException("A nova senha nao pode ser a senha padrao");
        }
    }
}
