using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Idempotency;

public interface IIdempotencyRequestStore
{
    Task<IdempotencyRequest?> FindAsync(
        int clinicaId,
        string operation,
        string scope,
        string key,
        CancellationToken cancellationToken);

    Task<bool> TryAddAsync(IdempotencyRequest request, CancellationToken cancellationToken);

    Task CompleteAsync(IdempotencyRequest request, CancellationToken cancellationToken);

    Task RemoveAsync(IdempotencyRequest request, CancellationToken cancellationToken);
}
