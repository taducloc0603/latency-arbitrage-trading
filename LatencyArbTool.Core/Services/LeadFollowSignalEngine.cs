using LatencyArbTool.Core.Models;

namespace LatencyArbTool.Core.Services;

public sealed class LeadFollowSignalEngine
{
    private long? _extremeSinceBuyMs;
    private long? _extremeSinceSellMs;

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
    }

    private bool UpdateBuy(MarketSnapshot snapshot, GapThresholds thresholds)
    {
        if (snapshot.GapBuy > thresholds.OpenBuy)
        {
            _extremeSinceBuyMs = null;
            return false;
        }

        _extremeSinceBuyMs ??= snapshot.NowMs;
        return snapshot.NowMs - _extremeSinceBuyMs.Value >= StrategyDefaults.ConfirmMs;
    }

    private bool UpdateSell(MarketSnapshot snapshot, GapThresholds thresholds)
    {
        if (snapshot.GapSell < thresholds.OpenSell)
        {
            _extremeSinceSellMs = null;
            return false;
        }

        _extremeSinceSellMs ??= snapshot.NowMs;
        return snapshot.NowMs - _extremeSinceSellMs.Value >= StrategyDefaults.ConfirmMs;
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

