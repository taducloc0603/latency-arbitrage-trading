using LatencyArbTool.Core.Models;
using LatencyArbTool.Core.Services;

namespace LatencyArbTool.Tests;

public sealed class GapCalculatorTests
{
    [Fact]
    public void Calculate_NetBuyRoom_ABidMinusBAsk()
    {
        // BUY room = A.Bid - B.Ask (cross B spread to enter at B.Ask).
        var a = Tick(bid: 100, ask: 101);
        var b = Tick(bid: 98, ask: 99);

        var (gapBuy, gapSell) = GapCalculator.Calculate(a, b, point: 1);

        Assert.Equal(1, gapBuy);   // A.Bid - B.Ask = 100 - 99
        Assert.Equal(3, gapSell);  // A.Ask - B.Bid = 101 - 98 (>0 => no sell)
    }

    [Fact]
    public void Calculate_NetSellRoom_BAboveA_GivesNegativeSellGap()
    {
        // B above A -> SELL B. gapSell = A.Ask - B.Bid <= -x  <=>  B.Bid - A.Ask >= x.
        var a = Tick(bid: 100, ask: 101);
        var b = Tick(bid: 110, ask: 111);

        var (gapBuy, gapSell) = GapCalculator.Calculate(a, b, point: 1);

        Assert.Equal(-11, gapBuy);  // A.Bid - B.Ask = 100 - 111
        Assert.Equal(-9, gapSell);  // A.Ask - B.Bid = 101 - 110 ; sell room = 9
    }

    [Fact]
    public void Calculate_NetGap_SubtractsBSpread_WithPointMultiplier()
    {
        var a = Tick(bid: 100.00, ask: 100.50);
        var b = Tick(bid: 99.90, ask: 100.80);

        var (gapBuy, gapSell) = GapCalculator.Calculate(a, b, point: 100);

        // gapBuy  = (int)(100.00*100) - (int)(100.80*100) = 10000 - 10080 = -80
        // gapSell = (int)(100.50*100) - (int)(99.90*100)  = 10050 - 9990  = 60
        Assert.Equal(-80, gapBuy);
        Assert.Equal(60, gapSell);
    }

    [Fact]
    public void ToPoints_Truncates()
    {
        Assert.Equal(10000, GapCalculator.ToPoints(100.005, 100));
    }

    private static TickRecord Tick(double bid, double ask)
    {
        return new TickRecord(1, 1_000, bid, ask, ask - bid, 1_000, "XAUUSD");
    }
}
