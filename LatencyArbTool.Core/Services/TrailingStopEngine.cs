using LatencyArbTool.Core.Models;

namespace LatencyArbTool.Core.Services;

// Manages the single live position on B. Opening happens ONLY when a signal is
// supplied and we are flat. Closing is signal-independent and follows a
// ratcheting stop off the best price since open (BUY shown; SELL mirrors):
//
//   1. MaxPrice = max(MaxPrice, Current)              — every tick, always
//   2. Current >= Entry + TrailingStart -> TrailingActive
//   3. Stop = MaxPrice - (TrailingActive ? TrailingStep : StopLoss)
//   4. Current <= Stop -> close ("trailing stop" / "stop loss")
//
// So even before trailing activates the stop trails the peak (MaxPrice - SL),
// not the entry.
//
// The engine's state must mirror the broker's: a position is only dropped after
// the caller confirms the close click succeeded (ConfirmClose) or the position
// is observed closed externally. While a close is pending the engine re-emits
// the close on a retry cadence instead of evaluating SL/trailing again. A
// failed open click is rolled back with AbortOpen.
//
// Prices are converted to points with config.Point. "Current" is the realistic
// close-side quote: BUY closes at B.Bid, SELL closes at B.Ask. Entry is the
// fill-side quote: BUY at B.Ask, SELL at B.Bid.
public sealed class TrailingStopEngine
{
    // A close click that did not get confirmed is retried on this cadence.
    private const int CloseRetryMs = 500;

    private long _nextClusterId = 1;

    public Position? Current { get; private set; }
    public bool IsFlat => Current is null;

    // Re-anchor the held position's entry to the broker's actual fill price (known
    // a few ticks after open) and remember the broker ticket for targeted closes.
    public bool ApplyOpenFill(long clusterId, ulong ticket, double fillPrice, int point)
    {
        if (Current is { } pos && pos.ClusterId == clusterId)
        {
            pos.Ticket = ticket;
            pos.EntryPoint = GapCalculator.ToPoints(fillPrice, point);

            // The stop references trail the best price since open, which was
            // seeded from the decide price — widen them to cover the real fill.
            if (pos.EntryPoint > pos.HighestPoint)
            {
                pos.HighestPoint = pos.EntryPoint;
            }

            if (pos.EntryPoint < pos.LowestPoint)
            {
                pos.LowestPoint = pos.EntryPoint;
            }

            return true;
        }

        return false;
    }

    // The close click succeeded (or the position was observed closed externally):
    // the engine is flat again.
    public bool ConfirmClose(long clusterId)
    {
        if (Current is { } pos && pos.ClusterId == clusterId)
        {
            Current = null;
            return true;
        }

        return false;
    }

    // The open click failed: no broker position exists, so drop the just-created
    // engine position instead of holding a phantom.
    public bool AbortOpen(long clusterId)
    {
        if (Current is { } pos && pos.ClusterId == clusterId && !pos.CloseRequested)
        {
            Current = null;
            return true;
        }

        return false;
    }

    public List<DryRunEvent> Step(double bidB, double askB, SignalSide? signal, long nowMs, StrategyConfig config)
    {
        var events = new List<DryRunEvent>();

        if (Current is null)
        {
            if (signal is { } side)
            {
                Open(side, bidB, askB, nowMs, config, events);
            }

            return events;
        }

        if (Current.CloseRequested)
        {
            RetryClose(bidB, askB, nowMs, config, events);
            return events;
        }

        TryClose(bidB, askB, nowMs, config, events);
        return events;
    }

    private void Open(SignalSide side, double bidB, double askB, long nowMs, StrategyConfig config, List<DryRunEvent> events)
    {
        var entryPrice = side == SignalSide.BuyB ? askB : bidB;
        var entryPoint = GapCalculator.ToPoints(entryPrice, config.Point);
        var clusterId = _nextClusterId++;

        Current = new Position(side, entryPoint, nowMs) { ClusterId = clusterId };

        events.Add(new DryRunEvent(
            Decision: "live open",
            Reason: "confirmed signal",
            State: BotState.Holding,
            TimestampMs: nowMs,
            ClusterId: clusterId,
            Side: ToDrySide(side),
            OrderCount: 1,
            OpenPrice: entryPrice));
    }

    private void TryClose(double bidB, double askB, long nowMs, StrategyConfig config, List<DryRunEvent> events)
    {
        var pos = Current!;
        var closePrice = pos.Side == SignalSide.BuyB ? bidB : askB;
        var current = GapCalculator.ToPoints(closePrice, config.Point);

        string? reason = null;

        if (pos.Side == SignalSide.BuyB)
        {
            if (current > pos.HighestPoint)
            {
                pos.HighestPoint = current;
            }

            if (!pos.TrailingActive && current >= pos.EntryPoint + config.TrailingStartPoint)
            {
                pos.TrailingActive = true;
            }

            var stop = pos.TrailingActive
                ? pos.HighestPoint - config.TrailingStepPoint
                : pos.HighestPoint - config.StopLossPoint;
            if (current <= stop)
            {
                reason = pos.TrailingActive ? "trailing stop" : "stop loss";
            }
        }
        else
        {
            if (current < pos.LowestPoint)
            {
                pos.LowestPoint = current;
            }

            if (!pos.TrailingActive && current <= pos.EntryPoint - config.TrailingStartPoint)
            {
                pos.TrailingActive = true;
            }

            var stop = pos.TrailingActive
                ? pos.LowestPoint + config.TrailingStepPoint
                : pos.LowestPoint + config.StopLossPoint;
            if (current >= stop)
            {
                reason = pos.TrailingActive ? "trailing stop" : "stop loss";
            }
        }

        if (reason is null)
        {
            return;
        }

        pos.CloseRequested = true;
        pos.CloseReason = reason;
        pos.LastCloseAttemptMs = nowMs;
        events.Add(BuildCloseEvent(pos, reason, closePrice, current, nowMs));
    }

    private void RetryClose(double bidB, double askB, long nowMs, StrategyConfig config, List<DryRunEvent> events)
    {
        var pos = Current!;
        if (nowMs - pos.LastCloseAttemptMs < CloseRetryMs)
        {
            return;
        }

        pos.LastCloseAttemptMs = nowMs;
        var closePrice = pos.Side == SignalSide.BuyB ? bidB : askB;
        var current = GapCalculator.ToPoints(closePrice, config.Point);
        events.Add(BuildCloseEvent(pos, $"{pos.CloseReason} (retry)", closePrice, current, nowMs));
    }

    private static DryRunEvent BuildCloseEvent(Position pos, string reason, double closePrice, int current, long nowMs)
    {
        var pnlPoints = pos.Side == SignalSide.BuyB
            ? current - pos.EntryPoint
            : pos.EntryPoint - current;

        return new DryRunEvent(
            Decision: "live close",
            Reason: reason,
            State: BotState.Idle,
            TimestampMs: nowMs,
            ClusterId: pos.ClusterId,
            Side: ToDrySide(pos.Side),
            OrderCount: 1,
            ClosePrice: closePrice,
            PnlRaw: pnlPoints,
            HoldMs: nowMs - pos.OpenedAtMs,
            TrailingActive: pos.TrailingActive);
    }

    private static DryRunSide ToDrySide(SignalSide side) =>
        side == SignalSide.BuyB ? DryRunSide.BuyB : DryRunSide.SellB;
}
