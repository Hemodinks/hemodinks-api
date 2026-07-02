using HemodinksAPI.Application.Data;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Users.Commands;

internal static class PasswordCommandQueries
{
    public static Task<User?> GetActiveUserByEmailAsync(
        IAppDbContext context,
        string email,
        CancellationToken cancellationToken)
    {
        return context.Users
            .FirstOrDefaultAsync(u => u.Email == email && u.Ativo, cancellationToken);
    }

    public static Task<PasswordResetToken?> GetValidResetTokenAsync(
        IAppDbContext context,
        string token,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var tokenHash = PasswordResetRules.HashToken(token);

        return context.PasswordResetTokens
            .Include(item => item.User)
            .FirstOrDefaultAsync(item =>
                item.TokenHash == tokenHash
                && item.UsedAt == null
                && item.ExpiresAt > now
                && item.User.Ativo,
                cancellationToken);
    }
}
