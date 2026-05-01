using LatencyArbTool.Core.Services;

namespace LatencyArbTool.Core.Models;

public sealed record MarketSnapshot(
    TickRecord A,
    TickRecord B,
    long NowMs,
    int GapBuy,
    int GapSell)
{
    public TickAgeResult FeedAAge => TickAgeCalculator.ResolveLatencyMs(NowMs, A.TimestampMs, A.TickTimeMsc);
    public TickAgeResult FeedBAge => TickAgeCalculator.ResolveLatencyMs(NowMs, B.TimestampMs, B.TickTimeMsc);
    public long? FeedAAgeMs => FeedAAge.AgeMs;
    public long? FeedBAgeMs => FeedBAge.AgeMs;
    public bool HasValidFeedATimestamp => FeedAAgeMs is not null;
    public bool HasValidFeedBTimestamp => FeedBAgeMs is not null;
    public bool FeedAIsStale => FeedAAgeMs is null || FeedAAgeMs > StrategyDefaults.FeedAStaleMs;
    public bool FeedBIsStale => FeedBAgeMs is null || FeedBAgeMs > StrategyDefaults.FeedBStaleMs;
}
