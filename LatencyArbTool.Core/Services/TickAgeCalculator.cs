namespace LatencyArbTool.Core.Services;

public static class TickAgeCalculator
{
    private const long MinUnixMs = 946684800000; // 2000-01-01T00:00:00Z
    private const long MaxFutureSkewMs = 60000;

    public static long? TryGetAgeMs(long nowMs, long timestampMs, long tickTimeMsc)
    {
        return ResolveLatencyMs(nowMs, timestampMs, tickTimeMsc).AgeMs;
    }

    public static TickAgeResult ResolveLatencyMs(long nowMs, long timestampMs, long tickTimeMsc)
    {
        if (IsValidUnixMs(nowMs, timestampMs))
        {
            return new TickAgeResult(Math.Max(0, nowMs - timestampMs), TickAgeSource.TimestampMs);
        }

        if (IsValidUnixMs(nowMs, tickTimeMsc))
        {
            return new TickAgeResult(Math.Max(0, nowMs - tickTimeMsc), TickAgeSource.TickTimeMsc);
        }

        return new TickAgeResult(null, TickAgeSource.Null);
    }

    public static long? GetEffectiveTimestampMs(long nowMs, long timestampMs, long tickTimeMsc)
    {
        if (IsValidUnixMs(nowMs, timestampMs))
        {
            return timestampMs;
        }

        if (IsValidUnixMs(nowMs, tickTimeMsc))
        {
            return tickTimeMsc;
        }

        return null;
    }

    public static bool IsValidUnixMs(long nowMs, long value)
    {
        return value >= MinUnixMs && value <= nowMs + MaxFutureSkewMs;
    }
}

public sealed record TickAgeResult(long? AgeMs, TickAgeSource Source);

public enum TickAgeSource
{
    TimestampMs,
    TickTimeMsc,
    Null
}
