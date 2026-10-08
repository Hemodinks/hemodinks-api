using HemodinksAPI.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace HemodinksAPI.Tests;

public sealed class DatabaseReadinessDiagnosticsTests
{
    [Fact]
    public async Task Success_records_separate_stages_without_connection_details()
    {
        var logger = new ProbeLogger();
        var probe = CreateProbe(logger);
        Assert.True((await probe.CheckAsync(false, CancellationToken.None)).Connected);
        Assert.Equal("complete", logger.Values["Phase"]);
        Assert.Equal("none", logger.Values["FailureCode"]);
        Assert.True((double)logger.Values["ConnectionMs"]! >= 0);
        Assert.True((double)logger.Values["QueryMs"]! >= 0);
        Assert.Equal(0d, logger.Values["SchemaMs"]);
        Assert.DoesNotContain(":memory:", logger.Message);
        Assert.Null(logger.Exception);
    }

    [Fact]
    public async Task Failure_reports_stage_and_category_without_exception_payload()
    {
        var logger = new ProbeLogger();
        var failure = new TimeoutException("Server=private;Password=do-not-log");
        var probe = new SqlDatabaseReadinessProbe(
            new TestingSqlConnectionFactory(() => throw failure),
            () => throw new InvalidOperationException("Must not resolve EF"), logger);
        Assert.Same(failure, await Assert.ThrowsAsync<TimeoutException>(() =>
            probe.CheckAsync(false, CancellationToken.None)));
        Assert.Equal("connection", logger.Values["Phase"]);
        Assert.Equal("timeout", logger.Values["FailureCode"]);
        Assert.DoesNotContain("private", logger.Message);
        Assert.DoesNotContain("do-not-log", logger.Message);
        Assert.Null(logger.Exception);
    }

    [Fact]
    public async Task Cancelled_probe_is_not_reported_as_ready()
    {
        var logger = new ProbeLogger();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateProbe(logger).CheckAsync(false, cancellation.Token));
        Assert.Equal("cancelled", logger.Values["FailureCode"]);
        Assert.Null(logger.Exception);
    }

    [Fact]
    public async Task Logging_failure_preserves_success_and_original_database_failure()
    {
        var logger = new ProbeLogger { ThrowOnLog = true };
        Assert.True((await CreateProbe(logger).CheckAsync(false, CancellationToken.None)).Connected);
        var failure = new InvalidOperationException("secret database details");
        var probe = new SqlDatabaseReadinessProbe(new TestingSqlConnectionFactory(() => throw failure),
            () => throw new InvalidOperationException("Must not resolve EF"), logger);
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() =>
            probe.CheckAsync(false, CancellationToken.None)));
    }

    private static SqlDatabaseReadinessProbe CreateProbe(ProbeLogger logger) => new(
        new TestingSqlConnectionFactory(() => new SqliteConnection("Data Source=:memory:")),
        () => throw new InvalidOperationException("Must not resolve EF"), logger);

    private sealed class ProbeLogger : ILogger<SqlDatabaseReadinessProbe>
    {
        public Dictionary<string, object?> Values { get; private set; } = [];
        public string Message { get; private set; } = "";
        public Exception? Exception { get; private set; }
        public bool ThrowOnLog { get; init; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (ThrowOnLog) throw new InvalidOperationException("Logging unavailable");
            Values = ((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary();
            Message = formatter(state, exception);
            Exception = exception;
        }
    }
}
