namespace HemodinksAPI.Application.Services;

public interface IPasswordResetNotificationSender
{
    Task<PasswordResetNotificationDispatchStatus> SendAsync(
        PasswordResetNotification notification,
        CancellationToken cancellationToken);
}

public enum PasswordResetNotificationDispatchStatus
{
    Sent = 1,
    Queued = 2
}

public sealed record PasswordResetNotification(
    string Email,
    string Nome,
    string Token,
    DateTime ExpiresAt);
