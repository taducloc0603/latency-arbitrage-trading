using LatencyArbTool.Core.Models;

namespace LatencyArbTool.Core.Services;

public static class GapCalculator
{
    public static (int GapBuy, int GapSell) Calculate(TickRecord a, TickRecord b)
    {
        var gapBuy = ToPoints(b.Bid - a.Ask);
        var gapSell = ToPoints(b.Ask - a.Bid);
        return (gapBuy, gapSell);
    }

    private static int ToPoints(double priceDelta)
    {
        return (int)Math.Round(
            priceDelta * StrategyDefaults.PointMultiplier,
            MidpointRounding.AwayFromZero);
    }
}

