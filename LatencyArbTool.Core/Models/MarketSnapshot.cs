namespace LatencyArbTool.Core.Models;

public sealed record MarketSnapshot(
    TickRecord A,
    TickRecord B,
    long NowMs,
    int GapBuy,
    int GapSell)
{
    public long FeedAAgeMs => Math.Max(0, NowMs - A.TimestampMs);
    public long FeedBAgeMs => Math.Max(0, NowMs - B.TimestampMs);
}

