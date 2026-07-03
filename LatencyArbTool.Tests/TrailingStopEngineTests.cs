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

    private static void CloseAndConfirm(TrailingStopEngine e, DryRunEvent close)
        => Assert.True(e.ConfirmClose(close.ClusterId!.Value));

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

        // The engine keeps the position until the close click is confirmed.
        Assert.False(e.IsFlat);
        CloseAndConfirm(e, close);
        Assert.True(e.IsFlat);
    }

    [Fact]
    public void Close_NotConfirmed_RetriesOnCadence()
    {
        var e = new TrailingStopEngine();
        var c = Cfg();
        Step(e, 1000, SignalSide.BuyB, 0, c);

        var close = Step(e, 950, null, 1000, c);
        Assert.Equal("stop loss", close!.Reason);

        // Within the retry window: no duplicate close event, and no re-open even
        // if a signal arrives (the position is still owned by the engine).
        Assert.Null(Step(e, 951, SignalSide.BuyB, 1200, c));

        // After the retry window a retry close is emitted at the current price.
        var retry = Step(e, 949, null, 1600, c);
        Assert.Equal("live close", retry!.Decision);
        Assert.Equal("stop loss (retry)", retry.Reason);
        Assert.False(e.IsFlat);

        CloseAndConfirm(e, retry);
        Assert.True(e.IsFlat);
    }

    [Fact]
    public void AbortOpen_RollsBackFailedOpenClick()
    {
        var e = new TrailingStopEngine();
        var c = Cfg();

        var open = Step(e, 1000, SignalSide.BuyB, 0, c);
        Assert.False(e.IsFlat);

        Assert.True(e.AbortOpen(open!.ClusterId!.Value));
        Assert.True(e.IsFlat);
    }

    [Fact]
    public void AbortOpen_IgnoredOnceCloseRequested()
    {
        var e = new TrailingStopEngine();
        var c = Cfg();
        var open = Step(e, 1000, SignalSide.BuyB, 0, c);
        Step(e, 950, null, 1, c); // close requested

        Assert.False(e.AbortOpen(open!.ClusterId!.Value));
        Assert.False(e.IsFlat);
    }

    [Fact]
    public void ConfirmClose_WrongCluster_NoChange()
    {
        var e = new TrailingStopEngine();
        var c = Cfg();
        Step(e, 1000, SignalSide.BuyB, 0, c);
        Step(e, 950, null, 1, c);

        Assert.False(e.ConfirmClose(999));
        Assert.False(e.IsFlat);
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
    public void ApplyOpenFill_ReanchorsEntryForStopLossAndStoresTicket()
    {
        var e = new TrailingStopEngine();
        var c = Cfg(); // point=1, SL=50

        Step(e, 1000, SignalSide.BuyB, 0, c);     // decide entry = 1000
        var id = e.Current!.ClusterId;

        Assert.True(e.ApplyOpenFill(id, 777, 1010, c.Point)); // real fill 1010
        Assert.Equal(1010, e.Current!.EntryPoint);
        Assert.Equal(777ul, e.Current!.Ticket);

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

        Assert.False(e.ApplyOpenFill(999, 777, 1010, c.Point));
        Assert.Equal(1000, e.Current!.EntryPoint);
        Assert.Null(e.Current!.Ticket);
    }

    // Config matching the business spec's examples: SL=80, start=50, step=50.
    private static StrategyConfig SpecCfg() => StrategyConfig.Default with
    {
        Point = 1,
        StopLossPoint = 80,
        TrailingStartPoint = 50,
        TrailingStepPoint = 50,
    };

    [Fact]
    public void Buy_StopTrailsMaxBeforeActivation()
    {
        var e = new TrailingStopEngine();
        var c = SpecCfg();
        Step(e, 1000, SignalSide.BuyB, 0, c);

        // Spec example 1: 1000 -> 1040 -> 1030: Max=1040, stop=960, no close.
        Assert.Null(Step(e, 1040, null, 1, c));
        Assert.Null(Step(e, 1030, null, 2, c));

        // Spec example 3: drop to 980 then 960: stop is Max-80=960, NOT entry-80=920.
        Assert.Null(Step(e, 980, null, 3, c));
        var close = Step(e, 960, null, 4, c);
        Assert.Equal("stop loss", close!.Reason);
        Assert.False(close.TrailingActive);
    }

    [Fact]
    public void Buy_StopStaysAtEntryWhilePriceBelowEntry()
    {
        var e = new TrailingStopEngine();
        var c = SpecCfg();
        Step(e, 1000, SignalSide.BuyB, 0, c);

        // Spec example 2: 1000 -> 990 -> 995: Max stays 1000, stop=920.
        Assert.Null(Step(e, 990, null, 1, c));
        Assert.Null(Step(e, 995, null, 2, c));
        Assert.Null(Step(e, 921, null, 3, c));
        var close = Step(e, 920, null, 4, c);
        Assert.Equal("stop loss", close!.Reason);
    }

    [Fact]
    public void Buy_FullSequencePerSpec()
    {
        var e = new TrailingStopEngine();
        var c = SpecCfg();
        Step(e, 1000, SignalSide.BuyB, 0, c);

        // 1040 -> 1050 (activates) -> 1080 -> 1120 -> 1100 -> 1070 closes.
        Assert.Null(Step(e, 1040, null, 1, c));
        Assert.Null(Step(e, 1050, null, 2, c)); // active; stop=1050-50=1000
        Assert.Null(Step(e, 1080, null, 3, c));
        Assert.Null(Step(e, 1120, null, 4, c)); // Max=1120, stop=1070
        Assert.Null(Step(e, 1100, null, 5, c));
        var close = Step(e, 1070, null, 6, c);
        Assert.Equal("trailing stop", close!.Reason);
        Assert.True(close.TrailingActive);
    }

    [Fact]
    public void Buy_StopLossClose_ReportsMaxAndStopLevel()
    {
        var e = new TrailingStopEngine();
        var c = SpecCfg(); // point=1 so price == point
        Step(e, 1000, SignalSide.BuyB, 0, c);
        Step(e, 1040, null, 1, c); // Max = 1040

        var close = Step(e, 960, null, 2, c); // stop = 1040 - 80 = 960
        Assert.Equal("stop loss", close!.Reason);
        Assert.False(close.TrailingActive);
        Assert.Equal(1040, close.StopRefPrice, 5);  // Max
        Assert.Equal(960, close.StopLevelPrice, 5);  // Max - StopLoss
    }

    [Fact]
    public void Buy_TrailingClose_ReportsStepStopLevel()
    {
        var e = new TrailingStopEngine();
        var c = SpecCfg();
        Step(e, 1000, SignalSide.BuyB, 0, c);
        Step(e, 1120, null, 1, c); // active; Max = 1120

        var close = Step(e, 1070, null, 2, c); // stop = 1120 - 50 = 1070
        Assert.Equal("trailing stop", close!.Reason);
        Assert.True(close.TrailingActive);
        Assert.Equal(1120, close.StopRefPrice, 5);
        Assert.Equal(1070, close.StopLevelPrice, 5); // Max - TrailingStep, NOT Max - SL
    }

    [Fact]
    public void Buy_ActivationGapTickDoesNotInstantClose()
    {
        var e = new TrailingStopEngine();
        var c = SpecCfg();
        Step(e, 1000, SignalSide.BuyB, 0, c);

        // Single tick jumps straight to entry+start: activates, stop=1000, no close.
        Assert.Null(Step(e, 1050, null, 1, c));
        Assert.False(e.IsFlat);
        Assert.True(e.Current!.TrailingActive);
    }

    [Fact]
    public void Sell_StopTrailsMinBeforeActivation()
    {
        var e = new TrailingStopEngine();
        var c = SpecCfg();
        Step(e, 1000, SignalSide.SellB, 0, c);

        // Mirror of the BUY example: 1000 -> 960 -> 970: Min=960, stop=1040.
        Assert.Null(Step(e, 960, null, 1, c));  // not active (needs <= 950)
        Assert.Null(Step(e, 970, null, 2, c));
        Assert.Null(Step(e, 1039, null, 3, c));
        var close = Step(e, 1040, null, 4, c);
        Assert.Equal("stop loss", close!.Reason);
        Assert.False(close.TrailingActive);
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
