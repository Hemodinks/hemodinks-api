using System.Net;
using System.Net.Mail;
using HemodinksAPI.Infrastructure.PasswordReset;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Infrastructure.Services;

public class SmtpPasswordResetNotificationSender : IPasswordResetNotificationTransport, HemodinksAPI.Application.Services.IEmailChangeNotificationSender
{
    private readonly EmailOptions _emailOptions;
    private readonly FrontendOptions _frontendOptions;
    private readonly ILogger<SmtpPasswordResetNotificationSender> _logger;

    public SmtpPasswordResetNotificationSender(
        IOptions<EmailOptions> emailOptions,
        IOptions<FrontendOptions> frontendOptions,
        ILogger<SmtpPasswordResetNotificationSender> logger)
    {
        _emailOptions = emailOptions.Value;
        _frontendOptions = frontendOptions.Value;
        _logger = logger;
    }

    public string Name => "SMTP";

    public async Task<PasswordResetNotificationDispatchStatus> SendAsync(
        PasswordResetNotification notification,
        CancellationToken cancellationToken)
    {
        if (!IsSmtpEnabled())
        {
            throw new InvalidOperationException(
                "Email:Provider deve ser configurado como Smtp ou GmailSmtp para envio de reset de senha.");
        }

        ValidateOptions();

        var resetLink = PasswordResetEmailTemplate.BuildResetLink(
            _frontendOptions.ResetPasswordUrl!,
            notification.Token);
        using var message = CreateMessage(notification, resetLink);
        using var client = CreateClient();

        await client.SendMailAsync(message, cancellationToken);
        _logger.LogInformation(
            "Email de reset de senha enviado para {MaskedEmail}",
            HemodinksAPI.Application.Security.SensitiveDataMasking.MaskEmail(notification.Email));
        return PasswordResetNotificationDispatchStatus.Sent;
    }

    public async Task SendConfirmationAsync(string newEmail, string code, CancellationToken cancellationToken)
    {
        if (!IsSmtpEnabled()) throw new InvalidOperationException("SMTP unavailable.");
        ValidateOptions(requireResetUrl: false);
        using var message = new MailMessage
        {
            From = new MailAddress(_emailOptions.FromEmail!, _emailOptions.FromName ?? "Hemodinks"),
            Subject = "Confirmação de alteração de email - Hemodinks",
            Body = "Para confirmar seu novo email no Hemodinks, copie este código na tela de alteração: " + code
                + "\nSe você não solicitou a alteração, ignore esta mensagem.",
            IsBodyHtml = false
        };
        message.To.Add(new MailAddress(newEmail));
        using var client = CreateClient();
        await client.SendMailAsync(message, cancellationToken);
    }

    private bool IsSmtpEnabled()
    {
        return string.Equals(_emailOptions.Provider, "GmailSmtp", StringComparison.OrdinalIgnoreCase)
            || string.Equals(_emailOptions.Provider, "Smtp", StringComparison.OrdinalIgnoreCase);
    }

    private void ValidateOptions(bool requireResetUrl = true)
    {
        if (string.IsNullOrWhiteSpace(_emailOptions.Smtp.Host))
        {
            throw new InvalidOperationException("Email:Smtp:Host deve ser configurado para envio SMTP.");
        }

        if (string.IsNullOrWhiteSpace(_emailOptions.Smtp.Username))
        {
            throw new InvalidOperationException("Email:Smtp:Username deve ser configurado para envio SMTP.");
        }

        if (string.IsNullOrWhiteSpace(_emailOptions.Smtp.Password))
        {
            throw new InvalidOperationException("Email:Smtp:Password deve ser configurado para envio SMTP.");
        }

        if (string.IsNullOrWhiteSpace(_emailOptions.FromEmail))
        {
            throw new InvalidOperationException("Email:FromEmail deve ser configurado para envio SMTP.");
        }

        if (requireResetUrl && string.IsNullOrWhiteSpace(_frontendOptions.ResetPasswordUrl))
        {
            throw new InvalidOperationException("Frontend:ResetPasswordUrl deve ser configurado para envio de reset de senha.");
        }
    }

    private MailMessage CreateMessage(PasswordResetNotification notification, string resetLink)
    {
        var fromName = string.IsNullOrWhiteSpace(_emailOptions.FromName)
            ? "Hemodinks"
            : _emailOptions.FromName.Trim();

        var message = new MailMessage
        {
            From = new MailAddress(_emailOptions.FromEmail!.Trim(), fromName),
            Subject = "Redefinicao de senha - Hemodinks",
            Body = PasswordResetEmailTemplate.CreateHtmlBody(notification, resetLink, _emailOptions.BrandLogoUrl),
            IsBodyHtml = true
        };

        message.To.Add(new MailAddress(notification.Email, notification.Nome));

        return message;
    }

    private SmtpClient CreateClient()
    {
        return new SmtpClient(_emailOptions.Smtp.Host, _emailOptions.Smtp.Port)
        {
            EnableSsl = _emailOptions.Smtp.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Timeout = Math.Max(1, _emailOptions.Smtp.TimeoutSeconds) * 1000,
            Credentials = new NetworkCredential(
                _emailOptions.Smtp.Username,
                _emailOptions.Smtp.Password)
        };
    }
}
