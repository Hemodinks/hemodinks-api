using System.Diagnostics;
using System.Text.Json;
using HemodinksAPI.Api;
using HemodinksAPI.Application.Security;
using HemodinksAPI.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Tests;

public sealed class SecurityObservationTests(ITestOutputHelper output)
{
    private static SecurityObservation Event(SecurityEventKind kind, string? account = null, string operation = "login") =>
        new(DateTimeOffset.UtcNow, kind, operation, kind == SecurityEventKind.AuthenticationRefused ? "invalid_credential" : "completed", "server-id", AccountKey: account);

    [Theory]
    [InlineData("login", 401, null, SecurityEventKind.AuthenticationRefused)]
    [InlineData("login", 403, null, SecurityEventKind.AuthorizationDenied)]
    [InlineData("discovery", 429, null, SecurityEventKind.Throttled)]
    [InlineData("login", 500, null, SecurityEventKind.InfrastructureFailure)]
    [InlineData("session", 503, "session_validation_busy", SecurityEventKind.TemporaryConflict)]
    [InlineData("session", 401, "session_absolute_expired", SecurityEventKind.SessionExpired)]
    [InlineData("session", 401, "session_idle_expired", SecurityEventKind.SessionExpired)]
    [InlineData("session", 401, null, SecurityEventKind.SessionRejected)]
    [InlineData("session", 409, null, SecurityEventKind.TemporaryConflict)]
    [InlineData("recovery", 200, null, SecurityEventKind.RecoveryRequested)]
    [InlineData("recovery_confirm", 200, null, SecurityEventKind.RecoveryCompleted)]
    public void OutcomesAreClassifiedWithoutMessageComparison(string operation, int status, string? code, SecurityEventKind expected) =>
        Assert.Equal(expected, SecurityObservationMiddleware.Classify(operation, status, code));

    [Fact]
    public void ChallengeIsNotSuccessfulAuthentication()
    {
        Assert.Null(SecurityObservationMiddleware.Classify("login", 200, null));
        Assert.Equal(SecurityEventKind.CredentialValidated, SecurityObservationMiddleware.Classify("login", 200, null, credentialOnly: true));
        Assert.Equal(SecurityEventKind.AuthenticationSucceeded, SecurityObservationMiddleware.Classify("login", 200, null, issued: true));
        Assert.Equal(SecurityEventKind.SessionRevoked, SecurityObservationMiddleware.Classify("session", 204, null, revoked: true));
    }

    [Fact]
    public void AccountFailuresAlertOnceAndExpire()
    {
        var detector = new SecurityPatternDetector(new());
        var value = Event(SecurityEventKind.AuthenticationRefused, new string('A', 64));
        for (var i = 0; i < 9; i++) Assert.Empty(detector.Observe(value));
        var alert = Assert.Single(detector.Observe(value));
        Assert.Equal("account_failures", alert.Reason);
        Assert.Null(alert.ClinicId);
        Assert.Empty(detector.Observe(value));
        Assert.Empty(detector.Observe(value with { Timestamp = value.Timestamp.AddMinutes(11) }));
    }

    [Fact]
    public void DetectorBoundsCooldownStateUnderManyAttackedAccounts()
    {
        var detector = new SecurityPatternDetector(new() { MaximumWindowEvents = 100,
            AccountFailureThreshold = 2, MultipleAccountFailuresThreshold = 10000 });
        var sample = Event(SecurityEventKind.AuthenticationRefused);
        var alerts = new List<SecurityObservation>();
        for (var i = 0; i < 250; i++)
        {
            var value = sample with { AccountKey = i.ToString() };
            alerts.AddRange(detector.Observe(value)); alerts.AddRange(detector.Observe(value));
        }
        Assert.Equal(100, alerts.Count); // Further correlation keys cannot grow state without bound.
        var next = sample with { AccountKey = "new", Timestamp = sample.Timestamp.AddMinutes(11) };
        Assert.Empty(detector.Observe(next));
        Assert.Single(detector.Observe(next));
    }

    [Fact]
    public void MultipleAccountsNeedsVolumeDiversityAndHighFailureRatio()
    {
        var options = new SecurityObservationOptions { AccountFailureThreshold = 100 };
        var detector = new SecurityPatternDetector(options);
        var alerts = new List<SecurityObservation>();
        for (var i = 0; i < 50; i++) alerts.AddRange(detector.Observe(Event(SecurityEventKind.AuthenticationRefused, i.ToString())));
        Assert.Equal("multiple_accounts", Assert.Single(alerts).Reason);
        Assert.Null(alerts[0].ClinicId);
        var legitimateNetwork = new SecurityPatternDetector(options);
        for (var i = 0; i < 100; i++) Assert.Empty(legitimateNetwork.Observe(Event(SecurityEventKind.AuthenticationSucceeded)));
        for (var i = 0; i < 50; i++) Assert.Empty(legitimateNetwork.Observe(Event(SecurityEventKind.AuthenticationRefused, i.ToString())));
        var oneAccount = new SecurityPatternDetector(options);
        for (var i = 0; i < 50; i++) Assert.Empty(oneAccount.Observe(Event(SecurityEventKind.AuthenticationRefused, "one")));
    }

