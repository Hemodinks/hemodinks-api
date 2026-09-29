using Microsoft.EntityFrameworkCore;

namespace Hemodinks.SchemaGuard;

public static class SchemaHistoryGuard
{
    public static bool Matches(IEnumerable<string> expected, IEnumerable<string> applied)
    {
        var expectedIds = expected.Order(StringComparer.Ordinal).ToArray();
        var appliedIds = applied.Order(StringComparer.Ordinal).ToArray();
        return expectedIds.Length > 0 && expectedIds.SequenceEqual(appliedIds, StringComparer.Ordinal);
    }

    public static async Task<bool> CheckAsync(DbContext context, CancellationToken cancellationToken)
    {
        // Read migration metadata only. Never create, migrate or seed a database.
        await context.Database.OpenConnectionAsync(cancellationToken);
        var expected = context.Database.GetMigrations();
        var applied = await context.Database.GetAppliedMigrationsAsync(cancellationToken);
        return Matches(expected, applied);
    }
}
