using System.Diagnostics;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Hemodinks.SchemaGuard;

internal enum SchemaState
{
    Matches = 0,
    PendingKnownMigrations = 10,
    InconsistentHistory = 3
}

internal static class SchemaGuardProgram
{
    public static async Task<int> Main()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("Schema check blocked: database connection is not configured.");
            return 2;
        }

        var elapsed = Stopwatch.StartNew();
        var stage = "Configuration";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(connectionString, sql => sql.CommandTimeout(30)).Options;

            var state = SchemaState.InconsistentHistory;

            await SqlReadRetry.ExecuteAsync(async cancellationToken =>
            {
                // Each attempt owns a fresh context/connection. This operation only reads migration metadata.
                await using var context = new AppDbContext(options, new ClinicaContext());

                stage = "OpeningSqlConnection";
                Console.WriteLine($"Schema check stage={stage} elapsedMs={elapsed.ElapsedMilliseconds}");
                await context.Database.OpenConnectionAsync(cancellationToken);

                stage = "ReadingMigrationHistory";
                Console.WriteLine($"Schema check stage={stage} elapsedMs={elapsed.ElapsedMilliseconds}");

                var knownMigrations = context.Database.GetMigrations().ToArray();
                var appliedMigrations = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();

                // Automatic migration is allowed only when the database history is an exact prefix
                // of the migrations compiled into this commit. This excludes unknown, missing and
                // out-of-order migrations and keeps the deployment fail-closed.
                if (appliedMigrations.Length > knownMigrations.Length)
                {
                    state = SchemaState.InconsistentHistory;
                    return true;
                }

                for (var index = 0; index < appliedMigrations.Length; index++)
                {
                    if (!string.Equals(appliedMigrations[index], knownMigrations[index], StringComparison.Ordinal))
                    {
                        state = SchemaState.InconsistentHistory;
                        return true;
                    }
                }

                state = appliedMigrations.Length == knownMigrations.Length
                    ? SchemaState.Matches
                    : SchemaState.PendingKnownMigrations;

                return true;
            }, timeout.Token, (attempt, error) =>
                Console.WriteLine($"Schema check retryAfterAttempt={attempt} stage={stage} elapsedMs={elapsed.ElapsedMilliseconds} {SqlFailureDiagnostic.Describe(error)}"));

            switch (state)
            {
                case SchemaState.Matches:
                    Console.WriteLine("Database migration history matches this commit. No database changes are required.");
                    return 0;

                case SchemaState.PendingKnownMigrations:
                    Console.WriteLine("Database migration history is a valid prefix of this commit. Known pending migrations are ready to be applied by the deployment workflow.");
                    return 10;

                default:
                    Console.Error.WriteLine("Deploy blocked: database migration history is inconsistent with this commit (unknown, missing or out-of-order migration). No changes were applied.");
                    return 3;
            }
        }
        catch (Exception error)
        {
            // Do not expose connection strings, SQL details or server names in CI logs.
            Console.Error.WriteLine($"Deploy blocked: stage={stage} elapsedMs={elapsed.ElapsedMilliseconds} {SqlFailureDiagnostic.Describe(error)}. No changes were applied.");
            return 2;
        }
    }
}
