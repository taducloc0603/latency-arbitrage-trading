namespace LatencyArbTool.Core.Services;

public static class TickLatencyCalculator
{
    private const long MinUnixMs = 946684800000; // 2000-01-01T00:00:00Z
    public const long MaxReasonableLatencyMs = 86_400_000;

    public static long? TryGetLatencyMs(long nowUnixMs, long nowTickCountMs, long eaTickCountMs, long tickTimeMsc)
    {
        return ResolveLatencyMs(nowUnixMs, nowTickCountMs, eaTickCountMs, tickTimeMsc).LatencyMs;
    }

    public static TickLatencyResult ResolveLatencyMs(long nowUnixMs, long nowTickCountMs, long eaTickCountMs, long tickTimeMsc)
    {
        if (eaTickCountMs > 0)
        {
            var tickCountLatencyMs = nowTickCountMs - eaTickCountMs;
            if (IsReasonableLatency(tickCountLatencyMs))
            {
                return new TickLatencyResult(tickCountLatencyMs, TickLatencySource.EaTickCount);
            }
        }

        var timestampLatencyMs = TryGetUnixLatencyMs(nowUnixMs, tickTimeMsc);
        if (timestampLatencyMs is not null)
        {
            return new TickLatencyResult(timestampLatencyMs.Value, TickLatencySource.TickTimeMscFallback);
        }

        return new TickLatencyResult(null, TickLatencySource.Null);
    }

    private static long? TryGetUnixLatencyMs(long nowUnixMs, long tickTimeMsc)
    {
        if (tickTimeMsc < MinUnixMs)
        {
            return null;
        }

        var latencyMs = nowUnixMs - tickTimeMsc;
        return IsReasonableLatency(latencyMs) ? latencyMs : null;
    }

    private static bool IsReasonableLatency(long latencyMs)
    {
        return latencyMs >= 0 && latencyMs <= MaxReasonableLatencyMs;
    }
}

public sealed record TickLatencyResult(long? LatencyMs, TickLatencySource Source);

public enum TickLatencySource
{
    EaTickCount,
    TickTimeMscFallback,
    Null
}
