using HemodinksAPI.Application.Services;

namespace HemodinksAPI.Tests;

internal sealed class RecordingPasswordResetNotificationSender : IPasswordResetNotificationSender
{
    public List<PasswordResetNotification> Notifications { get; } = [];

    public Task<PasswordResetNotificationDispatchStatus> SendAsync(
        PasswordResetNotification notification,
        CancellationToken cancellationToken)
    {
        Notifications.Add(notification);
        return Task.FromResult(PasswordResetNotificationDispatchStatus.Sent);
    }
}
