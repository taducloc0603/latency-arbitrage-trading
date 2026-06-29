using LatencyArbTool.Core.Models;
using LatencyArbTool.Core.Services;

namespace LatencyArbTool.Tests;

public sealed class CsvLoggerTests
{
    private const string EventsHeader =
        "timestamp,clusterId,decision,reason,side,entryPoint,openPrice,closePrice,pnlPoints,gapAtOpen,holdMs,trailingActive,windowN,windowMin,windowMax,windowFirst,windowLast,windowAvg,windowDurMs,windowGaps";

    [Fact]
    public void LogEvent_WritesReasonAndWindow()
    {
        InTempDir(logsDir =>
        {
            using (var logger = new CsvLogger(logsDir))
            {
                var window = new SignalWindow(3, 1000, 95, 150, 120, 130, 345, 30, 100, new[] { 120, 95, 130 });
                logger.LogEvent(new DryRunEvent(
                    "live open", "confirmed signal", BotState.Holding, 1, ClusterId: 7,
                    Side: DryRunSide.BuyB, OpenPrice: 4100.25),
                    gapAtOpen: 120, entryPoint: 410025, window: window);
            }

            var events = Directory.GetFiles(logsDir, "events_*.csv").Single();
            var lines = File.ReadAllLines(events);
            Assert.Equal(EventsHeader, lines[0]);
            Assert.Contains("live open", lines[1]);
            Assert.Contains("410025", lines[1]);
            Assert.Contains("120;95;130", lines[1]);
        });
    }

    [Fact]
    public void LogFill_WritesSlippageRow()
    {
        InTempDir(logsDir =>
        {
            using (var logger = new CsvLogger(logsDir))
            {
                logger.LogFill(new FillEvent(
                    IsClose: false, Ticket: 50, Side: DryRunSide.BuyB, ClusterId: 1,
                    DecideTimeMs: 1000, FillTimeMs: 1330, SlippageMs: 330,
                    DecideGap: 120, FillObservedGap: 112,
                    DecidePrice: 4100.25, FillPrice: 4100.30, SlippagePrice: 0.05));
            }

            var fills = Directory.GetFiles(logsDir, "fills_*.csv").Single();
            var lines = File.ReadAllLines(fills);
            Assert.Equal("kind,ticket,clusterId,side,decideTimeMs,fillTimeMs,latencyMs,decideGap,fillObservedGap,decidePrice,fillPrice,slippagePrice,realizedUsd,commission", lines[0]);
            Assert.StartsWith("open,50,1,BuyB,", lines[1]);
            Assert.Contains("330", lines[1]);
        });
    }

    [Fact]
    public void LogSnapshot_WritesRow()
    {
        InTempDir(logsDir =>
        {
            var a = new TickRecord(1, 900, 4050.47, 4050.62, 0.15, 1, "XAUUSD");
            var b = new TickRecord(1, 850, 4050.40, 4050.69, 0.29, 1, "XAUUSD");
            using (var logger = new CsvLogger(logsDir))
            {
                logger.LogSnapshot(1234, a, b, netGapBuy: -22, netGapSell: 22,
                    winState: "idle", winDurMs: 0, winMin: 0, winMax: 0, winN: 0);
            }

            var snap = Directory.GetFiles(logsDir, "snapshot_*.csv").Single();
            var lines = File.ReadAllLines(snap);
            Assert.Equal("timestamp,aBid,aAsk,bBid,bAsk,spreadA,spreadB,netGapBuy,netGapSell,winState,winDurMs,winMin,winMax,winN", lines[0]);
            Assert.Contains("1234,", lines[1]);
            Assert.Contains("-22,22,idle", lines[1]);
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
