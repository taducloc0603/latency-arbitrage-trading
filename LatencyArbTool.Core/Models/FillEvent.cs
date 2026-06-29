namespace LatencyArbTool.Core.Models;

// Emitted when FillTracker observes a broker confirmation (new ticket appearing
// in the trades map for opens; ticket disappearing + matched history record for
// closes). Paired with the click context so slippage / latency are directly
// readable. Logging/recheck only — not fed back into the strategy.
public sealed record FillEvent(
    bool IsClose,
    ulong Ticket,
    DryRunSide Side,
    long? ClusterId,
    long DecideTimeMs,
    long FillTimeMs,
    long SlippageMs,
    int DecideGap,
    int FillObservedGap,
    double DecidePrice,
    double FillPrice,
    double SlippagePrice,
    double RealizedUsd = 0,   // close only (broker POSITION_PROFIT)
    double Commission = 0);   // close only
