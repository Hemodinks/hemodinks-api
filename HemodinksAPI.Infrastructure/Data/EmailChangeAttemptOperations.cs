using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Infrastructure.Data;

/// <summary>
/// Operações atômicas das tentativas de confirmação da alteração de email (#322). Em banco relacional,
/// um UPDATE com a condição no WHERE serializa requisições concorrentes; no InMemory dos testes de endpoint,
/// que não suporta ExecuteUpdate, usa a entidade rastreada (mesmo padrão de RegisterOperatorPinFailureAsync).
/// </summary>
public static class EmailChangeAttemptOperations
{
    public static async Task<bool> TryReserveAsync(DbContext context, Guid requestId, int maxAttempts, CancellationToken ct)
    {
        var eligible = context.Set<EmailChangeRequest>()
            .Where(x => x.Id == requestId && x.UsedAt == null && x.ConfirmationAttempts < maxAttempts);
        if (context.Database.IsRelational())
            return await eligible.ExecuteUpdateAsync(s => s.SetProperty(x => x.ConfirmationAttempts, x => x.ConfirmationAttempts + 1), ct) > 0;

        var request = await eligible.SingleOrDefaultAsync(ct);
        if (request == null) return false;
        request.ConfirmationAttempts++;
        await context.SaveChangesAsync(ct);
        return true;
    }

    public static async Task<bool> InvalidateExhaustedAsync(DbContext context, Guid requestId, int maxAttempts, DateTime now, CancellationToken ct)
    {
        var exhausted = context.Set<EmailChangeRequest>()
            .Where(x => x.Id == requestId && x.UsedAt == null && x.ConfirmationAttempts >= maxAttempts);
        if (context.Database.IsRelational())
            return await exhausted.ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedAt, now), ct) > 0;

        var request = await exhausted.SingleOrDefaultAsync(ct);
        if (request == null) return false;
        request.UsedAt = now;
        await context.SaveChangesAsync(ct);
        return true;
    }
}
