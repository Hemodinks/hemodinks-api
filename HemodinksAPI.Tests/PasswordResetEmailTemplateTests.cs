using HemodinksAPI.Application.Services;
using HemodinksAPI.Infrastructure.Services;

namespace HemodinksAPI.Tests;

public sealed class PasswordResetEmailTemplateTests
{
    [Fact]
    public void BuildResetLink_UsesConfiguredHomologationUrlAndEncodesToken()
    {
        var link = PasswordResetEmailTemplate.BuildResetLink(
            "https://hemodinks-homologacao.gestao-saude.tec.br/reset-password",
            "A B+C");

        Assert.Equal(
            "https://hemodinks-homologacao.gestao-saude.tec.br/reset-password?token=A%20B%2BC",
            link);
    }

    [Fact]
    public void CreateHtmlBody_UsesCurrentBrandedLayout()
    {
        var notification = new PasswordResetNotification(
            "george@example.com",
            "George Marcone",
            "token",
            new DateTime(2026, 7, 17, 21, 10, 0, DateTimeKind.Utc));
        var resetLink = "https://hemodinks-homologacao.gestao-saude.tec.br/reset-password?token=token";

        var body = PasswordResetEmailTemplate.CreateHtmlBody(notification, resetLink, brandLogoUrl: null);

        Assert.Contains("<!doctype html>", body);
        Assert.Contains("Redefina sua senha com seguranca", body);
        Assert.Contains("Resetar senha", body);
        Assert.Contains(resetLink, body);
        Assert.Contains("border-radius:16px", body);
        Assert.DoesNotContain("Clique aqui para criar uma nova senha", body);
        Assert.DoesNotContain("hemodinks-homologacao.vercel.app", body);
    }
}
