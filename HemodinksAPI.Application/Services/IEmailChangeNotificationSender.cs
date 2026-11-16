namespace HemodinksAPI.Application.Services;

public interface IEmailChangeNotificationSender
{
    Task SendConfirmationAsync(string newEmail, string code, int validMinutes, CancellationToken cancellationToken);
}
