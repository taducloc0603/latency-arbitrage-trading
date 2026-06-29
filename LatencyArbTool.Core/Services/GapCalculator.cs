using LatencyArbTool.Core.Models;

namespace LatencyArbTool.Core.Services;

public static class GapCalculator
{
    // Same-side A - B divergence (does NOT subtract spread):
    //   GapBuy  = (int)(A.Bid * point) - (int)(B.Bid * point)
    //   GapSell = (int)(A.Ask * point) - (int)(B.Ask * point)
    //
    // A is the fast feed, B the slow one; orders open on B.
    //   A above B -> GapBuy > 0  -> BUY B  (A.Bid above B.Bid).
    //   A below B -> GapSell < 0 -> SELL B (A.Ask below B.Ask).
    // OpenSignalEngine: BUY when GapBuy >= x ; SELL when GapSell <= -x.
    public static (int GapBuy, int GapSell) Calculate(TickRecord a, TickRecord b, int point)
    {
        var gapBuy = ToPoints(a.Bid, point) - ToPoints(b.Bid, point);
        var gapSell = ToPoints(a.Ask, point) - ToPoints(b.Ask, point);
        return (gapBuy, gapSell);
    }

    // Truncating cast (matches the spec's (int)(price * point)).
    public static int ToPoints(double price, int point) => (int)(price * point);
}
