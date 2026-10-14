using System.Globalization;
using System.Net;
using System.Text.Json;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Infrastructure.Services;

// SMTP presentation layer only. Does not alter tenants, recipients or delivery policy.
internal static class EventEmailTemplateRenderer
{
    internal static EventEmailMessage Create(Event ev, string recipientEmail)
    {
        string start;
        string end;
        string timeZone;
        if (ev.IsAllDay && ev.AllDayStartDate.HasValue && ev.AllDayEndDate.HasValue)
        {
            start = ev.AllDayStartDate.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            end = ev.AllDayEndDate.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            timeZone = "Dia inteiro (datas civis)";
        }
        else
        {
            var zone = ResolveZone(ev.TimeZoneId);
            // Persisted Event.Start/End are UTC, even if loaded as DateTimeKind.Unspecified.
            start = FormatInZone(ev.Start, zone);
            end = FormatInZone(ev.End, zone);
            timeZone = zone.Id;
        }

        var template = new EventEmailTemplateData(ev.Title, ev.Description, start, end, timeZone,
            ev.IsAllDay && ev.AllDayStartDate.HasValue && ev.AllDayEndDate.HasValue);
        var body = $"Você foi incluído em um evento da agenda HemoDinks.\n\n"
            + $"Título: {ev.Title}\nInício: {start}\nFim: {end}\n"
            + $"Fuso horário: {timeZone}\n\n{ev.Description}";
        var rich = new EventEmailMessage(recipientEmail, body, template);
        // The Function enforces a 24 KiB JSON limit. Preserve delivery as text
        // rather than reject long or heavily escaped Unicode/HTML-like content.
        if (rich.IsValid() && JsonSerializer.SerializeToUtf8Bytes(rich,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)).Length <= 20 * 1024)
            return rich;
        return new EventEmailMessage(recipientEmail, body);
    }

    private static string FormatInZone(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone)
            .ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("pt-BR"));

    private static TimeZoneInfo ResolveZone(string? timeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId))
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }

    internal static string RenderHtml(EventEmailTemplateData template)
    {
        if (!template.IsValid()) throw new InvalidOperationException("Dados do template de evento inválidos.");
        static string Escape(string? text) => WebUtility.HtmlEncode(text ?? "");
        var title = Escape(template.Title);
        var start = Escape(template.StartLabel);
        var end = Escape(template.EndLabel);
        var zone = Escape(template.TimeZoneLabel);
        var description = Escape(template.Description).Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n').Replace("\n", "<br>", StringComparison.Ordinal);
        var descriptionSection = string.IsNullOrWhiteSpace(template.Description) ? "" : $$"""
          <tr><td style="padding:0 32px 22px;">
            <p style="margin:0 0 8px;color:#546578;font-size:12px;font-weight:700;text-transform:uppercase;letter-spacing:.6px;">Descrição</p>
            <p style="margin:0;color:#253748;font-size:15px;line-height:1.65;overflow-wrap:anywhere;">{{description}}</p>
          </td></tr>
          """;
        var dateLabel = template.IsAllDay ? "Período do evento" : "Data e horário";
        return $$"""
          <!doctype html>
          <html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"></head>
          <body style="margin:0;padding:0;background:#f2f6f7;color:#22313f;font-family:Arial,Helvetica,sans-serif;">
            <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%" style="background:#f2f6f7;width:100%;">
              <tr><td align="center" style="padding:28px 12px;">
                <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%" style="width:100%;max-width:600px;background:#ffffff;border:1px solid #dde8e9;border-radius:12px;">
                  <tr><td style="padding:26px 32px;background:#117f78;color:#ffffff;">
                    <p style="margin:0;font-size:22px;line-height:1.3;font-weight:700;">HemoDinks</p>
                    <p style="margin:7px 0 0;font-size:13px;line-height:1.5;color:#e2f7f4;">Agenda e notificações</p>
                  </td></tr>
                  <tr><td style="padding:28px 32px 16px;">
                    <p style="margin:0 0 10px;font-size:13px;font-weight:700;color:#117f78;">NOVO EVENTO</p>
                    <h1 style="margin:0 0 12px;font-size:22px;line-height:1.35;font-weight:700;color:#173b43;overflow-wrap:anywhere;">{{title}}</h1>
                    <p style="margin:0;font-size:15px;line-height:1.6;color:#495b6b;">Você foi incluído em um evento da agenda HemoDinks.</p>
                  </td></tr>
                  <tr><td style="padding:0 32px 22px;">
                    <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%" style="width:100%;background:#f2f8f8;border-left:4px solid #117f78;">
                      <tr><td style="padding:17px 18px;">
                        <p style="margin:0 0 10px;font-size:12px;color:#506776;font-weight:700;text-transform:uppercase;">{{dateLabel}}</p>
                        <p style="margin:0 0 6px;font-size:15px;line-height:1.5;color:#203f45;"><strong>Início:</strong> {{start}}</p>
                        <p style="margin:0 0 6px;font-size:15px;line-height:1.5;color:#203f45;"><strong>Fim:</strong> {{end}}</p>
                        <p style="margin:0;font-size:12px;line-height:1.5;color:#506776;">Fuso: {{zone}}</p>
                      </td></tr>
                    </table>
                  </td></tr>
                  {{descriptionSection}}
                  <tr><td style="padding:18px 32px 26px;border-top:1px solid #edf0f2;">
                    <p style="margin:0;color:#61717c;font-size:12px;line-height:1.6;">Esta é uma comunicação individual sobre um evento da agenda HemoDinks. Não responda a este e-mail para alterar o agendamento.</p>
                  </td></tr>
                </table>
              </td></tr>
            </table>
          </body></html>
          """;
    }
}
