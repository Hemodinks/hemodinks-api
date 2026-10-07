namespace HemodinksAPI.Application.Services;

public interface IEmailChangeNotificationSender
{
    Task SendConfirmationAsync(string newEmail, string code, CancellationToken cancellationToken);
}
