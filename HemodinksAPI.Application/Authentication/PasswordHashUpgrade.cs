using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Utils;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Authentication;

/// <summary>Called only after the selected clinic's password authentication checks succeed.</summary>
public static class PasswordHashUpgrade
{
    public static async Task<bool> TryUpgradeAsync(
        IGlobalIdentityDbContext context,
        IPasswordHasher hasher,
        GlobalAuthenticationContext authentication,
        string password,
        CancellationToken cancellationToken)
    {
        if (!authentication.NeedsPasswordRehash) return true;

        var global = authentication.UsuarioGlobal;
        var expectedVersion = global.SecurityVersion;
        global.Senha = hasher.HashPassword(password);
        // Senha and SecurityVersion are optimistic concurrency tokens. Only the canonical
        // credential changes; existing sessions, local copies and audit dates stay intact.
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException conflict) when (
            conflict.Entries.Count == 1 && ReferenceEquals(conflict.Entries[0].Entity, global))
        {
            await conflict.Entries[0].ReloadAsync(cancellationToken);
            // A concurrent login may have upgraded the same password. A reset/change
            // wins instead; discard our staged hash and never retry a stale write.
            return conflict.Entries[0].State != EntityState.Detached
                && global.SecurityVersion == expectedVersion
                && global.Ativo && !global.TemporaryPasswordRecovery
                && (global.BloqueadoAte == null || global.BloqueadoAte <= DateTime.UtcNow)
                && hasher.VerifyPassword(password, global.Senha);
        }
    }
}
