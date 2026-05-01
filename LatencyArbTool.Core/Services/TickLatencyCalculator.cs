namespace LatencyArbTool.Core.Services;

public static class TickLatencyCalculator
{
    private const long MinUnixMs = 946684800000; // 2000-01-01T00:00:00Z
    private const long MinUnixSeconds = MinUnixMs / 1000;
    private const long MaxFutureSkewMs = 60000;
    private const long MaxFutureSkewSeconds = MaxFutureSkewMs / 1000;

    public static long? TryGetLatencyMs(long nowMs, long timestampMs, long tickTimeMsc)
    {
        return ResolveLatencyMs(nowMs, timestampMs, tickTimeMsc).LatencyMs;
    }

    public static TickLatencyResult ResolveLatencyMs(long nowMs, long timestampMs, long tickTimeMsc)
    {
        var normalizedTimestampMs = NormalizeUnixTimestampMs(nowMs, timestampMs);
        if (normalizedTimestampMs is not null)
        {
            return new TickLatencyResult(Math.Max(0, nowMs - normalizedTimestampMs.Value), TickLatencySource.TimestampMs);
        }

        var normalizedTickTimeMsc = NormalizeUnixTimestampMs(nowMs, tickTimeMsc);
        if (normalizedTickTimeMsc is not null)
        {
            return new TickLatencyResult(Math.Max(0, nowMs - normalizedTickTimeMsc.Value), TickLatencySource.TickTimeMsc);
        }

        return new TickLatencyResult(null, TickLatencySource.Null);
    }

    public static long? GetEffectiveTimestampMs(long nowMs, long timestampMs, long tickTimeMsc)
    {
        var normalizedTimestampMs = NormalizeUnixTimestampMs(nowMs, timestampMs);
        if (normalizedTimestampMs is not null)
        {
            return normalizedTimestampMs;
        }

        var normalizedTickTimeMsc = NormalizeUnixTimestampMs(nowMs, tickTimeMsc);
        if (normalizedTickTimeMsc is not null)
        {
            return normalizedTickTimeMsc;
        }

        return null;
    }

    public static bool IsValidUnixMs(long nowMs, long value)
    {
        return NormalizeUnixTimestampMs(nowMs, value) is not null;
    }

    public static long? NormalizeUnixTimestampMs(long nowMs, long value)
    {
        if (value >= MinUnixMs && value <= nowMs + MaxFutureSkewMs)
        {
            return value;
        }

        var nowSeconds = nowMs / 1000;
        if (value >= MinUnixSeconds && value <= nowSeconds + MaxFutureSkewSeconds)
        {
            return value * 1000;
        }

        return null;
    }
}

public sealed record TickLatencyResult(long? LatencyMs, TickLatencySource Source);

public enum TickLatencySource
{
    TimestampMs,
    TickTimeMsc,
    Null
}
