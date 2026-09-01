using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Features.Users.Commands;

internal static class FirstAccessInvitation
{
    public static async Task<bool> TrySendAsync(
        IPasswordResetDbContext context,
        IPasswordResetNotificationSender? notificationSender,
        User user,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (notificationSender == null || user.Email.EndsWith("@hemodinks.local", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var now = DateTime.UtcNow;
        var token = PasswordResetRules.GenerateToken();
        var tokenEntity = PasswordCommandMutations.CreatePasswordResetToken(
            user.ClinicaId,
            user.Id,
            token,
            null,
            now);

        await PasswordCommandMutations.InvalidateActiveTokensAsync(
            context,
            user.Id,
            now,
            cancellationToken);
        context.PasswordResetTokens.Add(tokenEntity);
        await context.SaveChangesAsync(cancellationToken);

        try
        {
            await notificationSender.SendAsync(
                new PasswordResetNotification(
                    user.Email,
                    user.Nome,
                    token,
                    tokenEntity.ExpiresAt,
                    user.ClinicaId),
                cancellationToken);
            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(
                exception,
                "Nao foi possivel enviar o convite de primeiro acesso para o usuario {UserId}",
                user.Id);
            await PasswordCommandMutations.InvalidateActiveTokensAsync(
                context,
                user.Id,
                DateTime.UtcNow,
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return false;
        }
    }
}
