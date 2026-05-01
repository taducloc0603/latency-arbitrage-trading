using LatencyArbTool.Core.Services;

namespace LatencyArbTool.Core.Models;

public sealed record MarketSnapshot(
    TickRecord A,
    TickRecord B,
    long NowMs,
    int GapBuy,
    int GapSell)
{
    public TickLatencyResult FeedALatency => TickLatencyCalculator.ResolveLatencyMs(NowMs, A.TimestampMs, A.TickTimeMsc);
    public TickLatencyResult FeedBLatency => TickLatencyCalculator.ResolveLatencyMs(NowMs, B.TimestampMs, B.TickTimeMsc);
    public long? FeedALatencyMs => FeedALatency.LatencyMs;
    public long? FeedBLatencyMs => FeedBLatency.LatencyMs;
    public bool HasValidFeedATimestamp => FeedALatencyMs is not null;
    public bool HasValidFeedBTimestamp => FeedBLatencyMs is not null;
    public bool FeedAIsStale => FeedALatencyMs is null || FeedALatencyMs > StrategyDefaults.FeedAStaleMs;
    public bool FeedBIsStale => FeedBLatencyMs is null || FeedBLatencyMs > StrategyDefaults.FeedBStaleMs;
}
