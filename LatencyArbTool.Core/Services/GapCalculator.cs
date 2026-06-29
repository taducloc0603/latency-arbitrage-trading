using LatencyArbTool.Core.Models;

namespace LatencyArbTool.Core.Services;

public static class GapCalculator
{
    // Orders open only on B (one leg): a BUY pays B.Ask and exits at B.Bid, so a
    // round trip loses the full B spread. The real catch-up profit when B's mid
    // converges to A's mid is (midA - midB) - spreadB. So we threshold the
    // spread-adjusted mid divergence — x = the minimum REAL profit after paying B's spread.
    //
    //   D = midA - midB
    //   GapBuy  = D - spreadB     -> BUY  when GapBuy  >= x
    //   GapSell = D + spreadB     -> SELL when GapSell <= -x  (<=> (midB - midA) - spreadB >= x)
    //
    // A is the fast feed, B the slow one. Thresholds in OpenSignalEngine keep the
    // same shape; only the meaning of the gap changes.
    public static (int GapBuy, int GapSell) Calculate(TickRecord a, TickRecord b, int point)
    {
        var midA = (a.Bid + a.Ask) / 2.0;
        var midB = (b.Bid + b.Ask) / 2.0;
        var spreadB = b.Ask - b.Bid;
        var d = midA - midB;

        var gapBuy = ToPoints(d - spreadB, point);
        var gapSell = ToPoints(d + spreadB, point);
        return (gapBuy, gapSell);
    }

    // Truncating cast (matches the spec's (int)(price * point)).
    public static int ToPoints(double price, int point) => (int)(price * point);
}
