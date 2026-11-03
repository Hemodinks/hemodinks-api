using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using HemodinksAPI.Api;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HemodinksAPI.Tests;

public sealed class ColdStartInstrumentationTests
{
    [Fact]
    public void FastOpen_RecordsMetricWithoutLogging()
    {
        var logger = new CapturingLogger();
        using var metrics = new OpenDurationListener();
        new DatabaseConnectionTimingInterceptor(logger).RecordOpened(TimeSpan.FromMilliseconds(20), "AppDbContext");

        Assert.Empty(logger.Messages);
        Assert.Contains(metrics.Values, value => value.Outcome == "opened" && value.Ms == 20);
    }

    [Fact]
    public void SlowOpen_LogsOnlyDurationAndContext()
    {
        var logger = new CapturingLogger();
        new DatabaseConnectionTimingInterceptor(logger).RecordOpened(TimeSpan.FromSeconds(12.4), "PlatformDbContext");

        var message = Assert.Single(logger.Messages);
        Assert.Equal("DatabaseConnectionOpenSlow: 12400 ms in PlatformDbContext", message);
    }

    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task RealConnectionFailure_IsMeasuredWithoutLeakingServerOrCredentials()
    {
        SqlServerTestConnection.RequireConfigured();
        const string server = "tcp:127.0.0.1,1"; // Nothing listens: fails fast without any external call.
        const string secret = "Not-A-Real-Password-201";
        var connection = $"Server={server};Database=HemodinksColdStart;User Id=probe;Password={secret};Connect Timeout=2;Encrypt=False";
        var logger = new CapturingLogger();
        using var metrics = new OpenDurationListener();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection)
            .AddInterceptors(new DatabaseConnectionTimingInterceptor(logger)).Options;
        await using var db = new AppDbContext(options, ClinicaContextFactory.CreateDefaultResolved());

        await Assert.ThrowsAnyAsync<SqlException>(() => db.Database.OpenConnectionAsync());

        var message = Assert.Single(logger.Messages);
        Assert.StartsWith("DatabaseConnectionOpenFailed:", message);
        Assert.Contains("SqlException", message);
        Assert.DoesNotContain("127.0.0.1", message);
        Assert.DoesNotContain(secret, message);
        Assert.DoesNotContain("HemodinksColdStart", message);
        Assert.Contains(metrics.Values, value => value.Outcome == "failed");
    }

    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task RealConnectionOpen_IsMeasured()
    {
        SqlServerTestConnection.RequireConfigured();
        var logger = new CapturingLogger();
        using var metrics = new OpenDurationListener();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(SqlServerTestConnection.Create("master"))
            .AddInterceptors(new DatabaseConnectionTimingInterceptor(logger)).Options;
        await using var db = new AppDbContext(options, ClinicaContextFactory.CreateDefaultResolved());

        await db.Database.OpenConnectionAsync();
        await db.Database.CloseConnectionAsync();

        Assert.Contains(metrics.Values, value => value.Outcome == "opened");
    }

    [Theory]
    [InlineData("/", true)]
    [InlineData("/healthz", true)]
    [InlineData("/readyz", true)]
    [InlineData("/livez", true)]
    [InlineData("/api/warmup", false)]
    [InlineData("/api/users/login-context", false)]
    public void ProbeRequests_AreExcludedFromFirstApplicationRequest(string path, bool probe) =>
        Assert.Equal(probe, Program.IsProbeRequest(new PathString(path)));

    [Fact]
    public void FirstApplicationRequest_IsMarkedOnceAndUptimeAdvances()
    {
        var diagnostics = new StartupDiagnostics();
        var before = diagnostics.ElapsedMs;

        Assert.True(diagnostics.TryMarkFirstApplicationRequest());
        Assert.False(diagnostics.TryMarkFirstApplicationRequest());
        Assert.True(diagnostics.ElapsedMs >= before);
    }

    private sealed class CapturingLogger : ILogger<DatabaseConnectionTimingInterceptor>
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Enqueue(formatter(state, exception));
    }

    private sealed class OpenDurationListener : IDisposable
    {
        private readonly MeterListener _listener = new();
        public ConcurrentBag<(string Outcome, double Ms)> Values { get; } = [];

        public OpenDurationListener()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == DatabaseConnectionTimingInterceptor.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
            {
                foreach (var tag in tags)
                    if (tag.Key == "outcome") Values.Add(((string)tag.Value!, value));
            });
            _listener.Start();
        }

        public void Dispose() => _listener.Dispose();
    }
}
