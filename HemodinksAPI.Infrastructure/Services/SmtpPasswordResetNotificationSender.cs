using System.Net;
using System.Net.Mail;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Infrastructure.Services;

public class SmtpPasswordResetNotificationSender : IPasswordResetNotificationSender
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

    public async Task SendAsync(PasswordResetNotification notification, CancellationToken cancellationToken)
    {
        if (!IsSmtpEnabled())
        {
            _logger.LogWarning("Envio de email de reset ignorado porque Email:Provider nao esta configurado como Smtp/GmailSmtp");
            return;
        }

        ValidateOptions();

        var resetLink = BuildResetLink(notification.Token);
        using var message = CreateMessage(notification, resetLink);
        using var client = CreateClient();

        await client.SendMailAsync(message, cancellationToken);
        _logger.LogInformation("Email de reset de senha enviado para {Email}", notification.Email);
    }

    private bool IsSmtpEnabled()
    {
        return string.Equals(_emailOptions.Provider, "GmailSmtp", StringComparison.OrdinalIgnoreCase)
            || string.Equals(_emailOptions.Provider, "Smtp", StringComparison.OrdinalIgnoreCase);
    }

    private void ValidateOptions()
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

        if (string.IsNullOrWhiteSpace(_frontendOptions.ResetPasswordUrl))
        {
            throw new InvalidOperationException("Frontend:ResetPasswordUrl deve ser configurado para envio de reset de senha.");
        }
    }

    private string BuildResetLink(string token)
    {
        var baseUrl = _frontendOptions.ResetPasswordUrl!.Trim();
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{baseUrl}{separator}token={Uri.EscapeDataString(token)}";
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
            Body = CreateHtmlBody(notification, resetLink),
            IsBodyHtml = true
        };

        message.To.Add(new MailAddress(notification.Email, notification.Nome));

        return message;
    }

    private static string CreateHtmlBody(PasswordResetNotification notification, string resetLink)
    {
        var encodedName = HtmlEncoder.Default.Encode(notification.Nome);
        var encodedLink = HtmlEncoder.Default.Encode(resetLink);
        var expiresAt = notification.ExpiresAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");

        return $"""
            <p>Ola, {encodedName}.</p>
            <p>Recebemos uma solicitacao para redefinir sua senha no Hemodinks.</p>
            <p><a href="{encodedLink}">Clique aqui para criar uma nova senha</a>.</p>
            <p>Este link expira em {expiresAt}.</p>
            <p>Se voce nao solicitou essa alteracao, ignore este email.</p>
            """;
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
