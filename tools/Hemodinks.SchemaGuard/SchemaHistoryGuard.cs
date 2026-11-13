using Microsoft.EntityFrameworkCore;

namespace Hemodinks.SchemaGuard;

public static class SchemaHistoryGuard
{
    public static bool Matches(IEnumerable<string> expected, IEnumerable<string> applied)
    {
        var normalized = NormalizeApplied(applied);
        if (normalized is null) return false;
        var expectedIds = expected.Order(StringComparer.Ordinal).ToArray();
        var appliedIds = normalized.Order(StringComparer.Ordinal).ToArray();
        return expectedIds.Length > 0 && expectedIds.SequenceEqual(appliedIds, StringComparer.Ordinal);
    }

    /// <summary>
    /// Remove do histórico os IDs consolidados pela baseline (#231), preservando a ordem.
    /// Retorna null (histórico inconsistente) se só parte deles estiver presente ou se faltar o ID da baseline.
    /// </summary>
    public static string[]? NormalizeApplied(IEnumerable<string> applied)
    {
        var ids = applied.ToArray();
        var consolidated = ids.Count(ConsolidatedMigrations.Ids.Contains);
        if (consolidated == 0) return ids;
        if (consolidated != ConsolidatedMigrations.Ids.Count
            || !ids.Contains(ConsolidatedMigrations.BaselineId, StringComparer.Ordinal))
            return null;
        return ids.Where(id => !ConsolidatedMigrations.Ids.Contains(id)).ToArray();
    }

    public static async Task<bool> CheckAsync(DbContext context, CancellationToken cancellationToken, Action<string>? onStage = null)
    {
        // Read migration metadata only. Never create, migrate or seed a database.
        onStage?.Invoke("OpeningSqlConnection");
        await context.Database.OpenConnectionAsync(cancellationToken);
        onStage?.Invoke("ReadingMigrationHistory");
        var expected = context.Database.GetMigrations();
        var applied = await context.Database.GetAppliedMigrationsAsync(cancellationToken);
        return Matches(expected, applied);
    }
}
