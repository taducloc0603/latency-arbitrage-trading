using LatencyArbTool.Core.Models;
using LatencyArbTool.Core.Services;

namespace LatencyArbTool.Tests;

public sealed class LeadFollowSignalEngineTests
{
    [Fact]
    public void Evaluate_BuyRequiresContinuousConfirmation()
    {
        var engine = new LeadFollowSignalEngine();
        var thresholds = Thresholds();

        Assert.Null(engine.Evaluate(Snapshot(0, gapBuy: -60, gapSell: 0), thresholds));
        Assert.Null(engine.Evaluate(Snapshot(999, gapBuy: -60, gapSell: 0), thresholds));
        Assert.Equal(SignalSide.BuyB, engine.Evaluate(Snapshot(1000, gapBuy: -60, gapSell: 0), thresholds));
    }

    [Fact]
    public void Evaluate_SellRequiresContinuousConfirmation()
    {
        var engine = new LeadFollowSignalEngine();
        var thresholds = Thresholds();

        Assert.Null(engine.Evaluate(Snapshot(0, gapBuy: 0, gapSell: 40), thresholds));
        Assert.Equal(SignalSide.SellB, engine.Evaluate(Snapshot(1000, gapBuy: 0, gapSell: 40), thresholds));
    }

    [Fact]
    public void Evaluate_ResetsWhenGapLeavesThreshold()
    {
        var engine = new LeadFollowSignalEngine();
        var thresholds = Thresholds();

        Assert.Null(engine.Evaluate(Snapshot(0, gapBuy: -60, gapSell: 0), thresholds));
        Assert.Null(engine.Evaluate(Snapshot(500, gapBuy: -40, gapSell: 0), thresholds));
        Assert.Null(engine.Evaluate(Snapshot(1200, gapBuy: -60, gapSell: 0), thresholds));
    }

    private static GapThresholds Thresholds()
    {
        return new GapThresholds(-50, 30, -15, 20, 0, 0, 10, 10, 1, 500, false);
    }

    private static MarketSnapshot Snapshot(long nowMs, int gapBuy, int gapSell)
    {
        var a = Tick(nowMs, 100, 101);
        var b = Tick(nowMs, 100, 101);
        return new MarketSnapshot(a, b, nowMs, gapBuy, gapSell);
    }

    private static TickRecord Tick(long nowMs, double bid, double ask)
    {
        return new TickRecord(1, nowMs, bid, ask, 1, nowMs, "XAUUSD");
    }
}

