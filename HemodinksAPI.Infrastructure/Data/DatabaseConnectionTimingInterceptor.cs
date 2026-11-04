using System.Data.Common;
using System.Diagnostics.Metrics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HemodinksAPI.Infrastructure.Data;

// Separates Azure SQL serverless resume time from application time on real requests (#201).
// Observes connection opens EF Core already performs: no extra queries, no database wake-ups.
// Logs carry only durations, the context type and SQL error numbers: never connection strings,
// server/database names, SQL text or exception messages.
public sealed class DatabaseConnectionTimingInterceptor(ILogger<DatabaseConnectionTimingInterceptor> logger)
    : DbConnectionInterceptor
{
    public const string MeterName = "Hemodinks.Database";
    internal static readonly TimeSpan SlowOpenThreshold = TimeSpan.FromSeconds(1);

    private static readonly Meter Meter = new(MeterName);
    private static readonly Histogram<double> OpenDuration = Meter.CreateHistogram<double>(
        "hemodinks.db.connection.open.duration", "ms", "Time to open a database connection, including pooled reuse.");

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        RecordOpened(eventData.Duration, eventData.Context?.GetType().Name);

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        RecordOpened(eventData.Duration, eventData.Context?.GetType().Name);
        return Task.CompletedTask;
    }

    public override void ConnectionFailed(DbConnection connection, ConnectionErrorEventData eventData) =>
        RecordFailed(eventData.Duration, eventData.Exception, eventData.Context?.GetType().Name);

    public override Task ConnectionFailedAsync(DbConnection connection, ConnectionErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        RecordFailed(eventData.Duration, eventData.Exception, eventData.Context?.GetType().Name);
        return Task.CompletedTask;
    }

    internal void RecordOpened(TimeSpan duration, string? context)
    {
        OpenDuration.Record(duration.TotalMilliseconds, new KeyValuePair<string, object?>("outcome", "opened"));
        if (duration >= SlowOpenThreshold)
            logger.LogWarning(
                "DatabaseConnectionOpenSlow: {DatabaseConnectionOpenMs} ms in {DbContextType}",
                Math.Round(duration.TotalMilliseconds), context ?? "unknown");
    }

    internal void RecordFailed(TimeSpan duration, Exception exception, string? context)
    {
        OpenDuration.Record(duration.TotalMilliseconds, new KeyValuePair<string, object?>("outcome", "failed"));
        // Number 40613 = database unavailable (typical while a serverless database resumes).
        var number = exception is SqlException sql ? sql.Number : (int?)null;
        logger.LogWarning(
            "DatabaseConnectionOpenFailed: {DatabaseConnectionOpenMs} ms in {DbContextType}; {ErrorType} {SqlErrorNumber}",
            Math.Round(duration.TotalMilliseconds), context ?? "unknown", exception.GetType().Name, number);
    }
}
