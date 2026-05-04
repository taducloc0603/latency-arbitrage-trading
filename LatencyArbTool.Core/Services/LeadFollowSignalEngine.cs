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

    public SignalSide? Evaluate(MarketSnapshot snapshot, GapThresholds thresholds)
    {
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

        return IsStable(snapshot.GapBuy, _peakGapBuy.Value);
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

        return IsStable(snapshot.GapSell, _peakGapSell.Value);
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
