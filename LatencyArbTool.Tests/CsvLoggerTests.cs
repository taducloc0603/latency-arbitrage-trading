using LatencyArbTool.Core.Models;
using LatencyArbTool.Core.Services;

namespace LatencyArbTool.Tests;

public sealed class CsvLoggerTests
{
    [Fact]
    public void LogTick_WritesGapColumns()
    {
        WithLogger((logger, logsDir) =>
        {
            var a = new TickRecord(1, 900, 100, 101, 1, 1, "XAUUSD");
            var b = new TickRecord(1, 850, 99.5, 100.5, 1, 1, "XAUUSD");
            logger.LogTick(nowMs: 1234, a, b, gapBuy: 50, gapSell: -50);
            logger.Flush();

            var ticks = Directory.GetFiles(logsDir, "ticks_*.csv").Single();
            var lines = File.ReadAllLines(ticks);
            Assert.Equal("timestamp,bidA,askA,bidB,askB,gapBuy,gapSell", lines[0]);
            Assert.Contains("1234,", lines[1]);
            Assert.EndsWith(",50,-50", lines[1]);
        });
    }

    [Fact]
    public void LogEvent_WritesReason()
    {
        WithLogger((logger, logsDir) =>
        {
            logger.LogEvent(new DryRunEvent(
                "live close", "stop loss", BotState.Idle, 1, ClusterId: 7,
                Side: DryRunSide.BuyB, ClosePrice: 99.5, PnlRaw: -50));
            logger.Flush();

            var events = Directory.GetFiles(logsDir, "events_*.csv").Single();
            var lines = File.ReadAllLines(events);
            Assert.Equal("timestamp,clusterId,decision,reason,side,openPrice,closePrice,pnlPoints,holdMs,trailingActive", lines[0]);
            Assert.Contains("stop loss", lines[1]);
            Assert.Contains("BuyB", lines[1]);
        });
    }

    private static void WithLogger(Action<CsvLogger, string> body)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"latency-arb-tests-{Guid.NewGuid():N}");
        var logsDirectory = Path.Combine(directory, "logs");
        Directory.CreateDirectory(directory);
        try
        {
            using var logger = new CsvLogger(logsDirectory);
            body(logger, logsDirectory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
