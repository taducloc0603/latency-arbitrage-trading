namespace LatencyArbTool.Core.Services;

/// <summary>
/// Tracks the EA-side sequence counter (TickRecord.Count) to detect missed ticks.
///
/// The EA increments a counter on each shared-memory write. The C# tool polls at a
/// fixed interval. If polling rate is slower than tick rate (busy market), the tool
/// reads only the latest tick — intermediate values are lost.
///
/// Tracker returns the delta between successive observations:
///   delta == 0  -> no new tick since last poll (fine)
///   delta == 1  -> exactly one new tick read (no miss)
///   delta &gt; 1   -> the EA wrote (delta - 1) ticks that the tool never saw
///   delta &lt; 0   -> EA restarted (counter wrapped or reset)
///
/// On miss the signal engine should reset its in-progress state because gap
/// continuity assumed by the engine no longer holds: between two extreme ticks the
/// tool may have skipped over an opposite-sign tick that should have invalidated
/// the signal.
/// </summary>
public sealed class SequenceTracker
{
    private int? _lastSeq;

    public int ObserveDelta(int currentSeq)
    {
        if (_lastSeq is null)
        {
            _lastSeq = currentSeq;
            return 0;
        }

        var delta = currentSeq - _lastSeq.Value;
        _lastSeq = currentSeq;
        return delta;
    }

    public void Reset()
    {
        _lastSeq = null;
    }
}
