using System.Text.Encodings.Web;

namespace HemodinksAPI.Infrastructure.Services;

public static class PasswordResetEmailTemplate
{
    public static string BuildResetLink(string resetPasswordUrl, string token)
    {
        var baseUrl = resetPasswordUrl.Trim();
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{baseUrl}{separator}token={Uri.EscapeDataString(token)}";
    }

    public static string CreateHtmlBody(
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
}
