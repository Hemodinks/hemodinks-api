using System.Diagnostics;

namespace HemodinksAPI.Api;

internal sealed class StartupDiagnostics
{
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private int _ready;

    public void Record(ILogger logger, string stage, double durationMs) =>
        logger.LogInformation(
            "Startup stage {StartupStage}: {DurationMs} ms; elapsed {StartupElapsedMs} ms",
            stage, durationMs, _elapsed.Elapsed.TotalMilliseconds);

    public void RecordFirstReady(ILogger logger)
    {
        if (Interlocked.Exchange(ref _ready, 1) == 0)
        {
            Record(logger, "first_database_ready", _elapsed.Elapsed.TotalMilliseconds);
        }
    }

    public void RecordDatabaseProbe(ILogger logger, double durationMs, bool connected)
    {
        if (Volatile.Read(ref _ready) == 0)
        {
            Record(logger, connected ? "database_probe_connected" : "database_probe_failed", durationMs);
        }
    }
}
