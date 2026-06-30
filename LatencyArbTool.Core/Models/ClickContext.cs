namespace LatencyArbTool.Core.Models;

// Captured at the moment the bot decides to click open/close, so we can later
// compare against the broker's actual fill price/time and compute slippage.
public sealed record ClickContext(
    long DecideTimeMs,
    long DecideTickCount,
    int DecideGap,
    double DecidePrice,
    DryRunSide Side,
    long? ClusterId,
    string Decision);
