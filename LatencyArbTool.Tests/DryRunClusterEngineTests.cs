using LatencyArbTool.Core.Models;
using LatencyArbTool.Core.Services;

namespace LatencyArbTool.Tests;

public sealed class DryRunClusterEngineTests
{
    private const long BaseNowMs = 1_777_397_957_338;

    [Fact]
    public void Step_OpensBuyWhenConfirmed()
    {
        var engine = new DryRunClusterEngine();

        var events = engine.Step(Snapshot(0, gapBuy: -60, gapSell: 0), Thresholds(), SignalSide.BuyB);

        Assert.Equal(BotState.Holding, engine.State);
        Assert.Equal(DryRunSide.BuyB, engine.CurrentCluster?.Side);
        Assert.Equal("dry open", Assert.Single(events).Decision);
        Assert.Equal(101, engine.CurrentCluster?.Orders.Single().OpenPrice);
    }

    [Fact]
    public void Step_OpensSellWhenConfirmed()
    {
        var engine = new DryRunClusterEngine();

        engine.Step(Snapshot(0, gapBuy: 0, gapSell: 40), Thresholds(), SignalSide.SellB);

        Assert.Equal(DryRunSide.SellB, engine.CurrentCluster?.Side);
        Assert.Equal(100, engine.CurrentCluster?.Orders.Single().OpenPrice);
    }

    [Fact]
    public void Step_DoesNotStackWhileSingleOrderModeIsEnabled()
    {
        var engine = new DryRunClusterEngine();
        engine.Step(Snapshot(0, gapBuy: -60, gapSell: 0), Thresholds(), SignalSide.BuyB);

        engine.Step(Snapshot(999, gapBuy: -60, gapSell: 0), Thresholds(), null);
        Assert.Equal(1, engine.CurrentCluster?.Orders.Count);

        engine.Step(Snapshot(1000, gapBuy: -60, gapSell: 0), Thresholds(), null);
        engine.Step(Snapshot(2000, gapBuy: -60, gapSell: 0), Thresholds(), null);
        engine.Step(Snapshot(3000, gapBuy: -60, gapSell: 0), Thresholds(), null);

        Assert.Equal(1, engine.CurrentCluster?.Orders.Count);
    }

    [Fact]
    public void Step_ClosesBuyOnAReversalAfterMinHold()
    {
        var engine = new DryRunClusterEngine();
        engine.Step(Snapshot(0, gapBuy: -80, gapSell: 0, askA: 101.0), Thresholds(), SignalSide.BuyB);

        engine.Step(Snapshot(StrategyDefaults.MinHoldMs, gapBuy: -80, gapSell: 0, askA: 100.69), Thresholds(), null);

        Assert.Equal(BotState.Idle, engine.State);
        Assert.Null(engine.CurrentCluster);
    }

    [Fact]
    public void Step_ClosesBuyOnGapRevertAfterMinHold()
    {
        var engine = new DryRunClusterEngine();
        engine.Step(Snapshot(0, gapBuy: -80, gapSell: 0), Thresholds(), SignalSide.BuyB);

        engine.Step(Snapshot(StrategyDefaults.MinHoldMs, gapBuy: -15, gapSell: 0), Thresholds(), null);

        Assert.Null(engine.CurrentCluster);
    }

    [Fact]
    public void Step_ClosesSellOnAReversalAfterMinHold()
    {
        var engine = new DryRunClusterEngine();
        engine.Step(Snapshot(0, gapBuy: 0, gapSell: 80, bidA: 100.0), Thresholds(), SignalSide.SellB);

        engine.Step(Snapshot(StrategyDefaults.MinHoldMs, gapBuy: 0, gapSell: 80, bidA: 100.31), Thresholds(), null);

        Assert.Null(engine.CurrentCluster);
    }

    [Fact]
    public void Step_ClosesSellOnGapRevertAfterMinHold()
    {
        var engine = new DryRunClusterEngine();
        engine.Step(Snapshot(0, gapBuy: 0, gapSell: 80), Thresholds(), SignalSide.SellB);

        engine.Step(Snapshot(StrategyDefaults.MinHoldMs, gapBuy: 0, gapSell: 20), Thresholds(), null);

        Assert.Null(engine.CurrentCluster);
    }

