using HemodinksAPI.Api.Services;

namespace HemodinksAPI.Api.HostedServices;

public class EventNotificationHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EventNotificationHostedService> _logger;

    public EventNotificationHostedService(IServiceProvider serviceProvider, ILogger<EventNotificationHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EventNotificationHostedService iniciado");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<IEventReminderProcessor>();
                var processedCount = await processor.ProcessDueRemindersAsync(stoppingToken);

                if (processedCount > 0)
                {
                    _logger.LogInformation("Processados {Count} lembretes de eventos", processedCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro no processamento das notificacoes de eventos");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
