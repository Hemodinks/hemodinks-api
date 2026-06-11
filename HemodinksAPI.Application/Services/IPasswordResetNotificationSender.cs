namespace HemodinksAPI.Application.Services;

public interface IPasswordResetNotificationSender
{
    Task SendAsync(PasswordResetNotification notification, CancellationToken cancellationToken);
}

public sealed record PasswordResetNotification(
    string Email,
    string Nome,
    string Token,
    DateTime ExpiresAt);
