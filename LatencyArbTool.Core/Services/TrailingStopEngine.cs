using LatencyArbTool.Core.Models;

namespace LatencyArbTool.Core.Services;

// Manages the single live position on B. Opening happens ONLY when a signal is
// supplied and we are flat. Closing is signal-independent: every tick we check
// Stop-Loss (while trailing not yet active) then Trailing-Stop (once active).
//
// Prices are converted to points with config.Point. "Current" is the realistic
// close-side quote: BUY closes at B.Bid, SELL closes at B.Ask. Entry is the
// fill-side quote: BUY at B.Ask, SELL at B.Bid.
public sealed class TrailingStopEngine
{
    private long _nextClusterId = 1;

    public Position? Current { get; private set; }
    public bool IsFlat => Current is null;

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
            if (!pos.TrailingActive)
            {
                if (current <= pos.EntryPoint - config.StopLossPoint)
                {
                    reason = "stop loss";
                }
                else if (current >= pos.EntryPoint + config.TrailingStartPoint)
                {
                    pos.TrailingActive = true;
                    pos.HighestPoint = current;
                }
            }

            if (reason is null && pos.TrailingActive)
            {
                if (current > pos.HighestPoint)
                {
                    pos.HighestPoint = current;
                }

                if (current <= pos.HighestPoint - config.TrailingStepPoint)
                {
                    reason = "trailing stop";
                }
            }
        }
        else
        {
            if (!pos.TrailingActive)
            {
                if (current >= pos.EntryPoint + config.StopLossPoint)
                {
                    reason = "stop loss";
                }
                else if (current <= pos.EntryPoint - config.TrailingStartPoint)
                {
                    pos.TrailingActive = true;
                    pos.LowestPoint = current;
                }
            }

            if (reason is null && pos.TrailingActive)
            {
                if (current < pos.LowestPoint)
                {
                    pos.LowestPoint = current;
                }

                if (current >= pos.LowestPoint + config.TrailingStepPoint)
                {
                    reason = "trailing stop";
                }
            }
        }

        if (reason is null)
        {
            return;
        }

        var pnlPoints = pos.Side == SignalSide.BuyB
            ? current - pos.EntryPoint
            : pos.EntryPoint - current;

        events.Add(new DryRunEvent(
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
            TrailingActive: pos.TrailingActive));

        Current = null;
    }

    private static DryRunSide ToDrySide(SignalSide side) =>
        side == SignalSide.BuyB ? DryRunSide.BuyB : DryRunSide.SellB;
}
