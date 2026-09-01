namespace HemodinksAPI.Infrastructure.Services;

public class NotificationService : INotificationService
{
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(ILogger<NotificationService> logger)
    {
        _logger = logger;
    }

    public Task SendNotificationToUserAsync(int userId, string title, string message)
    {
        _logger.LogInformation("Notificacao processada para usuario {UserId}", userId);
        return Task.CompletedTask;
    }

    public Task SendNotificationToMedicalProfileAsync(int medicoPerfilId, string title, string message)
    {
        _logger.LogInformation("Notificacao processada para perfil medico {PerfilId}", medicoPerfilId);
        return Task.CompletedTask;
    }
}
