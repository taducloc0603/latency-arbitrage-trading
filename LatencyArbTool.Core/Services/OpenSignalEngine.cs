using LatencyArbTool.Core.Models;

namespace LatencyArbTool.Core.Services;

// Produces an OPEN signal only — never used to close. A signal fires when the
// gap has continuously held the sustain floor (z = OpenConfirmGapPts) for the
// confirm window (y = OpenHoldConfirmMs) and the final gap clears the trigger
// (x = OpenPts).
//
//   BUY  : GapBuy  >= z held for y ms, and final GapBuy  >= x.
//   SELL : GapSell <= -z held for y ms, and final GapSell <= -x.
//
// Implementation note: instead of buffering the whole window, we stamp when the
// gap first reached the floor and reset that stamp the moment it drops below —
// so "stamp held >= y ms" is equivalent to "every sample since was >= floor".
public sealed class OpenSignalEngine
{
    private long? _buyStartMs;
    private long? _sellStartMs;

    public long? BuyStartMs => _buyStartMs;
    public long? SellStartMs => _sellStartMs;

    public void Reset()
    {
        _buyStartMs = null;
        _sellStartMs = null;
    }

    public SignalSide? Evaluate(int gapBuy, int gapSell, long nowMs, StrategyConfig config)
    {
        var buy = UpdateBuy(gapBuy, nowMs, config);
        var sell = UpdateSell(gapSell, nowMs, config);

        // The gap formula's sign normally makes only one side eligible; if both
        // somehow qualify, prefer BUY deterministically.
        return (buy, sell) switch
        {
            (true, _) => SignalSide.BuyB,
            (false, true) => SignalSide.SellB,
            _ => null
        };
    }

    private bool UpdateBuy(int gapBuy, long nowMs, StrategyConfig config)
    {
        if (gapBuy < config.OpenConfirmGapPts)
        {
            _buyStartMs = null;
            return false;
        }

        _buyStartMs ??= nowMs;
        return nowMs - _buyStartMs.Value >= config.OpenHoldConfirmMs
               && gapBuy >= config.OpenPts;
    }

    private bool UpdateSell(int gapSell, long nowMs, StrategyConfig config)
    {
        if (gapSell > -config.OpenConfirmGapPts)
        {
            _sellStartMs = null;
            return false;
        }

        _sellStartMs ??= nowMs;
        return nowMs - _sellStartMs.Value >= config.OpenHoldConfirmMs
               && gapSell <= -config.OpenPts;
    }
}
