using HemodinksAPI.Application.Features.Clinics.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HemodinksAPI.Infrastructure.HostedServices;

public sealed record ActiveSessionsSnapshotOptions(TimeSpan Interval, TimeSpan RecentRequestWindow);

/// <summary>
/// Registra periodicamente um resumo agregado das sessoes ativas para observabilidade externa
/// (encaminhamento de logs do agente New Relic). So contagens e ClinicaId: nunca nome, e-mail,
/// perfil, IP ou navegador. A lista nominal continua restrita ao SuperAdministrador na aplicacao.
/// </summary>
public sealed class ActiveSessionsSnapshotHostedService(
    IServiceProvider services,
    RequestActivitySignal activity,
    ActiveSessionsSnapshotOptions options,
    ILogger<ActiveSessionsSnapshotHostedService> logger) : BackgroundService
{
    public static readonly EventId SnapshotEvent = new(4300, "ActiveSessionsSnapshot");
    public static readonly EventId ClinicSnapshotEvent = new(4301, "ActiveSessionsSnapshotClinica");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(options.Interval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }

            try
            {
                await TryRecordOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError("Falha ao registrar snapshot de sessoes ativas: {ErrorType}", exception.GetType().Name);
            }
        }
    }

    // Sem requisicao autenticada recente nao ha sessao ativa a contar: a replica ociosa nao toca
    // (nem acorda) o banco so para registrar zero.
    public async Task<bool> TryRecordOnceAsync(CancellationToken cancellationToken)
    {
        if (!activity.IsActiveWithin(options.RecentRequestWindow))
            return false;

        using var scope = services.CreateScope();
        var snapshot = await scope.ServiceProvider.GetRequiredService<PlatformActiveSessionsQueryHandler>()
            .GetSnapshotAsync(cancellationToken);

        // Warning de proposito: o encaminhamento ao New Relic descarta Information e abaixo
        // (evita levar caminhos e query strings de requisicoes). Nao e alerta; filtre pelo EventId.
        logger.LogWarning(SnapshotEvent,
            "ActiveSessionsSnapshot UsuariosOnline={UsuariosOnline} TotalSessoes={TotalSessoes} Clinicas={Clinicas}",
            snapshot.UsuariosOnline, snapshot.TotalSessoes, snapshot.PorClinica.Count);
        foreach (var clinic in snapshot.PorClinica)
        {
            logger.LogWarning(ClinicSnapshotEvent,
                "ActiveSessionsSnapshotClinica SnapshotClinicaId={SnapshotClinicaId} SnapshotUsuarios={SnapshotUsuarios} SnapshotSessoes={SnapshotSessoes}",
                clinic.ClinicaId, clinic.Usuarios, clinic.Sessoes);
        }

        return true;
    }
}
