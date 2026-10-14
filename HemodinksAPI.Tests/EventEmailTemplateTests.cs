using System.Net.Mime;
using System.Text.Json;
using System.Text.Json.Serialization;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Services;

namespace HemodinksAPI.Tests;

public sealed class EventEmailTemplateTests
{
    [Fact]
    public void HtmlEscapesEventFieldsAndTextAlternativeRetainsDetails()
    {
        var ev = new Event
        {
            Title = "Reunião <img src=x onerror=alert(1)> & revisão",
            Description = "Texto <script>alert(1)</script>\nSegunda linha",
            Start = new DateTime(2026, 10, 13, 16, 0, 0, DateTimeKind.Utc),
            End = new DateTime(2026, 10, 13, 17, 0, 0, DateTimeKind.Utc)
        };
        var message = EventEmailTemplateRenderer.Create(ev, "person@example.test");
        Assert.True(message.IsValid());
        Assert.Contains(ev.Title, message.Body);
        Assert.Contains(ev.Description!, message.Body);
        Assert.Contains("UTC", message.Body);
        var html = EventEmailTemplateRenderer.RenderHtml(message.Template!);
        Assert.Contains("&lt;img", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<img src=x", html);
        Assert.Contains("<br>", html);
        Assert.Contains("HemoDinks", html);
    }

    [Fact]
    public void AllDayUsesCivilDatesAndInvalidTimeZoneUsesExplicitUtc()
    {
        var allDay = new Event
        {
            Title = "Dia inteiro", IsAllDay = true,
            AllDayStartDate = new DateOnly(2026, 10, 13),
            AllDayEndDate = new DateOnly(2026, 10, 14)
        };
        var first = EventEmailTemplateRenderer.Create(allDay, "a@example.test");
        Assert.Contains("13/10/2026", first.Body);
        Assert.Contains("14/10/2026", first.Body);
        Assert.True(first.Template!.IsAllDay);
        var regular = new Event
        {
            Title = "Fuso inválido", TimeZoneId = "invalid/Unknown",
            Start = new DateTime(2026, 10, 13, 16, 0, 0, DateTimeKind.Utc),
            End = new DateTime(2026, 10, 13, 17, 0, 0, DateTimeKind.Utc)
        };
        var second = EventEmailTemplateRenderer.Create(regular, "a@example.test");
        Assert.Equal("UTC", second.Template!.TimeZoneLabel);
        Assert.Contains("13/10/2026 16:00", second.Body);
    }

    [Fact]
    public void SmtpUsesMultipartAlternativeAndLegacyPayloadRemainsValid()
    {
        var details = new EventEmailTemplateData("Título", "Descrição", "13/10/2026 13:00",
            "13/10/2026 14:00", "America/Sao_Paulo", false);
        var message = new EventEmailMessage("a@example.test", "Conteúdo em texto", details);
        using var mime = SmtpEventEmailSender.CreateMailMessage(message,
            new EmailOptions { FromEmail = "hemodinks@example.test" });
        Assert.False(mime.IsBodyHtml);
        Assert.Equal("Conteúdo em texto", mime.Body);
        Assert.Equal(2, mime.AlternateViews.Count);
        Assert.Equal(MediaTypeNames.Text.Plain, mime.AlternateViews[0].ContentType.MediaType);
        Assert.Equal(MediaTypeNames.Text.Html, mime.AlternateViews[1].ContentType.MediaType);
        var oldJson = """{"email":"a@example.test","body":"Mensagem antiga"}""";
        var parsed = JsonSerializer.Deserialize<EventEmailMessage>(oldJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow });
        Assert.NotNull(parsed);
        Assert.True(parsed.IsValid());
        Assert.Null(parsed.Template);
        using var legacyMime = SmtpEventEmailSender.CreateMailMessage(parsed,
            new EmailOptions { FromEmail = "hemodinks@example.test" });
        Assert.Empty(legacyMime.AlternateViews);
        Assert.False(legacyMime.IsBodyHtml);
    }

    [Fact]
    public void LargeEscapedPayloadFallsBackToPlainTextWithinFunctionLimit()
    {
        var ev = new Event
        {
            Title = "Evento longo", Description = new string('<', 2000),
            Start = new DateTime(2026, 10, 13, 16, 0, 0, DateTimeKind.Utc),
            End = new DateTime(2026, 10, 13, 17, 0, 0, DateTimeKind.Utc)
        };
        var message = EventEmailTemplateRenderer.Create(ev, "a@example.test");
        Assert.True(message.IsValid());
        Assert.Null(message.Template);
        Assert.Contains(ev.Description!, message.Body);
    }

    [Fact]
    public void InvalidStructuredPayloadIsRejectedBeforeTransport()
    {
        var tooLong = new EventEmailTemplateData(new string('x', 251), null, "Início", "Fim", "UTC", false);
        Assert.False(new EventEmailMessage("a@example.test", "plain", tooLong).IsValid());
        var poisoned = new EventEmailTemplateData("Title", "Line\0NUL", "Início", "Fim", "UTC", false);
        Assert.False(new EventEmailMessage("a@example.test", "plain", poisoned).IsValid());
    }
}
