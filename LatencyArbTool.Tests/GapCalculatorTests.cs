using LatencyArbTool.Core.Models;
using LatencyArbTool.Core.Services;

namespace LatencyArbTool.Tests;

public sealed class GapCalculatorTests
{
    [Fact]
    public void Calculate_AAboveB_BuyEdgeIsMidDivergenceMinusBSpread()
    {
        // midA=105, midB=85, D=20, spreadB=10 -> gapBuy = D - spreadB = 10
        var a = Tick(bid: 100, ask: 110);
        var b = Tick(bid: 80, ask: 90);

        var (gapBuy, gapSell) = GapCalculator.Calculate(a, b, point: 1);

        Assert.Equal(10, gapBuy);   // D - spreadB = 20 - 10
        Assert.Equal(30, gapSell);  // D + spreadB = 20 + 10 (>0 => no sell)
    }

    [Fact]
    public void Calculate_BAboveA_SellGapBelowMinusX()
    {
        // midA=85, midB=105, D=-20, spreadB=10 -> gapSell = D + spreadB = -10
        var a = Tick(bid: 80, ask: 90);
        var b = Tick(bid: 100, ask: 110);

        var (gapBuy, gapSell) = GapCalculator.Calculate(a, b, point: 1);

        Assert.Equal(-30, gapBuy);  // D - spreadB = -20 - 10
        Assert.Equal(-10, gapSell); // sell fires when <= -x ; sell room = (midB-midA)-spreadB = 10
    }

    [Fact]
    public void Calculate_SubtractsBSpread_WithPointMultiplier()
    {
        // midA=100.5, midB=98.5, D=2, spreadB=1 -> gapBuy=(2-1)*100=100, gapSell=(2+1)*100=300
        var a = Tick(bid: 100, ask: 101);
        var b = Tick(bid: 98, ask: 99);

        var (gapBuy, gapSell) = GapCalculator.Calculate(a, b, point: 100);

        Assert.Equal(100, gapBuy);
        Assert.Equal(300, gapSell);
    }

    [Fact]
    public void Calculate_EqualMidWideBSpread_NoSignalEitherSide()
    {
        // mid A = mid B = 100, spreadB=20 -> gapBuy=-20, gapSell=+20 (both far from +/-x)
        var a = Tick(bid: 95, ask: 105);
        var b = Tick(bid: 90, ask: 110);

        var (gapBuy, gapSell) = GapCalculator.Calculate(a, b, point: 1);

        Assert.Equal(-20, gapBuy);
        Assert.Equal(20, gapSell);
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
