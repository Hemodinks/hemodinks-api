using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HemodinksAPI.Infrastructure.HostedServices;

public sealed record ClinicAuditRetentionOptions(int RetentionDays, TimeSpan Interval);

/// <summary>
/// #368: remove registros de AuditoriasClinica mais antigos que a retencao configurada
/// (AuditoriaClinica:RetencaoDias). So e registrado com retencao positiva; sem configuracao a trilha
/// e mantida. A limpeza e global (todas as clinicas) e por isso usa o PlatformDbContext. E idempotente:
/// varias replicas executando ao mesmo tempo so repetem a mesma exclusao.
/// </summary>
public sealed class ClinicAuditRetentionHostedService(
    IServiceProvider services,
    ClinicAuditRetentionOptions options,
    TimeProvider timeProvider,
    ILogger<ClinicAuditRetentionHostedService> logger) : BackgroundService
{
    private const int BatchSize = 500;
    private const int MaxBatchesPerRun = 200;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var removed = await PurgeOnceAsync(stoppingToken);
                if (removed > 0)
                    logger.LogInformation("Retencao da auditoria clinica removeu {Removidos} registros", removed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError("Falha na retencao da auditoria clinica: {ErrorType}", exception.GetType().Name);
            }

            try { await Task.Delay(options.Interval, timeProvider, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    public async Task<int> PurgeOnceAsync(CancellationToken cancellationToken)
    {
        if (options.RetentionDays <= 0)
            return 0;

        var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddDays(-options.RetentionDays);
        var removed = 0;
        for (var batch = 0; batch < MaxBatchesPerRun; batch++)
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var expired = await db.AuditoriasClinica
                .Where(item => item.DataUtc < cutoff)
                .OrderBy(item => item.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (expired.Count == 0)
                break;

            db.AuditoriasClinica.RemoveRange(expired);
            await db.SaveChangesAsync(cancellationToken);
            removed += expired.Count;
            if (expired.Count < BatchSize)
                break;
        }

        return removed;
    }
}
