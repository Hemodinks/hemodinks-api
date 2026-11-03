using HemodinksAPI.Infrastructure.Services;

namespace HemodinksAPI.Infrastructure.HostedServices;

public sealed class EventEmailDeliveryHostedService(IServiceProvider services, RequestActivitySignal activity,
    EventEmailPollingOptions options, ILogger<EventEmailDeliveryHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TryProcessOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError("Falha no processamento de emails da agenda: {ErrorType}", exception.GetType().Name);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    // Deliveries are only enqueued by authenticated requests and retries are due within minutes,
    // so an idle replica skips the poll instead of touching (and resuming) the database.
    public async Task<bool> TryProcessOnceAsync(CancellationToken cancellationToken)
    {
        if (options.RequireRecentAuthenticatedRequest && !activity.IsActiveWithin(options.RecentRequestWindow))
            return false;

        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<EventEmailDeliveryProcessor>().ProcessAsync(cancellationToken);
        return true;
    }
}
