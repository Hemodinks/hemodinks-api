using HemodinksAPI.Application.Async;
using HemodinksAPI.Infrastructure.PasswordReset;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Infrastructure.Queues;

public class AzureQueuePasswordResetNotificationSender : IPasswordResetNotificationTransport
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

    public string Name => "Azure Storage Queue";

    public async Task<PasswordResetNotificationDispatchStatus> SendAsync(
        PasswordResetNotification notification,
        CancellationToken cancellationToken)
    {
        var message = new PasswordResetEmailQueueMessage(
            notification.Email,
            notification.Nome,
            notification.Token,
            notification.ExpiresAt);

        await _queuePublisher.EnqueueAsync(_options.PasswordResetEmailQueueName, message, cancellationToken);
        return PasswordResetNotificationDispatchStatus.Queued;
    }
}
