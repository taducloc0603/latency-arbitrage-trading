namespace LatencyArbTool.Core.Models;

public sealed class DryRunCluster
{
    public DryRunCluster(
        long clusterId,
        DryRunSide side,
        long openedAtMs,
        double initialPeakAskA,
        double initialTroughBidA,
        double initialBidB,
        double initialAskB)
    {
        ClusterId = clusterId;
        Side = side;
        OpenedAtMs = openedAtMs;
        PeakAskA = initialPeakAskA;
        TroughBidA = initialTroughBidA;
        PeakBidB = initialBidB;
        TroughAskB = initialAskB;
    }

    public long ClusterId { get; }
    public DryRunSide Side { get; }
    public long OpenedAtMs { get; }
    public long LastActionAtMs { get; set; }
    public long? ClosedAtMs { get; set; }
    public double PeakAskA { get; set; }
    public double TroughBidA { get; set; }
    // Trailing stop on B price: PeakBidB tracks the best exit price for a BUY
    // (highest B.Bid since open); TroughAskB tracks it for a SELL (lowest B.Ask).
    // TrailingActive flips true once gap has reverted AND broker profit cleared
    // the activation threshold — from that point the close trigger is a B-side
    // retrace rather than the gap-revert / A-reversal logic.
    public double PeakBidB { get; set; }
    public double TroughAskB { get; set; }
    public bool TrailingActive { get; set; }
    public double FloatingPnlRaw { get; set; }
    public double RealizedPnlRaw { get; set; }
    public string CloseReason { get; set; } = string.Empty;
    public List<DryRunOrder> Orders { get; } = [];
}
