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

    public static async Task<IReadOnlyList<PasswordResetToken>> InvalidateActiveTokensAsync(
        IPasswordResetDbContext context,
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
        return activeTokens;
    }

    public static async Task RevokeSessionsAndResetTokensAsync(
        ICredentialRevocationDbContext context,
        int globalId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        // Global credential revocation spans only memberships belonging to this identity.
        // The caller persists these changes together with the password and security version.
        var sessions = await context.AuthenticationSessions
            .Where(x => x.UsuarioGlobalId == globalId && x.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions) session.RevokedAt = now;

        var tokens = await context.PasswordResetTokens.IgnoreQueryFilters()
            .Where(x => x.UsedAt == null && context.UsuariosClinicas.IgnoreQueryFilters()
                .Any(m => m.UserId == x.UserId && m.UsuarioGlobalId == globalId))
            .ToListAsync(cancellationToken);
        foreach (var token in tokens) token.UsedAt = now;
    }
}
