using System.Diagnostics;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Hemodinks.SchemaGuard;

internal static class SchemaGuardProgram
{
    public static async Task<int> Main()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("Schema check blocked: homologation connection is not configured.");
            return 2;
        }

        var elapsed = Stopwatch.StartNew();
        var stage = "Configuration";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(connectionString, sql => sql.CommandTimeout(30)).Options;
            var matches = await SqlReadRetry.ExecuteAsync(async cancellationToken =>
            {
                // Each attempt owns a fresh context/connection. This operation only reads metadata.
                await using var context = new AppDbContext(options, new ClinicaContext());
                return await SchemaHistoryGuard.CheckAsync(context, cancellationToken, next =>
                {
                    stage = next;
                    Console.WriteLine($"Schema check stage={stage} elapsedMs={elapsed.ElapsedMilliseconds}");
                });
            }, timeout.Token, (attempt, error) =>
                Console.WriteLine($"Schema check retryAfterAttempt={attempt} stage={stage} elapsedMs={elapsed.ElapsedMilliseconds} {SqlFailureDiagnostic.Describe(error)}"));
            if (!matches)
            {
                Console.Error.WriteLine("Deploy blocked: database migration history differs from this commit (pending, unknown or missing migrations). No changes were applied. Review the SQL; use migrate-and-deploy only when schema changes are authorized.");
                return 3;
            }
            Console.WriteLine("Database migration history matches this commit. No database changes were applied.");
            return 0;
        }
        catch (Exception error)
        {
            // Do not expose connection strings, SQL details or server names in CI logs.
            Console.Error.WriteLine($"Deploy blocked: stage={stage} elapsedMs={elapsed.ElapsedMilliseconds} {SqlFailureDiagnostic.Describe(error)}. No changes were applied.");
            return 2;
        }
    }
}
