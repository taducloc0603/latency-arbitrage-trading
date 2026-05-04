using LatencyArbTool.Core.Models;
using LatencyArbTool.Core.Services;

namespace LatencyArbTool.Tests;

public sealed class LeadFollowSignalEngineTests
{
    private const int TriggerMs = StrategyDefaults.ConfirmMs + StrategyDefaults.ReCheckMs;

    [Fact]
    public void Evaluate_BuyRequiresContinuousConfirmation()
    {
        var engine = new LeadFollowSignalEngine();
        var thresholds = Thresholds();

        Assert.Null(engine.Evaluate(Snapshot(0, gapBuy: -60, gapSell: 0), thresholds));
        Assert.Null(engine.Evaluate(Snapshot(StrategyDefaults.ConfirmMs - 1, gapBuy: -60, gapSell: 0), thresholds));
        Assert.Null(engine.Evaluate(Snapshot(StrategyDefaults.ConfirmMs, gapBuy: -60, gapSell: 0), thresholds));
        Assert.Equal(SignalSide.BuyB, engine.Evaluate(Snapshot(TriggerMs, gapBuy: -60, gapSell: 0), thresholds));
    }

    [Fact]
    public void Evaluate_SellRequiresContinuousConfirmation()
    {
        var engine = new LeadFollowSignalEngine();
        var thresholds = Thresholds();

        Assert.Null(engine.Evaluate(Snapshot(0, gapBuy: 0, gapSell: 40), thresholds));
        Assert.Null(engine.Evaluate(Snapshot(StrategyDefaults.ConfirmMs, gapBuy: 0, gapSell: 40), thresholds));
        Assert.Equal(SignalSide.SellB, engine.Evaluate(Snapshot(TriggerMs, gapBuy: 0, gapSell: 40), thresholds));
    }

    [Fact]
    public void Evaluate_ResetsWhenGapLeavesThreshold()
    {
        var engine = new LeadFollowSignalEngine();
        var thresholds = Thresholds();

        Assert.Null(engine.Evaluate(Snapshot(0, gapBuy: -60, gapSell: 0), thresholds));
        Assert.Null(engine.Evaluate(Snapshot(StrategyDefaults.ConfirmMs / 2, gapBuy: -40, gapSell: 0), thresholds));
        Assert.Null(engine.Evaluate(Snapshot(TriggerMs + 200, gapBuy: -60, gapSell: 0), thresholds));
    }

    [Fact]
    public void Evaluate_BlocksWhenGapRevertsBelowStabilityRatio()
    {
        var engine = new LeadFollowSignalEngine();
        var thresholds = Thresholds();

        // Peak gap of -200, then revert to -60 (still below threshold but only 30% of peak)
        Assert.Null(engine.Evaluate(Snapshot(0, gapBuy: -200, gapSell: 0), thresholds));
        Assert.Null(engine.Evaluate(Snapshot(StrategyDefaults.ConfirmMs, gapBuy: -100, gapSell: 0), thresholds));
        Assert.Null(engine.Evaluate(Snapshot(TriggerMs, gapBuy: -60, gapSell: 0), thresholds));
    }

    [Fact]
    public void Evaluate_AllowsWhenGapStaysNearPeak()
    {
        var engine = new LeadFollowSignalEngine();
        var thresholds = Thresholds();

        // Peak -100, current -85 = 85% of peak (above 70% ratio)
        Assert.Null(engine.Evaluate(Snapshot(0, gapBuy: -100, gapSell: 0), thresholds));
        Assert.Null(engine.Evaluate(Snapshot(StrategyDefaults.ConfirmMs, gapBuy: -90, gapSell: 0), thresholds));
        Assert.Equal(SignalSide.BuyB, engine.Evaluate(Snapshot(TriggerMs, gapBuy: -85, gapSell: 0), thresholds));
    }

    [Fact]
    public void Evaluate_SellBlocksOnRevertBelowStabilityRatio()
    {
        var engine = new LeadFollowSignalEngine();
        var thresholds = Thresholds();

        // Peak +200, revert to +50 (only 25% of peak, below 70% ratio)
        Assert.Null(engine.Evaluate(Snapshot(0, gapBuy: 0, gapSell: 200), thresholds));
        Assert.Null(engine.Evaluate(Snapshot(StrategyDefaults.ConfirmMs, gapBuy: 0, gapSell: 100), thresholds));
        Assert.Null(engine.Evaluate(Snapshot(TriggerMs, gapBuy: 0, gapSell: 50), thresholds));
    }

    private static GapThresholds Thresholds()
    {
        return new GapThresholds(-50, 30, -15, 20, 0, 0, 10, 10, 1, 500, false);
    }

    private static MarketSnapshot Snapshot(long nowMs, int gapBuy, int gapSell)
    {
        var a = Tick(nowMs, 100, 101);
        var b = Tick(nowMs, 100, 101);
        return new MarketSnapshot(a, b, nowMs, gapBuy, gapSell, nowMs);
    }

    private static TickRecord Tick(long nowMs, double bid, double ask)
    {
        return new TickRecord(1, nowMs, bid, ask, 1, nowMs, "XAUUSD");
    }
}
