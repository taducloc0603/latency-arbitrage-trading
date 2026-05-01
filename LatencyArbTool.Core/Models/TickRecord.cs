namespace LatencyArbTool.Core.Models;

public sealed record TickRecord(
    int Count,
    // Monotonic EA local clock from Windows GetTickCount64. This is not Unix epoch time.
    long EaTickCountMs,
    double Bid,
    double Ask,
    double Spread,
    // MT tick timestamp in Unix epoch milliseconds when supplied by the feed; fallback-only for latency.
    long TickTimeMsc,
    string Symbol);
