using HemodinksAPI.Api;
using Microsoft.Extensions.Logging;

namespace HemodinksAPI.Tests;

// #277: so o fim do startup sobe para Warning (encaminhado ao New Relic, que descarta Information).
public sealed class StartupMilestoneLogTests
{
    [Fact]
    public void Http_started_emits_a_single_warning_milestone_other_stages_stay_information()
    {
        var logger = new CpfReconciliationSchedulingTests.CapturingLogger<StartupMilestoneLogTests>();
        var diagnostics = new StartupDiagnostics();

        diagnostics.Record(logger, "configuration_and_services", 12);
        diagnostics.Record(logger, "database_initialization", 34);
        diagnostics.Record(logger, "http_started", 56);

        var milestone = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Equal(StartupDiagnostics.StartupCompletedEvent.Id, milestone.EventId);
        Assert.StartsWith("StartupCompleted", milestone.Message);
        Assert.Equal(3, logger.Entries.Count(entry => entry.Level == LogLevel.Information));
    }
}
