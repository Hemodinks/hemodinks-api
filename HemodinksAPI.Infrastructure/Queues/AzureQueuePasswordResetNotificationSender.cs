using HemodinksAPI.Application.Async;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Infrastructure.Queues;

public class AzureQueuePasswordResetNotificationSender : IPasswordResetNotificationSender
{
    private readonly IAsyncQueuePublisher _queuePublisher;
    private readonly AsyncQueueOptions _options;

    public AzureQueuePasswordResetNotificationSender(
        IAsyncQueuePublisher queuePublisher,
        IOptions<AsyncQueueOptions> options)
    {
        _queuePublisher = queuePublisher;
        _options = options.Value;
    }

    public Task SendAsync(PasswordResetNotification notification, CancellationToken cancellationToken)
    {
        var message = new PasswordResetEmailQueueMessage(
            notification.Email,
            notification.Nome,
            notification.Token,
            notification.ExpiresAt);

        return _queuePublisher.EnqueueAsync(_options.PasswordResetEmailQueueName, message, cancellationToken);
    }
}
