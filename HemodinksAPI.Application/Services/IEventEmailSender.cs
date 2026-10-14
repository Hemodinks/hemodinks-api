namespace HemodinksAPI.Application.Services;

// Structured event content only: SMTP renders HTML internally, never from raw client HTML.
public sealed record EventEmailTemplateData(string Title, string? Description,
    string StartLabel, string EndLabel, string TimeZoneLabel, bool IsAllDay)
{
    public bool IsValid() => Valid(Title, 250) && Valid(StartLabel, 120)
        && Valid(EndLabel, 120) && Valid(TimeZoneLabel, 100)
        && (Description is null || (Description.Length <= 4000 && !Description.Contains('\0')));

    private static bool Valid(string? value, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= max && !value.Contains('\0');
}

// Optional Template preserves compatibility with old text-only Function payloads.
public sealed record EventEmailMessage(string Email, string Body, EventEmailTemplateData? Template = null)
{
    public bool IsValid() => !string.IsNullOrWhiteSpace(Email) && Email.Length <= 320
        && !Email.Contains('\r') && !Email.Contains('\n')
        && System.Net.Mail.MailAddress.TryCreate(Email, out var address) && address.Address == Email
        && !string.IsNullOrWhiteSpace(Body) && Body.Length <= 5000 && !Body.Contains('\0')
        && (Template is null || Template.IsValid());
}

public interface IEventEmailSender
{
    Task SendAsync(EventEmailMessage message, CancellationToken cancellationToken);
}

