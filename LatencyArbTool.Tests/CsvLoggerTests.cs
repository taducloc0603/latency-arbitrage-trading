using LatencyArbTool.Core.Models;
using LatencyArbTool.Core.Services;

namespace LatencyArbTool.Tests;

public sealed class CsvLoggerTests
{
    [Fact]
    public void LogEvent_WritesReason()
    {
        InTempDir(logsDir =>
        {
            using (var logger = new CsvLogger(logsDir))
            {
                logger.LogEvent(new DryRunEvent(
                    "live close", "stop loss", BotState.Idle, 1, ClusterId: 7,
                    Side: DryRunSide.BuyB, ClosePrice: 99.5, PnlRaw: -50),
                    gapAtOpen: 120, entryPoint: 200010);
            } // dispose closes the writer before we read the file (Windows file lock)

            var events = Directory.GetFiles(logsDir, "events_*.csv").Single();
            var lines = File.ReadAllLines(events);
            Assert.Equal("timestamp,clusterId,decision,reason,side,entryPoint,openPrice,closePrice,pnlPoints,gapAtOpen,holdMs,trailingActive", lines[0]);
            Assert.Contains("stop loss", lines[1]);
            Assert.Contains("BuyB", lines[1]);
            Assert.Contains("200010", lines[1]);
            Assert.Contains("120", lines[1]);
        });
    }

    private static void InTempDir(Action<string> body)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"latency-arb-tests-{Guid.NewGuid():N}");
        var logsDirectory = Path.Combine(directory, "logs");
        Directory.CreateDirectory(logsDirectory);
        try
        {
            body(logsDirectory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
