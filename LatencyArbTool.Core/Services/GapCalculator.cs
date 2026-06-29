using LatencyArbTool.Core.Models;

namespace LatencyArbTool.Core.Services;

public static class GapCalculator
{
    // GapBuy  = (int)(A.Bid * point) - (int)(B.Bid * point)
    // GapSell = (int)(A.Ask * point) - (int)(B.Ask * point)
    //
    // A is the fast feed, B the slow one; orders open on B.
    //   A above B -> GapBuy > 0  -> BUY B  (B is cheap, expect it to rise to A).
    //   A below B -> GapSell < 0 -> SELL B (B is rich, expect it to fall to A).
    public static (int GapBuy, int GapSell) Calculate(TickRecord a, TickRecord b, int point)
    {
        var gapBuy = ToPoints(a.Bid, point) - ToPoints(b.Bid, point);
        var gapSell = ToPoints(a.Ask, point) - ToPoints(b.Ask, point);
        return (gapBuy, gapSell);
    }

    // Truncating cast (matches the spec's (int)(price * point)).
    public static int ToPoints(double price, int point) => (int)(price * point);
}
