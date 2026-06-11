using HemodinksAPI.Infrastructure.Data;
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

            return canConnect
                ? HealthCheckResult.Healthy("Banco conectado")
                : HealthCheckResult.Unhealthy("Banco indisponivel");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Banco indisponivel", ex);
        }
    }
}
