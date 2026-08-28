using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Users.Commands;

/// <summary>
/// Resolve a clinica proprietaria de um reset antes da execucao idempotente.
/// O token e a credencial publica deste fluxo e nunca e exposto ao cliente em forma de hash.
/// </summary>
public sealed class PasswordResetTenantResolver(
    IAppDbContext context,
    ClinicaContext clinicaContext)
{
    public async Task ResolveAsync(string token, CancellationToken cancellationToken)
    {
        var tokenHash = PasswordResetRules.HashToken(token);
        var tokenClinic = await context.PasswordResetTokens
            .AsNoTracking()
            .Where(item => item.TokenHash == tokenHash)
            .Select(item => new { item.ClinicaId, item.Clinica.Slug })
            .FirstOrDefaultAsync(cancellationToken);

        if (tokenClinic == null)
        {
            throw new InvalidOperationException("Token de reset invalido ou expirado");
        }

        clinicaContext.SetCurrent(tokenClinic.ClinicaId, tokenClinic.Slug);
    }
}
