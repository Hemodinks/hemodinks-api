using HemodinksAPI.Application.Services;

namespace HemodinksAPI.Infrastructure.PasswordReset;

public interface IPasswordResetNotificationTransport
{
    string Name { get; }

    Task<PasswordResetNotificationDispatchStatus> SendAsync(
        PasswordResetNotification notification,
        CancellationToken cancellationToken);
}
