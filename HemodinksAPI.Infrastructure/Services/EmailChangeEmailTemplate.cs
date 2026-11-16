using System.Text.Encodings.Web;

namespace HemodinksAPI.Infrastructure.Services;

// Email com o código de confirmação do novo endereço. Mesmo layout do reset de senha;
// a versão em texto acompanha o HTML (multipart) para clientes sem HTML e filtros de spam.
public static class EmailChangeEmailTemplate
{
    public const string Subject = "Confirme seu novo email de acesso - Hemodinks";

    public static string CreatePlainTextBody(string code, int validMinutes) =>
        $"""
        Hemodinks - Confirmação de novo email

        Recebemos uma solicitação para usar este endereço como email de acesso ao Hemodinks.

        Código de confirmação:
        {code}

        Digite o código na tela de alteração de email. Ele é válido por {FormatMinutes(validMinutes)} e pode ser usado uma única vez.

        Seu email atual só muda depois desta confirmação. Ao concluir, será necessário entrar novamente com o novo email.

        Se você não solicitou esta alteração, ignore esta mensagem: nenhuma mudança será feita.

        Esta é uma mensagem automática enviada pelo Hemodinks. Por segurança, não responda este email.
        """;

    public static string CreateHtmlBody(string code, int validMinutes, string? brandLogoUrl)
    {
        var encodedCode = HtmlEncoder.Default.Encode(code);
        var validity = FormatMinutes(validMinutes);
        var brandMarkup = PasswordResetEmailTemplate.CreateBrandMarkup(brandLogoUrl);

        return $"""
            <!doctype html>
            <html lang="pt-BR">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>{Subject}</title>
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
                          <p style="margin:0 0 8px;font-size:12px;font-weight:700;letter-spacing:1.5px;text-transform:uppercase;color:#0f766e;">Segurança de acesso</p>
                          <h1 style="margin:0 0 16px;font-size:24px;line-height:1.25;color:#071928;">Confirme seu novo email</h1>
                          <p style="margin:0 0 22px;font-size:16px;line-height:1.6;color:#354052;">
                            Recebemos uma solicitação para usar este endereço como email de acesso ao Hemodinks.
                            Para concluir, digite o código abaixo na tela de alteração de email.
                          </p>
                          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin:0 0 22px;">
                            <tr>
                              <td align="center" style="padding:20px;background:#f0f7f6;border:1px solid #b9dcd8;border-radius:12px;text-align:center;">
                                <p style="margin:0 0 8px;font-size:12px;font-weight:700;letter-spacing:1px;text-transform:uppercase;color:#4a5568;">Código de confirmação</p>
                                <p style="margin:0;font-family:'Courier New',Courier,monospace;font-size:34px;font-weight:700;line-height:1.2;letter-spacing:10px;color:#071928;">{encodedCode}</p>
                              </td>
                            </tr>
                          </table>
                          <p style="margin:0 0 18px;font-size:14px;line-height:1.6;color:#5c6b7a;">
                            O código é válido por <strong style="color:#172033;">{validity}</strong> e pode ser usado uma única vez.
                            Seu email atual só muda depois desta confirmação; ao concluir, será necessário entrar novamente com o novo email.
                          </p>
                          <p style="margin:0;padding:16px 18px;background:#f8fafc;border-left:4px solid #0f766e;border-radius:8px;font-size:14px;line-height:1.6;color:#4a5568;">
                            Se você não solicitou esta alteração, ignore esta mensagem. Nenhuma mudança será feita na sua conta.
                          </p>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:20px 32px;background:#f8fafc;border-top:1px solid #e6edf5;">
                          <p style="margin:0;font-size:12px;line-height:1.5;color:#6b7785;">
                            Esta é uma mensagem automática enviada pelo Hemodinks. Por segurança, não responda este email.
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

    private static string FormatMinutes(int minutes) => minutes == 1 ? "1 minuto" : $"{Math.Max(1, minutes)} minutos";
}
