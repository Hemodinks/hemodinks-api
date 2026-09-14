using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Infrastructure.Data;

public sealed class SqlDatabaseReadinessProbe(
    ISqlConnectionFactory connections,
    Func<AppDbContext> schemaContextFactory) : IDatabaseReadinessProbe
{
    public async Task<DatabaseReadinessResult> CheckAsync(bool validateSchema, CancellationToken cancellationToken)
    {
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        command.CommandTimeout = 3;
        await command.ExecuteScalarAsync(cancellationToken);

        // Resolve EF only for deployments that still require runtime schema validation.
        if (validateSchema)
        {
            var context = schemaContextFactory();
            if (context.Database.IsRelational())
            {
                var pending = await context.Database.GetPendingMigrationsAsync(cancellationToken);
                return new(true, pending.ToList());
            }
        }
        return new(true, []);
    }
}
