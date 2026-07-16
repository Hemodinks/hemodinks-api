using System.Net;
using System.Net.Mail;
using System.Text.Encodings.Web;
using HemodinksAPI.Infrastructure.PasswordReset;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Infrastructure.Services;

public class SmtpPasswordResetNotificationSender : IPasswordResetNotificationTransport
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

        var resetLink = BuildResetLink(notification.Token);
        using var message = CreateMessage(notification, resetLink);
        using var client = CreateClient();

        await client.SendMailAsync(message, cancellationToken);
        _logger.LogInformation("Email de reset de senha enviado para {Email}", notification.Email);
        return PasswordResetNotificationDispatchStatus.Sent;
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
            Body = CreateHtmlBody(notification, resetLink, _emailOptions.BrandLogoUrl),
            IsBodyHtml = true
        };

        message.To.Add(new MailAddress(notification.Email, notification.Nome));

        return message;
    }

    private static string CreateHtmlBody(
        PasswordResetNotification notification,
        string resetLink,
        string? brandLogoUrl)
    {
        var encodedName = HtmlEncoder.Default.Encode(notification.Nome);
        var encodedLink = HtmlEncoder.Default.Encode(resetLink);
        var expiresAt = notification.ExpiresAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        var brandMarkup = CreateBrandMarkup(brandLogoUrl);

        return $"""
            <!doctype html>
            <html lang="pt-BR">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>Redefinicao de senha - Hemodinks</title>
            </head>
            <body style="margin:0;padding:0;background:#f4f7fb;font-family:Arial,Helvetica,sans-serif;color:#172033;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f4f7fb;padding:32px 16px;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;background:#ffffff;border:1px solid #dbe5f1;border-radius:16px;overflow:hidden;">
                      <tr>
                        <td style="padding:28px 32px 20px;background:#071928;">
                          {brandMarkup}
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:32px;">
                          <p style="margin:0 0 16px;font-size:16px;line-height:1.6;color:#172033;">Ola, {encodedName}.</p>
                          <h1 style="margin:0 0 16px;font-size:24px;line-height:1.25;color:#071928;">Redefina sua senha com seguranca</h1>
                          <p style="margin:0 0 22px;font-size:16px;line-height:1.6;color:#354052;">
                            Recebemos uma solicitacao para criar uma nova senha de acesso ao Hemodinks.
                            Para continuar, use o botao abaixo.
                          </p>
                          <table role="presentation" cellpadding="0" cellspacing="0" style="margin:0 0 24px;">
                            <tr>
                              <td bgcolor="#0f766e" style="border-radius:10px;">
                                <a href="{encodedLink}" style="display:inline-block;padding:14px 24px;font-size:16px;font-weight:700;line-height:1.2;color:#ffffff;text-decoration:none;border-radius:10px;">
                                  Resetar senha
                                </a>
                              </td>
                            </tr>
                          </table>
                          <p style="margin:0 0 18px;font-size:14px;line-height:1.6;color:#5c6b7a;">
                            Este link expira em <strong style="color:#172033;">{expiresAt}</strong>.
                          </p>
                          <p style="margin:0;padding:16px 18px;background:#f8fafc;border-left:4px solid #0f766e;border-radius:8px;font-size:14px;line-height:1.6;color:#4a5568;">
                            Se voce nao solicitou essa alteracao, ignore este email. Sua senha atual permanecera a mesma.
                          </p>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:20px 32px;background:#f8fafc;border-top:1px solid #e6edf5;">
                          <p style="margin:0;font-size:12px;line-height:1.5;color:#6b7785;">
                            Esta e uma mensagem automatica enviada pelo Hemodinks. Por seguranca, nao responda este email.
                          </p>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
    }

    private static string CreateBrandMarkup(string? brandLogoUrl)
    {
        var trimmedLogoUrl = brandLogoUrl?.Trim();
        if (Uri.TryCreate(trimmedLogoUrl, UriKind.Absolute, out var logoUri)
            && (logoUri.Scheme == Uri.UriSchemeHttp || logoUri.Scheme == Uri.UriSchemeHttps))
        {
            var encodedLogoUrl = HtmlEncoder.Default.Encode(logoUri.ToString());
            return $"""
                <img src="{encodedLogoUrl}" alt="Hemodinks" width="168" style="display:block;max-width:168px;height:auto;border:0;">
                """;
        }

        return """
            <table role="presentation" cellpadding="0" cellspacing="0">
              <tr>
                <td style="width:44px;height:44px;border-radius:12px;background:#0f766e;color:#ffffff;font-size:22px;font-weight:700;text-align:center;">H</td>
                <td style="padding-left:12px;color:#ffffff;font-size:24px;font-weight:700;letter-spacing:0;">Hemodinks</td>
              </tr>
            </table>
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
