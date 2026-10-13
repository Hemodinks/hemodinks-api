using System.Net;
using HemodinksAPI.Api;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Infrastructure.PasswordReset;
using HemodinksAPI.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HemodinksAPI.Tests;

public partial class StorageRegistrationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EventEmail_ReusesPasswordResetFunction_WhenDedicatedSettingsAreAbsentOrBlank(string? empty)
    {
        var settings = SharedEmailSettings();
        settings["EventEmailFunctions:BaseUrl"] = empty;
        settings["EventEmailFunctions:FunctionKey"] = empty;
        var handler = new EmailRegistrationHandler();
        using var provider = EmailRegistrationProvider(settings, handler);
        AssertPasswordResetSenderPipeline(provider, typeof(FunctionBackedPasswordResetNotificationSender), typeof(SmtpPasswordResetNotificationSender));

        await provider.GetRequiredService<IEventEmailSender>().SendAsync(new("recipient@example.test", "Event content"), default);
        await provider.GetRequiredService<IPasswordResetNotificationSender>().SendAsync(
            new("recipient@example.test", "Recipient", "fake-reset-token", DateTime.UtcNow.AddMinutes(30), 1), default);

        Assert.Equal(new[] { "https://shared-worker.example.test/api/event-email/send", "https://shared-worker.example.test/api/password-reset/send" }, handler.Urls);
        Assert.Equal(new[] { "fake-shared-key", "fake-shared-key" }, handler.Keys);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EventEmail_PartialOverrideFailsClosed_WithoutMixingPasswordResetCredentials(bool onlyUrl)
    {
        var settings = SharedEmailSettings();
        settings[onlyUrl ? "EventEmailFunctions:BaseUrl" : "EventEmailFunctions:FunctionKey"] =
            onlyUrl ? "https://different-worker.example.test" : "fake-dedicated-key";
        var handler = new EmailRegistrationHandler();
        using var provider = EmailRegistrationProvider(settings, handler);

        Assert.IsType<FunctionEventEmailSender>(provider.GetRequiredService<IEventEmailSender>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetRequiredService<IEventEmailSender>()
            .SendAsync(new("recipient@example.test", "Event content"), default));
        Assert.Empty(handler.Urls);
        // A broken event override must not break the functioning recovery transport.
        await provider.GetRequiredService<IPasswordResetNotificationSender>().SendAsync(
            new("recipient@example.test", "Recipient", "fake-reset-token", DateTime.UtcNow.AddMinutes(30), 1), default);
        Assert.Equal("https://shared-worker.example.test/api/password-reset/send", Assert.Single(handler.Urls));
        Assert.Equal("fake-shared-key", Assert.Single(handler.Keys));
    }

    [Fact]
    public async Task EventEmail_CompleteOverrideUsesOnlyDedicatedFunction()
    {
        var settings = SharedEmailSettings();
        settings["EventEmailFunctions:BaseUrl"] = " https://dedicated-worker.example.test/api/ ";
        settings["EventEmailFunctions:FunctionKey"] = " fake-dedicated-key ";
        var handler = new EmailRegistrationHandler();
        using var provider = EmailRegistrationProvider(settings, handler);
        await provider.GetRequiredService<IEventEmailSender>().SendAsync(new("recipient@example.test", "Event content"), default);
        Assert.Equal("https://dedicated-worker.example.test/api/event-email/send", Assert.Single(handler.Urls));
        Assert.Equal("fake-dedicated-key", Assert.Single(handler.Keys));
    }

    [Fact]
    public void EventEmail_WithoutFunctionsPreservesSmtpAndRecoveryConfiguration()
    {
        using var provider = EmailRegistrationProvider(new(), new EmailRegistrationHandler());
        Assert.IsType<SmtpEventEmailSender>(provider.GetRequiredService<IEventEmailSender>());
        AssertPasswordResetSenderPipeline(provider, typeof(SmtpPasswordResetNotificationSender));
    }

    private static Dictionary<string, string?> SharedEmailSettings() => new()
    {
        ["PasswordResetFunctions:BaseUrl"] = "https://shared-worker.example.test",
        ["PasswordResetFunctions:FunctionKey"] = "fake-shared-key"
    };

    private static ServiceProvider EmailRegistrationProvider(Dictionary<string, string?> settings, EmailRegistrationHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationServices(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            new TestWebHostEnvironment(Environments.Development));
        services.AddSingleton<IHttpClientFactory>(new EmailRegistrationClients(handler));
        return services.BuildServiceProvider();
    }

    private sealed class EmailRegistrationClients(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class EmailRegistrationHandler : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];
        public List<string> Keys { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.AbsoluteUri);
            Keys.Add(request.Headers.GetValues("x-functions-key").Single());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
