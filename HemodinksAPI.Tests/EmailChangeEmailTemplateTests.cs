using System.Text;
using HemodinksAPI.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Tests;

public sealed class EmailChangeEmailTemplateTests
{
    private const string Code = "482913";

    [Fact]
    public void CreateHtmlBody_UsesBrandedLayoutWithCodeAndValidity()
    {
        var body = EmailChangeEmailTemplate.CreateHtmlBody(Code, 10, brandLogoUrl: null);

        Assert.Contains("<!doctype html>", body);
        Assert.Contains("Confirme seu novo email", body);
        Assert.Contains("Código de confirmação", body);
        Assert.Contains(Code, body);
        Assert.Contains("<strong style=\"color:#172033;\">10 minutos</strong>", body);
        Assert.Contains("Se você não solicitou esta alteração", body);
        Assert.Contains("border-radius:16px", body);
    }

    [Fact]
    public void CreateHtmlBody_EncodesCodeAndIgnoresUnsafeLogoUrl()
    {
        var body = EmailChangeEmailTemplate.CreateHtmlBody("<script>", 1, brandLogoUrl: "javascript:alert(1)");

        Assert.DoesNotContain("<script>", body);
        Assert.Contains("&lt;script&gt;", body);
        Assert.DoesNotContain("javascript:", body);
        Assert.Contains("1 minuto", body);
    }

    [Fact]
    public void CreatePlainTextBody_KeepsCodeAndInstructions()
    {
        var body = EmailChangeEmailTemplate.CreatePlainTextBody(Code, 10);

        Assert.Contains(Code, body);
        Assert.Contains("válido por 10 minutos", body);
        Assert.Contains("ignore esta mensagem", body);
        Assert.DoesNotContain("<", body);
    }

    [Fact]
    public void CreateEmailChangeMessage_IsMultipartUtf8WithHtmlAlternative()
    {
        var sender = new SmtpPasswordResetNotificationSender(
            Options.Create(new EmailOptions { FromEmail = " hemodinks@example.test ", FromName = " " }),
            Options.Create(new FrontendOptions()),
            NullLogger<SmtpPasswordResetNotificationSender>.Instance);

        using var message = sender.CreateEmailChangeMessage("novo@example.test", Code, 10);

        Assert.Equal(EmailChangeEmailTemplate.Subject, message.Subject);
        Assert.Equal("hemodinks@example.test", message.From!.Address);
        Assert.Equal("Hemodinks", message.From.DisplayName);
        Assert.Equal("novo@example.test", Assert.Single(message.To).Address);
        Assert.False(message.IsBodyHtml);
        Assert.Equal(Encoding.UTF8, message.BodyEncoding);
        Assert.Equal(Encoding.UTF8, message.SubjectEncoding);
        Assert.Contains(Code, message.Body);
        var html = Assert.Single(message.AlternateViews);
        Assert.Equal("text/html", html.ContentType.MediaType);
        using var reader = new StreamReader(html.ContentStream, Encoding.UTF8);
        Assert.Contains(Code, reader.ReadToEnd());
    }
}
