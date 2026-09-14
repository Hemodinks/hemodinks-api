namespace HemodinksAPI.Application.Data;

public interface IDatabaseReadinessProbe
{
    Task<DatabaseReadinessResult> CheckAsync(bool validateSchema, CancellationToken cancellationToken);
}

public sealed record DatabaseReadinessResult(bool Connected, IReadOnlyList<string> PendingMigrations);
