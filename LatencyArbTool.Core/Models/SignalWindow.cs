namespace LatencyArbTool.Core.Models;

// Snapshot of the gaps observed during the confirm window of the signal that
// fired — captured for recheck/logging. For a SELL the gaps are negative.
public sealed record SignalWindow(
    int Count,
    long DurationMs,
    int Min,
    int Max,
    int First,
    int Last,
    int Sum,
    int Z,            // sustain floor (OpenConfirmGapPts)
    int X,            // trigger (OpenPts)
    IReadOnlyList<int> Gaps)
{
    public double Avg => Count == 0 ? 0 : (double)Sum / Count;
}
