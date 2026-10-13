using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using HemodinksAPI.Application.Services;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Infrastructure.Services;

public sealed class SmtpEventEmailSender(IOptions<EmailOptions> options) : IEventEmailSender
{
    // Kept internal to test MIME without sending to external SMTP servers.
    internal static MailMessage CreateMailMessage(EventEmailMessage notification, EmailOptions email)
    {
        if (!notification.IsValid()) throw new InvalidOperationException("Mensagem de email invalida.");
        var message = new MailMessage
        {
            From = new MailAddress(email.FromEmail!, email.FromName ?? "HemoDinks"),
            Subject = "Notificação de evento - HemoDinks",
            SubjectEncoding = Encoding.UTF8,
            Body = notification.Body,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = false
        };
        message.To.Add(new MailAddress(notification.Email));
        if (notification.Template is { } template)
        {
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                notification.Body, Encoding.UTF8, MediaTypeNames.Text.Plain));
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                EventEmailTemplateRenderer.RenderHtml(template), Encoding.UTF8, MediaTypeNames.Text.Html));
        }
        return message;
    }

    public async Task SendAsync(EventEmailMessage notification, CancellationToken cancellationToken)
    {
        if (!notification.IsValid()) throw new InvalidOperationException("Mensagem de email invalida.");
        var email = options.Value;
        if (!(string.Equals(email.Provider, "Smtp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(email.Provider, "GmailSmtp", StringComparison.OrdinalIgnoreCase))
            || string.IsNullOrWhiteSpace(email.Smtp.Host) || string.IsNullOrWhiteSpace(email.FromEmail)
            || string.IsNullOrWhiteSpace(email.Smtp.Username) || string.IsNullOrWhiteSpace(email.Smtp.Password)
            || email.Smtp.Port is < 1 or > 65535)
            throw new InvalidOperationException("Envio de email indisponivel.");
        using var message = CreateMailMessage(notification, email);
        using var client = new SmtpClient(email.Smtp.Host, email.Smtp.Port)
        {
            EnableSsl = email.Smtp.EnableSsl, DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false, Timeout = Math.Clamp(email.Smtp.TimeoutSeconds, 1, 60) * 1000,
            Credentials = new NetworkCredential(email.Smtp.Username, email.Smtp.Password)
        };
        await client.SendMailAsync(message, cancellationToken);
    }
}