    [Fact]
    public void Step_DoesNotCloseBeforeMinHoldUnlessEmergency()
    {
        var engine = new DryRunClusterEngine();
        engine.Step(Snapshot(0, gapBuy: -80, gapSell: 0), Thresholds(), SignalSide.BuyB);

        engine.Step(Snapshot(StrategyDefaults.MinHoldMs - 1, gapBuy: -15, gapSell: 0), Thresholds(), null);

        Assert.NotNull(engine.CurrentCluster);
    }

    [Fact]
    public void Step_MaxHoldClosesCluster()
    {
        var engine = new DryRunClusterEngine();
        engine.Step(Snapshot(0, gapBuy: -80, gapSell: 0), Thresholds(), SignalSide.BuyB);

        engine.Step(Snapshot(StrategyDefaults.MaxHoldMs, gapBuy: -80, gapSell: 0), Thresholds(), null);

        Assert.Null(engine.CurrentCluster);
    }

    [Fact]
    public void Step_FeedAStaleEntersEmergency()
    {
        var engine = new DryRunClusterEngine();

        var events = engine.Step(Snapshot(10_000, gapBuy: -80, gapSell: 0, tickAOffsetMs: -6000), Thresholds(), SignalSide.BuyB);

        Assert.Equal(BotState.Emergency, engine.State);
        Assert.Contains(events, e => e.Decision == "emergency");
    }

    [Fact]
    public void Step_FeedBStaleBlocksOpen()
    {
        var engine = new DryRunClusterEngine();

        var events = engine.Step(Snapshot(10_000, gapBuy: -80, gapSell: 0, tickBOffsetMs: -4000), Thresholds(), SignalSide.BuyB);

        Assert.Equal(BotState.Idle, engine.State);
        Assert.Null(engine.CurrentCluster);
        Assert.Contains(events, e => e.Decision == "guard block" && e.Reason == "feed B stale");
    }

    [Fact]
    public void Step_OpensWhenSymbolsDiffer()
    {
        var engine = new DryRunClusterEngine();

        var events = engine.Step(
            Snapshot(0, gapBuy: -80, gapSell: 0, symbolA: "XAUUSD.lmx", symbolB: "XAUUSD.s"),
            Thresholds(),
            SignalSide.BuyB);

        Assert.Equal(BotState.Holding, engine.State);
        Assert.Equal(DryRunSide.BuyB, engine.CurrentCluster?.Side);
        Assert.Equal("dry open", Assert.Single(events).Decision);
    }

    [Fact]
    public void Step_AbnormalSpreadBlocksOpen()
    {
        var engine = new DryRunClusterEngine();

        var events = engine.Step(Snapshot(0, gapBuy: -80, gapSell: 0, spreadB: 3), Thresholds(), SignalSide.BuyB);

        Assert.Null(engine.CurrentCluster);
        Assert.Contains(events, e => e.Decision == "guard block" && e.Reason == "spread B abnormal");
    }

    private static GapThresholds Thresholds()
    {
        return new GapThresholds(-50, 30, -15, 20, 0, 0, 10, 10, 1, 500, false);
    }

    private static MarketSnapshot Snapshot(
        long nowMs,
        int gapBuy,
        int gapSell,
        double bidA = 100,
        double askA = 101,
        double bidB = 100,
        double askB = 101,
        double spreadB = 1,
        long tickAOffsetMs = 0,
        long tickBOffsetMs = 0,
        string symbolA = "XAUUSD",
        string symbolB = "XAUUSD")
    {
        var effectiveNowMs = BaseNowMs + nowMs;
        var effectiveTickCountMs = 10_000_000 + nowMs;
        var a = new TickRecord(1, effectiveTickCountMs + tickAOffsetMs, bidA, askA, askA - bidA, effectiveNowMs + tickAOffsetMs, symbolA);
        var b = new TickRecord(1, effectiveTickCountMs + tickBOffsetMs, bidB, askB, spreadB, effectiveNowMs + tickBOffsetMs, symbolB);
        return new MarketSnapshot(a, b, effectiveNowMs, gapBuy, gapSell, effectiveTickCountMs);
    }
}
