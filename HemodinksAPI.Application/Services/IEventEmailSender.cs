namespace HemodinksAPI.Application.Services;

public sealed record EventEmailMessage(string Email, string Body)
{
    public bool IsValid() => !string.IsNullOrWhiteSpace(Email) && Email.Length <= 320
        && !Email.Contains('\r') && !Email.Contains('\n')
        && System.Net.Mail.MailAddress.TryCreate(Email, out var address) && address.Address == Email
        && !string.IsNullOrWhiteSpace(Body) && Body.Length <= 5000 && !Body.Contains('\0');
}

public interface IEventEmailSender
{
    Task SendAsync(EventEmailMessage message, CancellationToken cancellationToken);
}

