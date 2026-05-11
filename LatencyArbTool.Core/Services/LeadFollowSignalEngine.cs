using LatencyArbTool.Core.Models;

namespace LatencyArbTool.Core.Services;

public sealed class LeadFollowSignalEngine
{
    private long? _extremeSinceBuyMs;
    private long? _extremeSinceSellMs;
    private long? _confirmReachedAtBuyMs;
    private long? _confirmReachedAtSellMs;
    private int? _peakGapBuy;
    private int? _peakGapSell;

    // Exposed for diagnostic logging — lets the CSV capture how close the signal got.
    public long? ExtremeSinceBuyMs => _extremeSinceBuyMs;
    public long? ExtremeSinceSellMs => _extremeSinceSellMs;
    public long? ConfirmReachedAtBuyMs => _confirmReachedAtBuyMs;
    public long? ConfirmReachedAtSellMs => _confirmReachedAtSellMs;
    public int? PeakGapBuy => _peakGapBuy;
    public int? PeakGapSell => _peakGapSell;

    public SignalSide? Evaluate(MarketSnapshot snapshot, GapThresholds thresholds)
    {
        // Loosened: previously we reset whenever PollMissedTicks was true, but with a
        // fast A feed and 25ms polling that fires nearly every tick and prevents the
        // confirm/recheck windows from ever accumulating. Continuity is now governed
        // solely by gap-vs-threshold: if the unseen intermediate tick crossed back,
        // the next observed gap will be on the wrong side and reset state naturally.
        var buyConfirmed = UpdateBuy(snapshot, thresholds);
        var sellConfirmed = UpdateSell(snapshot, thresholds);

        return (buyConfirmed, sellConfirmed) switch
        {
            (true, false) => SignalSide.BuyB,
            (false, true) => SignalSide.SellB,
            (true, true) => StrongerSide(snapshot, thresholds),
            _ => null
        };
    }

    public void Reset()
    {
        _extremeSinceBuyMs = null;
        _extremeSinceSellMs = null;
        _confirmReachedAtBuyMs = null;
        _confirmReachedAtSellMs = null;
        _peakGapBuy = null;
        _peakGapSell = null;
    }

    private bool UpdateBuy(MarketSnapshot snapshot, GapThresholds thresholds)
    {
        if (snapshot.GapBuy > thresholds.OpenBuy)
        {
            _extremeSinceBuyMs = null;
            _confirmReachedAtBuyMs = null;
            _peakGapBuy = null;
            return false;
        }

        _extremeSinceBuyMs ??= snapshot.NowMs;
        _peakGapBuy = _peakGapBuy.HasValue ? Math.Min(_peakGapBuy.Value, snapshot.GapBuy) : snapshot.GapBuy;

        if (snapshot.NowMs - _extremeSinceBuyMs.Value < StrategyDefaults.ConfirmMs)
        {
            return false;
        }

        _confirmReachedAtBuyMs ??= snapshot.NowMs;

        if (snapshot.NowMs - _confirmReachedAtBuyMs.Value < StrategyDefaults.ReCheckMs)
        {
            return false;
        }

        if (!IsStable(snapshot.GapBuy, _peakGapBuy.Value))
        {
            return false;
        }

        // Velocity gate: skip if gap is already drifting back toward zero. For
        // a buy candidate the favorable direction is "more negative", so we
        // require velocity below -MinFavorableVelocityPtsPerSec.
        if (thresholds.GapBuyVelocityPtsPerSec > -StrategyDefaults.MinFavorableVelocityPtsPerSec)
        {
            return false;
        }

        // Lead-detection gate: BUY thesis is "B will rise to match A". For that
        // to hold, A must have moved up recently and led B. If B moved instead
        // (B-led gap), the bet is wrong direction — skip.
        return LeadIsA(expectAPositive: true, thresholds);
    }

    private bool UpdateSell(MarketSnapshot snapshot, GapThresholds thresholds)
    {
        if (snapshot.GapSell < thresholds.OpenSell)
        {
            _extremeSinceSellMs = null;
            _confirmReachedAtSellMs = null;
            _peakGapSell = null;
            return false;
        }

        _extremeSinceSellMs ??= snapshot.NowMs;
        _peakGapSell = _peakGapSell.HasValue ? Math.Max(_peakGapSell.Value, snapshot.GapSell) : snapshot.GapSell;

        if (snapshot.NowMs - _extremeSinceSellMs.Value < StrategyDefaults.ConfirmMs)
        {
            return false;
        }

        _confirmReachedAtSellMs ??= snapshot.NowMs;

        if (snapshot.NowMs - _confirmReachedAtSellMs.Value < StrategyDefaults.ReCheckMs)
        {
            return false;
        }

        if (!IsStable(snapshot.GapSell, _peakGapSell.Value))
        {
            return false;
        }

        // Velocity gate: for a sell candidate, favorable direction is "more
        // positive", so we require velocity above +MinFavorableVelocityPtsPerSec.
        if (thresholds.GapSellVelocityPtsPerSec < StrategyDefaults.MinFavorableVelocityPtsPerSec)
        {
            return false;
        }

        // Lead-detection gate: SELL thesis is "B will fall to match A". For that
        // to hold, A must have moved down recently and led B. If B moved instead
        // (B-led gap), the bet is wrong direction — skip.
        return LeadIsA(expectAPositive: false, thresholds);
    }

    // Verifies A drove the move that produced the gap, in the direction the
    // trade thesis expects. expectAPositive=true means A should have risen (BUY
    // thesis); false means A should have fallen (SELL thesis). |A move| must
    // also dominate |B move| by LeadRatio to rule out B-led gaps.
    private static bool LeadIsA(bool expectAPositive, GapThresholds thresholds)
    {
        var aChange = thresholds.MidAChangePtsInWindow;
        var bChange = thresholds.MidBChangePtsInWindow;

        if (expectAPositive)
        {
            if (aChange < StrategyDefaults.MinLeadChangePts) return false;
        }
        else
        {
            if (aChange > -StrategyDefaults.MinLeadChangePts) return false;
        }

        return Math.Abs(aChange) >= Math.Abs(bChange) * StrategyDefaults.LeadRatio;
    }

    private static bool IsStable(int currentGap, int peakGap)
    {
        var peakAbs = Math.Abs(peakGap);
        if (peakAbs == 0)
        {
            return true;
        }

        var currentAbs = Math.Abs(currentGap);
        return currentAbs >= peakAbs * StrategyDefaults.StabilityRatio;
    }

    private static SignalSide StrongerSide(MarketSnapshot snapshot, GapThresholds thresholds)
    {
        var buyScore = Score(snapshot.GapBuy, thresholds.MedianBuy, thresholds.StdBuy);
        var sellScore = Score(snapshot.GapSell, thresholds.MedianSell, thresholds.StdSell);
        return buyScore >= sellScore ? SignalSide.BuyB : SignalSide.SellB;
    }

    private static double Score(int value, double median, double std)
    {
        return std <= double.Epsilon
            ? Math.Abs(value - median)
            : Math.Abs(value - median) / std;
    }
}