    [Fact]
    public void SessionErrorsNeedMinimumSamplesAndRatio()
    {
        var detector = new SecurityPatternDetector(new());
        for (var i = 0; i < 80; i++) Assert.Empty(detector.Observe(Event(SecurityEventKind.SessionValidated, operation: "session")));
        for (var i = 0; i < 19; i++) Assert.Empty(detector.Observe(Event(SecurityEventKind.TemporaryConflict, operation: "session")));
        Assert.Equal("session_error_growth", Assert.Single(detector.Observe(Event(SecurityEventKind.SessionExpired, operation: "session"))).Reason);
        var normal = new SecurityPatternDetector(new());
        for (var i = 0; i < 200; i++) Assert.Empty(normal.Observe(Event(SecurityEventKind.SessionRejected, operation: "bootstrap")));
        for (var i = 0; i < 100; i++) Assert.Empty(normal.Observe(Event(SecurityEventKind.SessionValidated, operation: "session")));
        for (var i = 0; i < 20; i++) Assert.Empty(normal.Observe(Event(SecurityEventKind.SessionRejected, operation: "session")));
    }

    [Fact]
    public async Task PersistenceFailureDoesNotStopConsumerAndPayloadIsWhitelisted()
    {
        var logger = new CaptureLogger { ThrowFirst = true };
        using var worker = new SecurityObservationWorker(Options.Create(new SecurityObservationOptions()), logger, TimeProvider.System);
        await worker.StartAsync(default);
        worker.TryWrite(Event(SecurityEventKind.AuthenticationRefused));
        worker.TryWrite(Event(SecurityEventKind.AuthenticationRefused) with
        { Operation = "password-secret\r\n", Reason = "bearer-secret", RequestId = "server\r\n\t-id", AccountKey = "email@example.com" });
        await logger.Written.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await worker.StopAsync(default);
        var serialized = JsonSerializer.Serialize(logger.Properties);
        Assert.DoesNotContain("secret", serialized);
        Assert.DoesNotContain("email@", serialized);
        Assert.Equal("server-id", logger.Properties["RequestId"]);
        Assert.Equal("other", logger.Properties["Operation"]);
        Assert.Null(logger.Properties["AccountKey"]);
    }

    [Fact]
    public void QueueSaturationNeverWaitsAndPseudonymsAreKeyedAndBounded()
    {
        using var first = Worker(); using var second = Worker();
        var key = first.PseudonymizeAccount(" Person@Example.com ");
        Assert.Equal(64, key!.Length);
        Assert.Equal(key, first.PseudonymizeAccount("person@example.com"));
        Assert.NotEqual(key, second.PseudonymizeAccount("person@example.com"));
        Assert.Null(first.PseudonymizeAccount(new string('a', 255)));
        Assert.Equal(4096, Enumerable.Range(0, 5000).Count(_ => first.TryWrite(Event(SecurityEventKind.AuthenticationRefused))));
    }

    [Fact]
    public async Task BrokenObservationCannotChangeRejectedOrSuccessfulRequest()
    {
        foreach (var status in new[] { 200, 401, 403, 503 })
        {
            var context = new DefaultHttpContext();
            context.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
                new EndpointMetadataCollection(new Microsoft.AspNetCore.Routing.EndpointNameMetadata("AuthenticateUser")), "login"));
            var middleware = new SecurityObservationMiddleware(c => { c.Response.StatusCode = status; return Task.CompletedTask; });
            await middleware.InvokeAsync(context, new BrokenWriter(), TimeProvider.System);
            Assert.Equal(status, context.Response.StatusCode);
            Assert.False(context.Response.Headers.ContainsKey("Set-Cookie"));
        }
    }

    [Fact]
    public void EmissionCostIsSmallIncludingPseudonymAndSaturatedQueue()
    {
        using var worker = Worker();
        const int count = 20000;
        var value = Event(SecurityEventKind.AuthenticationRefused);
        for (var i = 0; i < 1000; i++) worker.TryWrite(value with { AccountKey = worker.PseudonymizeAccount("person@example.com") });
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < count; i++) worker.TryWrite(value with { AccountKey = worker.PseudonymizeAccount("person@example.com") });
        var microseconds = watch.Elapsed.TotalMicroseconds / count;
        var hasher = new HemodinksAPI.Infrastructure.Utils.PasswordHasher();
        var hash = hasher.HashPassword(TestPasswords.Valid);
        watch.Restart();
        Assert.True(hasher.VerifyPassword(TestPasswords.Valid, hash));
        var hashMilliseconds = watch.Elapsed.TotalMilliseconds;
        output.WriteLine($"Storage hash verification reference: {hashMilliseconds:F2} ms; emission/reference ratio: {microseconds / 1000 / hashMilliseconds:P3}.");
        output.WriteLine($"Emission + keyed pseudonym + full queue: {microseconds:F2} us/op, {count} samples. No database/network/file access.");
        Assert.True(microseconds < 1000, $"Unexpected emission cost: {microseconds} us/op");
    }

    private static SecurityObservationWorker Worker() => new(Options.Create(new SecurityObservationOptions()),
        NullLogger<SecurityObservationWorker>.Instance, TimeProvider.System);
    private sealed class BrokenWriter : ISecurityObservationWriter
    {
        public string? PseudonymizeAccount(string? email) => throw new IOException();
        public bool TryWrite(SecurityObservation value) => throw new IOException();
    }
    private sealed class CaptureLogger : ILogger<SecurityObservationWorker>
    {
        public bool ThrowFirst;
        public Dictionary<string, object?> Properties = new();
        public TaskCompletionSource Written = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (ThrowFirst) { ThrowFirst = false; throw new IOException("Must not escape"); }
            Properties = ((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary(x => x.Key, x => x.Value);
            Written.TrySetResult();
        }
    }
}
