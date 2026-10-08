using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Infrastructure.Data;

public sealed class SqlDatabaseReadinessProbe(
    ISqlConnectionFactory connections,
    Func<AppDbContext> schemaContextFactory,
    ILogger<SqlDatabaseReadinessProbe>? logger = null) : IDatabaseReadinessProbe
{
    public async Task<DatabaseReadinessResult> CheckAsync(bool validateSchema, CancellationToken cancellationToken)
    {
        var phase = "connection";
        var phaseTimer = Stopwatch.StartNew();
        double connectionMs = 0, queryMs = 0, schemaMs = 0;
        var failure = "none";
        try
        {
            await using var connection = connections.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            connectionMs = phaseTimer.Elapsed.TotalMilliseconds;
            phase = "query";
            phaseTimer.Restart();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.CommandTimeout = 3;
            await command.ExecuteScalarAsync(cancellationToken);
            queryMs = phaseTimer.Elapsed.TotalMilliseconds;

            IReadOnlyList<string> pendingMigrations = [];
            // Resolve EF only for deployments that still require runtime schema validation.
            if (validateSchema)
            {
                phase = "schema";
                phaseTimer.Restart();
                var context = schemaContextFactory();
                if (context.Database.IsRelational())
                {
                    var pending = await context.Database.GetPendingMigrationsAsync(cancellationToken);
                    pendingMigrations = pending.ToList();
                }
                schemaMs = phaseTimer.Elapsed.TotalMilliseconds;
            }
            phase = "dispose";
            return new(true, pendingMigrations);
        }
        catch (Exception exception)
        {
            failure = FailureCode(exception);
            throw;
        }
        finally
        {
            switch (phase)
            {
                case "connection": connectionMs = phaseTimer.Elapsed.TotalMilliseconds; break;
                case "query": queryMs = phaseTimer.Elapsed.TotalMilliseconds; break;
                case "schema": schemaMs = phaseTimer.Elapsed.TotalMilliseconds; break;
            }
            // Diagnostics must not change readiness; never include exception payloads or SQL credentials.
            try
            {
                logger?.Log(failure == "none" ? LogLevel.Information : LogLevel.Warning,
                    "DatabaseReadinessProbe {Phase} {FailureCode}; connection {ConnectionMs} ms; query {QueryMs} ms; schema {SchemaMs} ms; trace {TraceId}",
                    failure == "none" ? "complete" : phase, failure, connectionMs, queryMs, schemaMs, Activity.Current?.TraceId.ToString());
            }
            catch (Exception) { }
        }
    }

    internal static string FailureCode(Exception exception) => exception switch
    {
        OperationCanceledException => "cancelled",
        TimeoutException => "timeout",
        SqlException sql => sql.Number switch
        {
            -2 => "timeout",
            18456 => "authentication_failed",
            229 => "permission_denied",
            40615 => "firewall_denied",
            40613 => "database_unavailable",
            40501 => "capacity_or_throttling",
            10060 or 10061 or 11001 => "network_unavailable",
            _ => "sql_failure"
        },
        _ => "unavailable"
    };
}
