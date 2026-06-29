using LatencyArbTool.Core.Models;
using LatencyArbTool.Core.Services;

namespace LatencyArbTool.Tests;

public sealed class OpenSignalEngineTests
{
    // x=100, y=1000ms, z=50
    private static StrategyConfig Cfg() => StrategyConfig.Default with
    {
        OpenPts = 100,
        OpenHoldConfirmMs = 1000,
        OpenConfirmGapPts = 50,
    };

    [Fact]
    public void Buy_FiresAfterSustainedWindowAndFinalTrigger()
    {
        var e = new OpenSignalEngine();
        var c = Cfg();

        Assert.Null(e.Evaluate(120, 0, 0, c));     // start window
        Assert.Null(e.Evaluate(120, 0, 999, c));   // not enough time yet
        Assert.Equal(SignalSide.BuyB, e.Evaluate(120, 0, 1000, c));
    }

    [Fact]
    public void Buy_ResetsWhenGapDropsBelowSustainFloor()
    {
        var e = new OpenSignalEngine();
        var c = Cfg();

        e.Evaluate(120, 0, 0, c);
        Assert.Null(e.Evaluate(40, 0, 500, c));    // below z -> reset
        Assert.Null(e.Evaluate(120, 0, 1400, c));  // restart at 1400
        Assert.Null(e.Evaluate(120, 0, 2399, c));  // 999ms < y
        Assert.Equal(SignalSide.BuyB, e.Evaluate(120, 0, 2400, c));
    }

    [Fact]
    public void Buy_DoesNotFireWhenFinalBelowTrigger_ThenFiresWhenItClears()
    {
        var e = new OpenSignalEngine();
        var c = Cfg();

        // Held >= z (50) but final 60 < x (100): window satisfied, trigger not.
        e.Evaluate(60, 0, 0, c);
        Assert.Null(e.Evaluate(60, 0, 1000, c));
        // Gap never dropped below z, now clears x at a later tick -> fires.
        Assert.Equal(SignalSide.BuyB, e.Evaluate(120, 0, 1100, c));
    }

    [Fact]
    public void Sell_FiresMirrorOfBuy()
    {
        var e = new OpenSignalEngine();
        var c = Cfg();

        Assert.Null(e.Evaluate(0, -120, 0, c));
        Assert.Null(e.Evaluate(0, -120, 999, c));
        Assert.Equal(SignalSide.SellB, e.Evaluate(0, -120, 1000, c));
    }

    [Fact]
    public void LastWindow_CapturedWhenSignalFires()
    {
        var e = new OpenSignalEngine();
        var c = Cfg(); // x=100, y=1000, z=50

        e.Evaluate(120, 0, 0, c);
        e.Evaluate(150, 0, 200, c);
        e.Evaluate(95, 0, 500, c);
        var s = e.Evaluate(130, 0, 1000, c);

        Assert.Equal(SignalSide.BuyB, s);
        var w = e.LastWindow!;
        Assert.NotNull(w);
        Assert.Equal(4, w.Count);
        Assert.Equal(95, w.Min);
        Assert.Equal(150, w.Max);
        Assert.Equal(120, w.First);
        Assert.Equal(130, w.Last);
        Assert.Equal(1000, w.DurationMs);
        Assert.Equal(50, w.Z);
        Assert.Equal(100, w.X);
        Assert.Equal(new[] { 120, 150, 95, 130 }, w.Gaps);
    }

    [Fact]
    public void CurrentWindow_ReportsActiveBuyWindow()
    {
        var e = new OpenSignalEngine();
        var c = Cfg(); // z=50

        Assert.Equal("idle", e.CurrentWindow(0).State);

        e.Evaluate(120, 0, 0, c);   // buy window starts (>= z)
        e.Evaluate(90, 0, 200, c);
        var w = e.CurrentWindow(500);
        Assert.Equal("buy", w.State);
        Assert.Equal(2, w.Count);
        Assert.Equal(90, w.Min);
        Assert.Equal(120, w.Max);
        Assert.Equal(500, w.DurationMs);

        e.Evaluate(40, 0, 600, c);  // drops below z -> window reset
        Assert.Equal("idle", e.CurrentWindow(600).State);
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var e = new OpenSignalEngine();
        var c = Cfg();

        e.Evaluate(120, 0, 0, c);
        e.Reset();
        Assert.Null(e.Evaluate(120, 0, 1000, c)); // window restarts after reset
        Assert.Equal(SignalSide.BuyB, e.Evaluate(120, 0, 2000, c));
    }
}
