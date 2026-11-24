using HemodinksAPI.Application.Security;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Infrastructure.Security;

/// <summary>
/// Backfill idempotente do #227: alinha CpfCifrado/CpfHash com a coluna legada Cpf, que segue como
/// fonte da verdade ate o PR destrutivo. Cobre linhas antigas e linhas gravadas por uma imagem anterior
/// (rollback ou janela blue/green). Loga apenas contagens; nunca CPF, cifra ou hash.
/// </summary>
public sealed class CpfProtectionReconciliationService(
    IServiceScopeFactory scopeFactory,
    CpfProtectionRuntimeInfo runtimeInfo,
    DatabaseAvailabilitySignal databaseAvailability,
    CpfReconciliationSchedule schedule,
    ILogger<CpfProtectionReconciliationService> logger) : BackgroundService
{
    public static readonly EventId CpfReconciliationCompletedEvent = new(4400, "CpfReconciliationCompleted");

    private const int BatchSize = 500;
    private const int MaximumPasses = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (runtimeInfo.UsesEphemeralKeys)
        {
            logger.LogWarning("Protecao de CPF usando chaves efemeras (somente Development/Testing)");
        }

        if (!runtimeInfo.ReconcileOnStartup)
        {
            return;
        }

        try
        {
            // Nunca conecta por conta propria: espera o banco responder a trafego real (#209) e deixa as
            // primeiras requisicoes do usuario terminarem antes de varrer os CPFs.
            await databaseAvailability.WaitAsync(stoppingToken);
            await Task.Delay(schedule.StartDelay, stoppingToken);

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                    return;
                }
                catch (Exception ex) when (attempt <= schedule.RetryDelays.Count
                    && !stoppingToken.IsCancellationRequested
                    && TryGetUnavailability(ex, out var cause, out var sqlErrorNumber))
                {
                    // Indisponibilidade passageira (ex.: SQL serverless retomando): so tipo e numero do erro.
                    logger.LogWarning(
                        "Reconciliacao de CPF adiada: banco indisponivel na tentativa {Attempt}; {ErrorType} {SqlErrorNumber}",
                        attempt, cause.GetType().Name, sqlErrorNumber);
                    await Task.Delay(schedule.RetryDelays[attempt - 1], stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // Leitura segue na coluna legada; falha aqui degrada so unicidade/busca por hash ate o proximo startup.
            logger.LogError(ex, "Falha na reconciliacao da protecao de CPF");
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<ICpfProtector>();
        if (protector is DataProtectionCpfProtector dataProtection)
        {
            var rotation = dataProtection.PrepareCertificateRotation();
            if (rotation.RetiringKeys > 0)
            {
                logger.LogInformation(
                    "Protecao de CPF: {RetiringKeys} chaves de certificado anterior no anel; chave nova criada: {CreatedKey}",
                    rotation.RetiringKeys, rotation.CreatedKey);
            }
        }

        await ReconcileAsync(context, protector, logger, cancellationToken);
    }

    internal static bool TryGetUnavailability(Exception exception, out Exception cause, out int? sqlErrorNumber)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sql && SqlAvailabilityErrors.IsUnavailable(sql.Number, sql.Class))
            {
                cause = sql;
                sqlErrorNumber = sql.Number;
                return true;
            }

            if (current is TimeoutException)
            {
                cause = current;
                sqlErrorNumber = null;
                return true;
            }
        }

        cause = exception;
        sqlErrorNumber = null;
        return false;
    }

    internal static async Task<CpfReconciliationResult> ReconcileAsync(
        AppDbContext context,
        ICpfProtector protector,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var total = new CpfReconciliationResult(0, 0, 0, 0, 0);
        for (var pass = 1; pass <= MaximumPasses; pass++)
        {
            var result = await RunPassAsync(context, protector, cancellationToken);
            total = total with
            {
                Checked = total.Checked + result.Checked,
                Updated = total.Updated + result.Updated,
                Unreadable = total.Unreadable + result.Unreadable,
                Failed = result.Failed,
                PendingReprotection = result.PendingReprotection
            };

            // Conflitos de indice unico com hash obsoleto em outra linha se resolvem na passada seguinte.
            if (result.Failed == 0 || result.Updated == 0)
            {
                break;
            }
        }

        // #277: Warning de proposito, nao e alerta. O encaminhamento de logs do New Relic descarta Information,
        // e esta linha e o criterio de fim do deploy e da rotacao de certificado (docs/observability-newrelic.md).
        // So contagens, nunca CPF. Filtre pelo EventId/mensagem.
        logger.LogWarning(
            CpfReconciliationCompletedEvent,
            "Reconciliacao de CPF concluida: {Checked} verificados, {Updated} atualizados, {Unreadable} cifras ilegiveis, {Failed} falhas, {PendingReprotection} cifras pendentes de recifragem",
            total.Checked, total.Updated, total.Unreadable, total.Failed, total.PendingReprotection);
        if (total.Unreadable > 0)
        {
            logger.LogWarning(
                "Reconciliacao de CPF encontrou {Unreadable} cifras ilegiveis; verifique certificado e anel de chaves",
                total.Unreadable);
        }

        return total;
    }

    private static async Task<CpfReconciliationResult> RunPassAsync(
        AppDbContext context,
        ICpfProtector protector,
        CancellationToken cancellationToken)
    {
        var lastId = 0;
        int checkedCount = 0, updated = 0, unreadable = 0, failed = 0, pendingReprotection = 0;

        while (true)
        {
            var batch = await context.Users
                .AsNoTracking()
                .Where(user => user.Id > lastId)
                .OrderBy(user => user.Id)
                .Select(user => new { user.Id, user.Cpf, user.CpfCifrado, user.CpfHash })
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var row in batch)
            {
                checkedCount++;
                var legacyCpf = string.IsNullOrWhiteSpace(row.Cpf) ? null : row.Cpf;
                string? expectedHash = null;
                var needsUpdate = false;
                var reprotect = false;

                if (legacyCpf is null)
                {
                    needsUpdate = row.CpfCifrado is not null || row.CpfHash is not null;
                }
                else
                {
                    expectedHash = protector.ComputeLookupHash(legacyCpf);
                    var decrypted = row.CpfCifrado is null ? null : protector.TryUnprotect(row.CpfCifrado);
                    if (row.CpfCifrado is not null && decrypted is null)
                    {
                        unreadable++;
                    }

                    // Cifra feita com chave de certificado anterior: recifra com a chave padrao atual.
                    reprotect = decrypted is not null && protector.RequiresReprotection(row.CpfCifrado!);
                    needsUpdate = reprotect
                        || !string.Equals(row.CpfHash, expectedHash, StringComparison.Ordinal)
                        || !string.Equals(decrypted, legacyCpf, StringComparison.Ordinal);
                }

                if (!needsUpdate)
                {
                    continue;
                }

                var newCifrado = legacyCpf is null ? null : protector.Protect(legacyCpf);
                if (reprotect && protector.RequiresReprotection(newCifrado!))
                {
                    // A chave padrao ainda e do certificado anterior: regravar nao resolve.
                    pendingReprotection++;
                    continue;
                }

                try
                {
                    // Condicionado ao valor legado lido: uma alteracao concorrente vence (ela ja grava os tres campos).
                    var affected = await context.Users
                        .Where(user => user.Id == row.Id && user.Cpf == row.Cpf)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(user => user.CpfCifrado, newCifrado)
                            .SetProperty(user => user.CpfHash, expectedHash),
                            cancellationToken);
                    updated += affected;
                }
                catch (Exception ex) when (ex is DbUpdateException or System.Data.Common.DbException)
                {
                    failed++;
                    if (reprotect)
                    {
                        pendingReprotection++;
                    }
                }
            }

            lastId = batch[^1].Id;
        }

        return new CpfReconciliationResult(checkedCount, updated, unreadable, failed, pendingReprotection);
    }
}

public sealed record CpfReconciliationResult(int Checked, int Updated, int Unreadable, int Failed, int PendingReprotection);

/// <summary>Espera apos o banco ficar disponivel e intervalos entre novas tentativas da reconciliacao.</summary>
public sealed record CpfReconciliationSchedule(TimeSpan StartDelay, IReadOnlyList<TimeSpan> RetryDelays)
{
    public static CpfReconciliationSchedule Default { get; } =
        // 2 min: a varredura fica fora do login e das primeiras telas de quem acordou o banco.
        new(TimeSpan.FromMinutes(2), [TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)]);
}
