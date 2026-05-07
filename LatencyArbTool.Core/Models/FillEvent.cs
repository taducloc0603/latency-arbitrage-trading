namespace LatencyArbTool.Core.Models;

// Emitted when FillTracker observes a broker confirmation (new ticket appearing
// in the trades map for opens, ticket disappearing + matched history record
// for closes). The timestamps and prices are paired with the click context so
// downstream analysis can directly read slippage in the CSV.
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
    double SlippagePrice);
