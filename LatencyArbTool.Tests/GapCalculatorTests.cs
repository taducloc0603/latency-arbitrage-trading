using LatencyArbTool.Core.Models;
using LatencyArbTool.Core.Services;

namespace LatencyArbTool.Tests;

public sealed class GapCalculatorTests
{
    [Fact]
    public void Calculate_AAboveB_GivesPositiveBuyGap()
    {
        // A=4200, B=4100, point=1 -> A-B = +100 -> BUY B
        var a = Tick(4200, 4200);
        var b = Tick(4100, 4100);

        var (gapBuy, gapSell) = GapCalculator.Calculate(a, b, point: 1);

        Assert.Equal(100, gapBuy);
        Assert.Equal(100, gapSell);
    }

    [Fact]
    public void Calculate_ABelowB_GivesNegativeSellGap()
    {
        // A=4100, B=4200, point=1 -> A-B = -100 -> SELL B
        var a = Tick(4100, 4100);
        var b = Tick(4200, 4200);

        var (gapBuy, gapSell) = GapCalculator.Calculate(a, b, point: 1);

        Assert.Equal(-100, gapBuy);
        Assert.Equal(-100, gapSell);
    }

    [Fact]
    public void Calculate_UsesBidForBuyAskForSell_AndPointMultiplier()
    {
        var a = Tick(bid: 100.00, ask: 100.50);
        var b = Tick(bid: 99.90, ask: 100.80);

        var (gapBuy, gapSell) = GapCalculator.Calculate(a, b, point: 100);

        // gapBuy  = (int)(100.00*100) - (int)(99.90*100)  = 10000 - 9990  = 10
        // gapSell = (int)(100.50*100) - (int)(100.80*100) = 10050 - 10080 = -30
        Assert.Equal(10, gapBuy);
        Assert.Equal(-30, gapSell);
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
