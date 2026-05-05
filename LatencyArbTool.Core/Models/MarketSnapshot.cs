using LatencyArbTool.Core.Services;

namespace LatencyArbTool.Core.Models;

public sealed record MarketSnapshot(
    TickRecord A,
    TickRecord B,
    long NowMs,
    int GapBuy,
    int GapSell,
    long NowTickCountMs,
    long FeedASilenceMs = 0,
    long FeedBSilenceMs = 0)
{
    public TickLatencyResult FeedALatency => TickLatencyCalculator.ResolveLatencyMs(NowMs, NowTickCountMs, A.EaTickCountMs, A.TickTimeMsc);
    public TickLatencyResult FeedBLatency => TickLatencyCalculator.ResolveLatencyMs(NowMs, NowTickCountMs, B.EaTickCountMs, B.TickTimeMsc);
    public long? FeedALatencyMs => FeedALatency.LatencyMs;
    public long? FeedBLatencyMs => FeedBLatency.LatencyMs;
    public bool HasValidFeedALatency => FeedALatencyMs is not null;
    public bool HasValidFeedBLatency => FeedBLatencyMs is not null;

    // Stale = feed went silent for too long (no new tick from EA). Different from raw
    // latency: in a quiet market, latency grows but silence resets when any tick arrives.
    // Silence > threshold indicates feed actually stopped producing ticks.
    public bool FeedAIsStale => !HasValidFeedALatency || FeedASilenceMs > StrategyDefaults.FeedAStaleMs;
    public bool FeedBIsStale => !HasValidFeedBLatency || FeedBSilenceMs > StrategyDefaults.FeedBStaleMs;
}
