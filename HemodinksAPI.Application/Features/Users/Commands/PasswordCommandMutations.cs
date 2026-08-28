using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Utils;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Users.Commands;

internal static class PasswordCommandMutations
{
    public static void ApplyTemporaryPassword(
        User user,
        IPasswordHasher passwordHasher,
        string temporaryPassword,
        DateTime now)
    {
        user.Senha = passwordHasher.HashPassword(temporaryPassword);
        user.PrecisaTrocarSenha = true;
        user.DataAtualizacao = now;
    }

    public static void ApplyNewPassword(
        User user,
        IPasswordHasher passwordHasher,
        string newPassword,
        bool requirePasswordChange,
        DateTime now)
    {
        user.Senha = passwordHasher.HashPassword(newPassword);
        user.PrecisaTrocarSenha = requirePasswordChange;
        user.DataAtualizacao = now;
    }

    public static PasswordResetToken CreatePasswordResetToken(
        int clinicaId,
        int userId,
        string token,
        string? requestIp,
        DateTime now)
    {
        return new PasswordResetToken
        {
            ClinicaId = clinicaId,
            UserId = userId,
            TokenHash = PasswordResetRules.HashToken(token),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(30),
            RequestIp = PasswordResetRules.TrimRequestIp(requestIp)
        };
    }

    public static async Task InvalidateActiveTokensAsync(
        IAppDbContext context,
        int userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var activeTokens = await context.PasswordResetTokens
            .Where(item => item.UserId == userId
                && item.UsedAt == null
                && item.ExpiresAt > now)
            .ToListAsync(cancellationToken);

        foreach (var activeToken in activeTokens)
        {
            activeToken.UsedAt = now;
        }
    }
}
