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
        Assert.Equal("live open", Assert.Single(events).Decision);
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

        var reversedAsk = 101.0 - StrategyDefaults.AReversalUsd - 0.01;
        engine.Step(Snapshot(StrategyDefaults.MinHoldMs, gapBuy: -80, gapSell: 0, askA: reversedAsk), Thresholds(), null);

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

        var reversedBid = 100.0 + StrategyDefaults.AReversalUsd + 0.01;
        engine.Step(Snapshot(StrategyDefaults.MinHoldMs, gapBuy: 0, gapSell: 80, bidA: reversedBid), Thresholds(), null);

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

        var events = engine.Step(Snapshot(10_000, gapBuy: -80, gapSell: 0, feedASilenceMs: StrategyDefaults.FeedAStaleMs + 1000), Thresholds(), SignalSide.BuyB);

        Assert.Equal(BotState.Emergency, engine.State);
        Assert.Contains(events, e => e.Decision == "emergency");
    }

    [Fact]
    public void Step_FeedBStaleEmitsShadowBlockButStillOpens()
    {
        var engine = new DryRunClusterEngine();

        var events = engine.Step(Snapshot(10_000, gapBuy: -80, gapSell: 0, feedBSilenceMs: StrategyDefaults.FeedBStaleMs + 1000), Thresholds(), SignalSide.BuyB);

        Assert.Equal(BotState.Holding, engine.State);
        Assert.NotNull(engine.CurrentCluster);
        Assert.Contains(events, e => e.Decision == "shadow block" && e.Reason == "feed B stale");
        Assert.Contains(events, e => e.Decision == "live open" && e.ShadowBlockReasons.Contains("feed B stale"));
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
        Assert.Equal("live open", Assert.Single(events).Decision);
    }

    [Fact]
    public void Step_AbnormalSpreadEmitsShadowBlockButStillOpens()
    {
        var engine = new DryRunClusterEngine();

        var events = engine.Step(Snapshot(0, gapBuy: -80, gapSell: 0, spreadB: 10), Thresholds(), SignalSide.BuyB);

        Assert.NotNull(engine.CurrentCluster);
        Assert.Contains(events, e => e.Decision == "shadow block" && e.Reason == "spread B abnormal");
        Assert.Contains(events, e => e.Decision == "live open" && e.ShadowBlockReasons.Contains("spread B abnormal"));
    }

    [Fact]
    public void Step_LowAVolatilityEmitsShadowBlockButStillOpens()
    {
        var engine = new DryRunClusterEngine();
        var lowVolThresholds = Thresholds() with { ARangePoints = StrategyDefaults.MinAVolPoints - 1 };

        var events = engine.Step(Snapshot(0, gapBuy: -80, gapSell: 0), lowVolThresholds, SignalSide.BuyB);

        Assert.NotNull(engine.CurrentCluster);
        Assert.Contains(events, e => e.Decision == "shadow block" && e.Reason == "A volatility low");
        Assert.Contains(events, e => e.Decision == "live open" && e.ShadowBlockReasons.Contains("A volatility low"));
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
        long feedASilenceMs = 0,
        long feedBSilenceMs = 0,
        string symbolA = "XAUUSD",
        string symbolB = "XAUUSD")
    {
        var effectiveNowMs = BaseNowMs + nowMs;
        var effectiveTickCountMs = 10_000_000 + nowMs;
        var a = new TickRecord(1, effectiveTickCountMs, bidA, askA, askA - bidA, effectiveNowMs, symbolA);
        var b = new TickRecord(1, effectiveTickCountMs, bidB, askB, spreadB, effectiveNowMs, symbolB);
        return new MarketSnapshot(a, b, effectiveNowMs, gapBuy, gapSell, effectiveTickCountMs, feedASilenceMs, feedBSilenceMs);
    }
}
