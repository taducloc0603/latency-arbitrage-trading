using LatencyArbTool.Core.Models;

namespace LatencyArbTool.Core.Services;

// Snapshot of the in-progress confirm window for diagnostic logging.
public readonly record struct WindowSnapshot(string State, long DurationMs, int Min, int Max, int Count);

// Produces an OPEN signal only — never used to close. A signal fires when the
// gap has continuously held the sustain floor (z = OpenConfirmGapPts) for the
// confirm window (y = OpenHoldConfirmMs) and the final gap clears the trigger
// (x = OpenPts).
//
//   BUY  : GapBuy  >= z held for y ms, and final GapBuy  >= x.
//   SELL : GapSell <= -z held for y ms, and final GapSell <= -x.
//
// While the window is open we buffer every gap sample so the caller can log the
// full window (for recheck). The stamp is reset the moment the gap drops below
// the floor — so "stamp held >= y ms" is equivalent to "every sample was >= floor".
public sealed class OpenSignalEngine
{
    private const int MaxWindowSamples = 5000;

    private long? _buyStartMs;
    private long? _sellStartMs;
    private readonly List<int> _buyGaps = new();
    private readonly List<int> _sellGaps = new();

    public long? BuyStartMs => _buyStartMs;
    public long? SellStartMs => _sellStartMs;

    // The confirm-window snapshot of the signal that fired on the most recent
    // Evaluate (null if none fired). Read it before calling Reset().
    public SignalWindow? LastWindow { get; private set; }

    // In-progress confirm window (for periodic snapshot logging). "buy"/"sell"/"idle".
    public WindowSnapshot CurrentWindow(long nowMs)
    {
        if (_buyStartMs is { } bs && _buyGaps.Count > 0)
        {
            return Summarize("buy", _buyGaps, bs, nowMs);
        }

        if (_sellStartMs is { } ss && _sellGaps.Count > 0)
        {
            return Summarize("sell", _sellGaps, ss, nowMs);
        }

        return new WindowSnapshot("idle", 0, 0, 0, 0);
    }

    private static WindowSnapshot Summarize(string state, List<int> gaps, long startMs, long nowMs)
    {
        int min = gaps[0], max = gaps[0];
        foreach (var g in gaps)
        {
            if (g < min) min = g;
            if (g > max) max = g;
        }

        return new WindowSnapshot(state, nowMs - startMs, min, max, gaps.Count);
    }

    public void Reset()
    {
        _buyStartMs = null;
        _sellStartMs = null;
        _buyGaps.Clear();
        _sellGaps.Clear();
        LastWindow = null;
    }

    public SignalSide? Evaluate(int gapBuy, int gapSell, long nowMs, StrategyConfig config)
    {
        var buy = UpdateBuy(gapBuy, nowMs, config);
        var sell = UpdateSell(gapSell, nowMs, config);

        // The gap formula's sign normally makes only one side eligible; if both
        // somehow qualify, prefer BUY deterministically.
        var side = (buy, sell) switch
        {
            (true, _) => (SignalSide?)SignalSide.BuyB,
            (false, true) => SignalSide.SellB,
            _ => null
        };

        LastWindow = side switch
        {
            SignalSide.BuyB => BuildWindow(_buyGaps, _buyStartMs, nowMs, config),
            SignalSide.SellB => BuildWindow(_sellGaps, _sellStartMs, nowMs, config),
            _ => null
        };

        return side;
    }

    private bool UpdateBuy(int gapBuy, long nowMs, StrategyConfig config)
    {
        if (gapBuy < config.OpenConfirmGapPts)
        {
            _buyStartMs = null;
            _buyGaps.Clear();
            return false;
        }

        _buyStartMs ??= nowMs;
        if (_buyGaps.Count < MaxWindowSamples)
        {
            _buyGaps.Add(gapBuy);
        }

        return nowMs - _buyStartMs.Value >= config.OpenHoldConfirmMs
               && gapBuy >= config.OpenPts;
    }

    private bool UpdateSell(int gapSell, long nowMs, StrategyConfig config)
    {
        if (gapSell > -config.OpenConfirmGapPts)
        {
            _sellStartMs = null;
            _sellGaps.Clear();
            return false;
        }

        _sellStartMs ??= nowMs;
        if (_sellGaps.Count < MaxWindowSamples)
        {
            _sellGaps.Add(gapSell);
        }

        return nowMs - _sellStartMs.Value >= config.OpenHoldConfirmMs
               && gapSell <= -config.OpenPts;
    }

    private static SignalWindow BuildWindow(List<int> gaps, long? startMs, long nowMs, StrategyConfig config)
    {
        if (gaps.Count == 0)
        {
            return new SignalWindow(0, 0, 0, 0, 0, 0, 0, config.OpenConfirmGapPts, config.OpenPts, Array.Empty<int>());
        }

        int min = gaps[0], max = gaps[0], sum = 0;
        foreach (var g in gaps)
        {
            if (g < min) min = g;
            if (g > max) max = g;
            sum += g;
        }

        return new SignalWindow(
            Count: gaps.Count,
            DurationMs: startMs.HasValue ? nowMs - startMs.Value : 0,
            Min: min,
            Max: max,
            First: gaps[0],
            Last: gaps[^1],
            Sum: sum,
            Z: config.OpenConfirmGapPts,
            X: config.OpenPts,
            Gaps: gaps.ToArray());
    }
}
