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

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(connectionString, sql => sql.CommandTimeout(30)).Options;
            await using var context = new AppDbContext(options, new ClinicaContext());
            if (!await SchemaHistoryGuard.CheckAsync(context, timeout.Token))
            {
                Console.Error.WriteLine("Deploy blocked: database migration history differs from this commit (pending, unknown or missing migrations). No changes were applied. Review the SQL; use migrate-and-deploy only when schema changes are authorized.");
                return 3;
            }
            Console.WriteLine("Database migration history matches this commit. No database changes were applied.");
            return 0;
        }
        catch (Exception)
        {
            // Do not expose connection strings, SQL details or server names in CI logs.
            Console.Error.WriteLine("Deploy blocked: unable to verify database history. Check connectivity and permissions. No changes were applied.");
            return 2;
        }
    }
}
