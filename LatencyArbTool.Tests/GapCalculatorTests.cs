using LatencyArbTool.Core.Models;
using LatencyArbTool.Core.Services;

namespace LatencyArbTool.Tests;

public sealed class GapCalculatorTests
{
    [Fact]
    public void Calculate_UsesPointMultiplier100()
    {
        var a = Tick(100.00, 100.50);
        var b = Tick(99.90, 100.80);

        var (gapBuy, gapSell) = GapCalculator.Calculate(a, b);

        Assert.Equal(-60, gapBuy);
        Assert.Equal(80, gapSell);
        Assert.Equal(100, StrategyDefaults.PointMultiplier);
    }

    private static TickRecord Tick(double bid, double ask)
    {
        return new TickRecord(1, 1_000, bid, ask, ask - bid, 1_000, "XAUUSD");
    }
}

