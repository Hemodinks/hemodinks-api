using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HemodinksAPI.Api;

public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly IServiceScopeFactory _scopeFactory;

    public DatabaseHealthCheck(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

            if (!canConnect)
            {
                return HealthCheckResult.Unhealthy("Banco indisponivel");
            }

            if (dbContext.Database.IsRelational())
            {
                var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync(cancellationToken);
                var pendingMigrationsList = pendingMigrations.ToList();

                if (pendingMigrationsList.Count > 0)
                {
                    return HealthCheckResult.Unhealthy(
                        "Banco com migrations pendentes",
                        data: new Dictionary<string, object>
                        {
                            ["pendingMigrations"] = pendingMigrationsList
                        });
                }
            }

            return HealthCheckResult.Healthy("Banco conectado e atualizado");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Banco indisponivel", ex);
        }
    }
}
