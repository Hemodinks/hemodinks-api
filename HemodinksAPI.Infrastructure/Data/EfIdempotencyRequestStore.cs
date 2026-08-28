using HemodinksAPI.Application.Idempotency;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Infrastructure.Data;

public sealed class EfIdempotencyRequestStore(AppDbContext context) : IIdempotencyRequestStore
{
    public Task<IdempotencyRequest?> FindAsync(
        int clinicaId,
        string operation,
        string scope,
        string key,
        CancellationToken cancellationToken)
    {
        return context.IdempotencyRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.ClinicaId == clinicaId
                && item.Operation == operation
                && item.Scope == scope
                && item.IdempotencyKey == key,
                cancellationToken);
    }

    public async Task<bool> TryAddAsync(IdempotencyRequest request, CancellationToken cancellationToken)
    {
        context.IdempotencyRequests.Add(request);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            context.Entry(request).State = EntityState.Detached;
            return false;
        }
    }

    public Task CompleteAsync(IdempotencyRequest request, CancellationToken cancellationToken)
    {
        context.IdempotencyRequests.Update(request);
        return context.SaveChangesAsync(cancellationToken);
    }

    public Task RemoveAsync(IdempotencyRequest request, CancellationToken cancellationToken)
    {
        context.IdempotencyRequests.Remove(request);
        return context.SaveChangesAsync(cancellationToken);
    }
}
