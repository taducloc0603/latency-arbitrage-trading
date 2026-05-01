namespace LatencyArbTool.Core.Models;

public sealed record TickRecord(
    int Version,
    long TimestampMs,
    double Bid,
    double Ask,
    double Spread,
    long TickTimeMsc,
    string Symbol);

