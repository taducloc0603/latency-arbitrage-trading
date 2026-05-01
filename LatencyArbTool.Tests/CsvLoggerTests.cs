using LatencyArbTool.Core.Models;
using LatencyArbTool.Core.Services;

namespace LatencyArbTool.Tests;

public sealed class CsvLoggerTests
{
    [Fact]
    public void LogDecision_WritesReason()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"latency-arb-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            using var logger = new CsvLogger(directory);
            var snapshot = Snapshot();
            var thresholds = new GapThresholds(-50, 30, -15, 20, 0, 0, 0, 0, 1, 1, true);
            logger.LogDecision(new DryRunEvent("guard block", "feed B stale", BotState.Idle, 1), snapshot, thresholds);
            logger.Flush();

            var decisions = Directory.GetFiles(Path.Combine(directory, "logs"), "decisions_*.csv").Single();
            var lines = File.ReadAllLines(decisions);

            Assert.Contains("feed B stale", lines[1]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static MarketSnapshot Snapshot()
    {
        var a = new TickRecord(1, 1, 100, 101, 1, 1, "XAUUSD");
        var b = new TickRecord(1, 1, 100, 101, 1, 1, "XAUUSD");
        return new MarketSnapshot(a, b, 1, -50, 30);
    }
}

