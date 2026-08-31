using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Infrastructure.Authentication;

public sealed class EfLoginAccountProtection(
    AppDbContext context,
    IOptions<Application.Authentication.LoginAccountProtectionOptions> options,
    TimeProvider timeProvider) : Application.Authentication.ILoginAccountProtection
{
    private readonly Application.Authentication.LoginAccountProtectionOptions _options = options.Value;

    public Task<bool> IsLockedAsync(int usuarioGlobalId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return context.UsuariosGlobais.AsNoTracking().AnyAsync(
            item => item.Id == usuarioGlobalId && item.BloqueadoAte > now,
            cancellationToken);
    }

    public async Task RegisterFailureAsync(int usuarioGlobalId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var attemptCutoff = now.AddMinutes(-_options.AttemptWindowMinutes);
        var lockoutEnd = now.AddMinutes(_options.LockoutMinutes);
        var maximumAttempts = _options.MaximumFailedAttempts;

        if (!context.Database.IsRelational())
        {
            var account = await context.UsuariosGlobais.SingleAsync(
                item => item.Id == usuarioGlobalId,
                cancellationToken);
            if (account.BloqueadoAte > now)
            {
                return;
            }

            account.TentativasLoginFalhas = account.UltimaFalhaLoginEm >= attemptCutoff
                ? account.TentativasLoginFalhas + 1
                : 1;
            account.UltimaFalhaLoginEm = now;
            account.BloqueadoAte = account.TentativasLoginFalhas >= maximumAttempts ? lockoutEnd : null;
            await context.SaveChangesAsync(cancellationToken);
            return;
        }

        await context.UsuariosGlobais
            .Where(item => item.Id == usuarioGlobalId
                && (!item.BloqueadoAte.HasValue || item.BloqueadoAte <= now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    item => item.TentativasLoginFalhas,
                    item => item.UltimaFalhaLoginEm.HasValue && item.UltimaFalhaLoginEm >= attemptCutoff
                        ? item.TentativasLoginFalhas + 1
                        : 1)
                .SetProperty(
                    item => item.BloqueadoAte,
                    item => (item.UltimaFalhaLoginEm.HasValue && item.UltimaFalhaLoginEm >= attemptCutoff
                        ? item.TentativasLoginFalhas + 1
                        : 1) >= maximumAttempts
                            ? lockoutEnd
                            : null)
                .SetProperty(item => item.UltimaFalhaLoginEm, now),
                cancellationToken);
    }

    public async Task RegisterSuccessAsync(int usuarioGlobalId, CancellationToken cancellationToken)
    {
        if (!context.Database.IsRelational())
        {
            var account = await context.UsuariosGlobais.SingleAsync(
                item => item.Id == usuarioGlobalId,
                cancellationToken);
            if (account.TentativasLoginFalhas == 0
                && account.UltimaFalhaLoginEm == null
                && account.BloqueadoAte == null)
            {
                return;
            }

            account.TentativasLoginFalhas = 0;
            account.UltimaFalhaLoginEm = null;
            account.BloqueadoAte = null;
            await context.SaveChangesAsync(cancellationToken);
            return;
        }

        await context.UsuariosGlobais
            .Where(item => item.Id == usuarioGlobalId
                && (item.TentativasLoginFalhas != 0
                    || item.UltimaFalhaLoginEm != null
                    || item.BloqueadoAte != null))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.TentativasLoginFalhas, 0)
                .SetProperty(item => item.UltimaFalhaLoginEm, (DateTime?)null)
                .SetProperty(item => item.BloqueadoAte, (DateTime?)null),
                cancellationToken);
    }
}
