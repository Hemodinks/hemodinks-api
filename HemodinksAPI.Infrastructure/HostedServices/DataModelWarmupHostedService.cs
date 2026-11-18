using System.Diagnostics;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Infrastructure.HostedServices;

/// <summary>
/// Monta no arranque o modelo do EF Core dos contextos usados no login, para que a primeira
/// requisicao de login nao pague esse custo depois de a replica sair do zero.
/// So CPU: nao abre conexao, nao executa SQL, nao le dados e nao acorda o SQL serverless.
/// O /api/warmup continua sem resolver contexto de tenant; este aquecimento corre fora da requisicao.
/// </summary>
public sealed class DataModelWarmupHostedService(
    IServiceProvider services,
    ILogger<DataModelWarmupHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Nao segura o arranque do host: o aquecimento corre em paralelo com o primeiro trafego.
        await Task.Yield();
        var timer = Stopwatch.StartNew();
        try
        {
            WarmUp(services, stoppingToken);
            logger.LogInformation("DataModelWarmupCompleted {WarmupDurationMs}", timer.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            // Otimizacao apenas: a falha nao derruba o processo. Nunca registrar a mensagem da excecao.
            logger.LogWarning("DataModelWarmupFailed {WarmupDurationMs} {ErrorType}",
                timer.Elapsed.TotalMilliseconds, exception.GetType().Name);
        }
    }

    internal static void WarmUp(IServiceProvider services, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var scope = services.CreateScope();

        // Cada tipo de contexto tem o proprio modelo em cache; o login usa os dois.
        var platform = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        TranslateWithoutExecuting(platform);
        cancellationToken.ThrowIfCancellationRequested();

        var tenant = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        TranslateWithoutExecuting(tenant);
    }

    // ToQueryString monta o modelo e passa pelo pipeline de traducao LINQ -> SQL sem tocar o banco.
    private static void TranslateWithoutExecuting(AppDbContext context) =>
        _ = context.UsuariosGlobais.AsNoTracking().Select(user => user.Id).ToQueryString();
}
