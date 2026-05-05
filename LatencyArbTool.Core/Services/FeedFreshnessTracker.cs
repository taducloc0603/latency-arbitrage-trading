namespace LatencyArbTool.Core.Services;

/// <summary>
/// Tracks how long a feed has been "silent" - i.e., how long since the EA last wrote
/// a new tick (ea_ms changed). This is different from raw latency (now - ea_ms), which
/// keeps growing during quiet periods even when the feed is healthy.
///
/// Silence resets to 0 every time ea_ms changes (a new tick arrives), regardless of
/// the absolute latency value. A growing silence indicates the feed has stopped
/// producing ticks (broker disconnect, EA hung), not just a quiet market.
/// </summary>
public sealed class FeedFreshnessTracker
{
    private long? _lastEaTickCountMs;
    private long _lastChangeAtTickCountMs;

    /// <summary>
    /// Observe the current EA tick count and current monotonic clock. Returns the
    /// number of ms elapsed since the EA tick count last changed (silence duration).
    /// </summary>
    public long Observe(long eaTickCountMs, long nowTickCountMs)
    {
        if (_lastEaTickCountMs is null || eaTickCountMs != _lastEaTickCountMs.Value)
        {
            _lastEaTickCountMs = eaTickCountMs;
            _lastChangeAtTickCountMs = nowTickCountMs;
        }

        return nowTickCountMs - _lastChangeAtTickCountMs;
    }

    public void Reset()
    {
        _lastEaTickCountMs = null;
        _lastChangeAtTickCountMs = 0;
    }
}
