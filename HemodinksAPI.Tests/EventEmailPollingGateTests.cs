using System.Net.Http.Headers;
using System.Net.Http.Json;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Infrastructure.HostedServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public sealed class EventEmailPollingGateTests
{
    [Fact]
    public void Signal_IsInactiveUntilMarkedAndExpiresAfterWindow()
    {
        var clock = new ManualTimeProvider();
        var signal = new RequestActivitySignal(clock);
        Assert.False(signal.IsActiveWithin(TimeSpan.FromMinutes(15)));

        signal.MarkActive();
        clock.Advance(TimeSpan.FromMinutes(15));
        Assert.True(signal.IsActiveWithin(TimeSpan.FromMinutes(15)));

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(signal.IsActiveWithin(TimeSpan.FromMinutes(15)));
    }

    [Fact]
    public async Task IdleReplica_SkipsPollWithoutResolvingDatabaseServices()
    {
        var services = new ThrowingServiceProvider();
        var worker = Worker(services, new RequestActivitySignal(new ManualTimeProvider()), EventEmailPollingOptions.Default);

        Assert.False(await worker.TryProcessOnceAsync(default));
        Assert.Equal(0, services.Resolutions);
    }

    [Theory]
    [InlineData(true, true)]   // Gate on, recent authenticated request.
    [InlineData(false, false)] // Gate disabled by configuration: legacy behavior polls always.
    public async Task ActiveOrUngatedReplica_Polls(bool requireActivity, bool markActive)
    {
        var services = new ThrowingServiceProvider();
        var signal = new RequestActivitySignal(new ManualTimeProvider());
        if (markActive) signal.MarkActive();
        var worker = Worker(services, signal, new(requireActivity, TimeSpan.FromMinutes(15)));

        // The poll reached the processor (scope creation): proof it would touch the database.
        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.TryProcessOnceAsync(default));
        Assert.Equal(1, services.Resolutions);
    }

    [Fact]
    public async Task OnlyAuthenticatedRequestsWithValidSession_MarkActivity()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        var signal = factory.Services.GetRequiredService<RequestActivitySignal>();
        var window = TimeSpan.FromMinutes(1);

        foreach (var path in new[] { "/", "/healthz", "/readyz", "/livez" })
            await client.GetAsync(path);
        var forged = new HttpRequestMessage(HttpMethod.Get, "/api/users/");
        forged.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "forged.token.value");
        await client.SendAsync(forged);
        var login = await client.PostAsJsonAsync("/api/users/authenticate",
            new { email = "gmarcone@gmail.com", senha = TestPasswords.Valid });
        login.EnsureSuccessStatusCode();
        Assert.False(signal.IsActiveWithin(window)); // Probes, anonymous login and forged tokens do not count.

        var token = (await login.Content.ReadFromJsonAsync<AuthenticateUserResponse>())!.Token;
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/users/");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await client.SendAsync(request)).EnsureSuccessStatusCode();
        Assert.True(signal.IsActiveWithin(window));
    }

    private static EventEmailDeliveryHostedService Worker(IServiceProvider services, RequestActivitySignal signal,
        EventEmailPollingOptions options) =>
        new(services, signal, options, NullLogger<EventEmailDeliveryHostedService>.Instance);

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class ThrowingServiceProvider : IServiceProvider
    {
        public int Resolutions { get; private set; }
        public object? GetService(Type serviceType)
        {
            Resolutions++;
            throw new InvalidOperationException($"Database services must not be resolved: {serviceType.Name}");
        }
    }
}
