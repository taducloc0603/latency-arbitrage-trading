using LatencyArbTool.Core.Models;
using LatencyArbTool.Core.Services;

namespace LatencyArbTool.Tests;

public sealed class TrailingStopEngineTests
{
    // point=1 with zero spread (bid==ask) so EntryPoint == price and
    // Current == price, matching the spec's single-price examples.
    private static StrategyConfig Cfg() => StrategyConfig.Default with
    {
        Point = 1,
        StopLossPoint = 50,
        TrailingStartPoint = 200,
        TrailingStepPoint = 30,
    };

    private static DryRunEvent? Step(TrailingStopEngine e, double price, SignalSide? signal, long t, StrategyConfig c)
        => e.Step(price, price, signal, t, c).SingleOrDefault();

    [Fact]
    public void Buy_OpensOnSignalThenStopLoss()
    {
        var e = new TrailingStopEngine();
        var c = Cfg();

        var open = Step(e, 1000, SignalSide.BuyB, 0, c);
        Assert.Equal("live open", open!.Decision);
        Assert.False(e.IsFlat);

        Assert.Null(Step(e, 960, null, 1, c)); // 960 > 950, no close
        var close = Step(e, 950, null, 2, c);  // 950 <= 1000-50
        Assert.Equal("live close", close!.Decision);
        Assert.Equal("stop loss", close.Reason);
        Assert.True(e.IsFlat);
    }

    [Fact]
    public void Buy_TrailingActivatesAndClosesOnRetrace()
    {
        var e = new TrailingStopEngine();
        var c = Cfg();
        Step(e, 1000, SignalSide.BuyB, 0, c);

        Assert.Null(Step(e, 1200, null, 1, c)); // activate, Highest=1200, stop=1170
        Assert.Null(Step(e, 1171, null, 2, c)); // above 1170
        Assert.Null(Step(e, 1300, null, 3, c)); // Highest=1300, stop=1270
        Assert.Null(Step(e, 1450, null, 4, c)); // Highest=1450, stop=1420
        var close = Step(e, 1420, null, 5, c);  // <= 1420
        Assert.Equal("trailing stop", close!.Reason);
        Assert.True(close.TrailingActive);
    }

    [Fact]
    public void Buy_StopLossNotCheckedOnceTrailingActive()
    {
        var e = new TrailingStopEngine();
        var c = Cfg();
        Step(e, 1000, SignalSide.BuyB, 0, c);
        Step(e, 1200, null, 1, c);              // active, Highest=1200
        // Drop straight to 940 (below SL 950) but trailing stop is 1170 -> trailing closes.
        var close = Step(e, 940, null, 2, c);
        Assert.Equal("trailing stop", close!.Reason);
    }

    [Fact]
    public void Sell_OpensOnSignalThenStopLoss()
    {
        var e = new TrailingStopEngine();
        var c = Cfg();

        var open = Step(e, 1000, SignalSide.SellB, 0, c);
        Assert.Equal("live open", open!.Decision);
        Assert.Equal(DryRunSide.SellB, open.Side);

        Assert.Null(Step(e, 1040, null, 1, c)); // 1040 < 1050
        var close = Step(e, 1050, null, 2, c);  // >= 1000+50
        Assert.Equal("stop loss", close!.Reason);
    }

    [Fact]
    public void Sell_TrailingActivatesAndClosesOnRetrace()
    {
        var e = new TrailingStopEngine();
        var c = Cfg();
        Step(e, 1000, SignalSide.SellB, 0, c);

        Assert.Null(Step(e, 800, null, 1, c)); // activate, Lowest=800, stop=830
        Assert.Null(Step(e, 829, null, 2, c)); // below 830
        Assert.Null(Step(e, 700, null, 3, c)); // Lowest=700, stop=730
        Assert.Null(Step(e, 550, null, 4, c)); // Lowest=550, stop=580
        var close = Step(e, 580, null, 5, c);  // >= 580
        Assert.Equal("trailing stop", close!.Reason);
    }

    [Fact]
    public void ApplyOpenFill_ReanchorsEntryForStopLoss()
    {
        var e = new TrailingStopEngine();
        var c = Cfg(); // point=1, SL=50

        Step(e, 1000, SignalSide.BuyB, 0, c);     // decide entry = 1000
        var id = e.Current!.ClusterId;

        Assert.True(e.ApplyOpenFill(id, 1010, c.Point)); // real fill 1010
        Assert.Equal(1010, e.Current!.EntryPoint);

        Assert.Null(Step(e, 961, null, 1, c));     // safe vs new SL (1010-50=960)
        var close = Step(e, 960, null, 2, c);      // hits SL at real entry
        Assert.Equal("stop loss", close!.Reason);
    }

    [Fact]
    public void ApplyOpenFill_WrongCluster_NoChange()
    {
        var e = new TrailingStopEngine();
        var c = Cfg();
        Step(e, 1000, SignalSide.BuyB, 0, c);

        Assert.False(e.ApplyOpenFill(999, 1010, c.Point));
        Assert.Equal(1000, e.Current!.EntryPoint);
    }

    [Fact]
    public void Open_OnlyWhenSignalAndFlat()
    {
        var e = new TrailingStopEngine();
        var c = Cfg();

        Assert.Empty(e.Step(1000, 1000, null, 0, c)); // no signal -> no open
        Assert.True(e.IsFlat);

        e.Step(1000, 1000, SignalSide.BuyB, 1, c);    // open
        // While holding, a fresh signal must not open a second position.
        var evts = e.Step(1010, 1010, SignalSide.BuyB, 2, c);
        Assert.DoesNotContain(evts, x => x.Decision == "live open");
    }
}
