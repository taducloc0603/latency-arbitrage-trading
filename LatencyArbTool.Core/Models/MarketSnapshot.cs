using LatencyArbTool.Core.Services;

namespace LatencyArbTool.Core.Models;

public sealed record MarketSnapshot(
    TickRecord A,
    TickRecord B,
    long NowMs,
    int GapBuy,
    int GapSell,
    long NowTickCountMs)
{
    public TickLatencyResult FeedALatency => TickLatencyCalculator.ResolveLatencyMs(NowMs, NowTickCountMs, A.EaTickCountMs, A.TickTimeMsc);
    public TickLatencyResult FeedBLatency => TickLatencyCalculator.ResolveLatencyMs(NowMs, NowTickCountMs, B.EaTickCountMs, B.TickTimeMsc);
    public long? FeedALatencyMs => FeedALatency.LatencyMs;
    public long? FeedBLatencyMs => FeedBLatency.LatencyMs;
    public bool HasValidFeedALatency => FeedALatencyMs is not null;
    public bool HasValidFeedBLatency => FeedBLatencyMs is not null;
    public bool FeedAIsStale => FeedALatencyMs is null || FeedALatencyMs > StrategyDefaults.FeedAStaleMs;
    public bool FeedBIsStale => FeedBLatencyMs is null || FeedBLatencyMs > StrategyDefaults.FeedBStaleMs;
}
