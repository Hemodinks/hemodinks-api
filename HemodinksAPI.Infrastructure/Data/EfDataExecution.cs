using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HemodinksAPI.Infrastructure.Data;

public sealed class EfDataExecution(PlatformDbContext context) : IDataExecutionStrategy, IDataTransactionManager
{
    public Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(() => operation(cancellationToken));
    }

    public async Task<IDataTransaction?> BeginAsync(CancellationToken cancellationToken)
    {
        if (!context.Database.IsRelational())
        {
            return null;
        }

        return new EfDataTransaction(await context.Database.BeginTransactionAsync(cancellationToken));
    }

    private sealed class EfDataTransaction(IDbContextTransaction transaction) : IDataTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
