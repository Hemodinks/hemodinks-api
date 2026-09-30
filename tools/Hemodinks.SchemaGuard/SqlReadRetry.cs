using Microsoft.Data.SqlClient;

namespace Hemodinks.SchemaGuard;

// Only for the read-only deployment guard. Never wrap a migration or deployment in this policy.
public static class SqlReadRetry
{
    public static bool IsTransientSqlNumber(int number) => number is
        -2 or 64 or 233 or 10053 or 10054 or 10060 or 40197 or 40613 or 40501 or 10928 or 10929;

    public static async Task<bool> ExecuteAsync(
        Func<CancellationToken, Task<bool>> read,
        CancellationToken cancellationToken,
        Action<int, Exception>? onRetry = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        delay ??= Task.Delay;
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await read(cancellationToken);
            }
            catch (Exception error) when (attempt < 3 && !cancellationToken.IsCancellationRequested &&
                (error is TimeoutException || error is SqlException sql && IsTransientSqlNumber(sql.Number)))
            {
                onRetry?.Invoke(attempt, error);
                await delay(TimeSpan.FromSeconds(10), cancellationToken);
            }
        }
    }
}
