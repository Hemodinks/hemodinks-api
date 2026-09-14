using System.Diagnostics;
using HemodinksAPI.Application.Data;
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
            var diagnostics = scope.ServiceProvider.GetService<StartupDiagnostics>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<DatabaseHealthCheck>>();
            var probeTimer = Stopwatch.StartNew();
            var canConnect = false;
            var ready = false;
            try
            {
                var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                var probe = scope.ServiceProvider.GetRequiredService<IDatabaseReadinessProbe>();
                var result = await probe.CheckAsync(
                    !configuration.GetValue<bool>("Database:SchemaManagedByDeployment"), cancellationToken);
                canConnect = result.Connected;

                if (!canConnect)
                {
                    return HealthCheckResult.Unhealthy("Banco indisponivel");
                }

                if (result.PendingMigrations.Count > 0)
                {
                    return HealthCheckResult.Unhealthy(
                        "Banco com migrations pendentes",
                        data: new Dictionary<string, object>
                        {
                            ["pendingMigrations"] = result.PendingMigrations
                        });
                }

                ready = true;
                return HealthCheckResult.Healthy("Banco conectado");
            }
            finally
            {
                diagnostics?.RecordDatabaseProbe(logger, probeTimer.Elapsed.TotalMilliseconds, canConnect);
                if (ready)
                {
                    diagnostics?.RecordFirstReady(logger);
                }
            }
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Banco indisponivel", ex);
        }
    }
}
