using System.Net;
using System.Net.Mail;
using HemodinksAPI.Application.Services;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Infrastructure.Services;

public sealed class SmtpEventEmailSender(IOptions<EmailOptions> options) : IEventEmailSender
{
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
        using var message = new MailMessage
        {
            From = new MailAddress(email.FromEmail, email.FromName ?? "HemoDinks"),
            Subject = "Notificacao de evento - HemoDinks", Body = notification.Body, IsBodyHtml = false
        };
        message.To.Add(new MailAddress(notification.Email));
        using var client = new SmtpClient(email.Smtp.Host, email.Smtp.Port)
        {
            EnableSsl = email.Smtp.EnableSsl, DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false, Timeout = Math.Clamp(email.Smtp.TimeoutSeconds, 1, 60) * 1000,
            Credentials = new NetworkCredential(email.Smtp.Username, email.Smtp.Password)
        };
        await client.SendMailAsync(message, cancellationToken);
    }
}

