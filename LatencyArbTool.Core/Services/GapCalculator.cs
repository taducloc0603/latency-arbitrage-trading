using LatencyArbTool.Core.Models;

namespace LatencyArbTool.Core.Services;

public static class GapCalculator
{
    // Spread-adjusted ("net") gaps — the entry crosses B's spread, so the gap is
    // measured against the price we actually trade at on B:
    //   GapBuy  = (int)(A.Bid * point) - (int)(B.Ask * point)   // buy B at B.Ask
    //   GapSell = (int)(A.Ask * point) - (int)(B.Bid * point)   // sell B at B.Bid
    //
    // A is the fast feed, B the slow one; orders open on B. Thresholds in
    // OpenSignalEngine keep the same shape:
    //   BUY  when GapBuy  >= x : A.Bid - B.Ask >= x  (>=x of room from entry to A).
    //   SELL when GapSell <= -x: A.Ask - B.Bid <= -x  <=>  B.Bid - A.Ask >= x.
    // Requiring the move to clear B's spread removes the spread-noise edge that
    // made every near-spread tick fire a (losing) BUY.
    public static (int GapBuy, int GapSell) Calculate(TickRecord a, TickRecord b, int point)
    {
        var gapBuy = ToPoints(a.Bid, point) - ToPoints(b.Ask, point);
        var gapSell = ToPoints(a.Ask, point) - ToPoints(b.Bid, point);
        return (gapBuy, gapSell);
    }

    // Truncating cast (matches the spec's (int)(price * point)).
    public static int ToPoints(double price, int point) => (int)(price * point);
}
