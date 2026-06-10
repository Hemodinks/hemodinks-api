using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace HemodinksAPI.Api.Services;

public class NotificationService : INotificationService
{
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(ILogger<NotificationService> logger)
    {
        _logger = logger;
    }

    public Task SendNotificationToUserAsync(int userId, string title, string message)
    {
        _logger.LogInformation("[Notification] ToUser {UserId} - {Title}: {Message}", userId, title, message);
        return Task.CompletedTask;
    }

    public Task SendNotificationToMedicalProfileAsync(int medicoPerfilId, string title, string message)
    {
        _logger.LogInformation("[Notification] ToMedicalProfile {PerfilId} - {Title}: {Message}", medicoPerfilId, title, message);
        return Task.CompletedTask;
    }
}
