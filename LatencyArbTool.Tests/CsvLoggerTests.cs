using LatencyArbTool.Core.Models;
using LatencyArbTool.Core.Services;

namespace LatencyArbTool.Tests;

public sealed class CsvLoggerTests
{
    [Fact]
    public void LogTick_WritesResolvedLatencyColumns()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"latency-arb-tests-{Guid.NewGuid():N}");
        var logsDirectory = Path.Combine(directory, "logs");
        Directory.CreateDirectory(directory);

        try
        {
            using (var logger = new CsvLogger(logsDirectory))
            {
                var snapshot = Snapshot();
                var thresholds = new GapThresholds(-50, 30, -15, 20, 0, 0, 0, 0, 1, 1, true);
                logger.LogTick(snapshot, thresholds);
                logger.Flush();
            }

            var ticks = Directory.GetFiles(Path.Combine(directory, "logs"), "ticks_*.csv").Single();
            var lines = File.ReadAllLines(ticks);

            Assert.Contains("latencyA,latencyASource,latencyB,latencyBSource", lines[0]);
            Assert.Contains("100,EaTickCount,150,EaTickCount", lines[1]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LogDecision_WritesReason()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"latency-arb-tests-{Guid.NewGuid():N}");
        var logsDirectory = Path.Combine(directory, "logs");
        Directory.CreateDirectory(directory);

        try
        {
            using (var logger = new CsvLogger(logsDirectory))
            {
                var snapshot = Snapshot();
                var thresholds = new GapThresholds(-50, 30, -15, 20, 0, 0, 0, 0, 1, 1, true);
                logger.LogDecision(new DryRunEvent("guard block", "feed B stale", BotState.Idle, 1), snapshot, thresholds);
                logger.Flush();
            }

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
        var a = new TickRecord(1, 900, 100, 101, 1, 1, "XAUUSD");
        var b = new TickRecord(1, 850, 100, 101, 1, 1, "XAUUSD");
        return new MarketSnapshot(a, b, 1, -50, 30, 1_000);
    }
}
