namespace HemodinksAPI.Infrastructure.Data;

public partial class AppDbContext
{
    public Task<bool> TryReserveEmailChangeConfirmationAttemptAsync(Guid requestId, int maxAttempts, CancellationToken cancellationToken) =>
        EmailChangeAttemptOperations.TryReserveAsync(this, requestId, maxAttempts, cancellationToken);

    public Task<bool> InvalidateExhaustedEmailChangeRequestAsync(Guid requestId, int maxAttempts, DateTime now, CancellationToken cancellationToken) =>
        EmailChangeAttemptOperations.InvalidateExhaustedAsync(this, requestId, maxAttempts, now, cancellationToken);
}
