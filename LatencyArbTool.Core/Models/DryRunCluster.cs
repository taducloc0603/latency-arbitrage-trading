namespace LatencyArbTool.Core.Models;

public sealed class DryRunCluster
{
    public DryRunCluster(
        long clusterId,
        DryRunSide side,
        long openedAtMs,
        double initialPeakAskA,
        double initialTroughBidA)
    {
        ClusterId = clusterId;
        Side = side;
        OpenedAtMs = openedAtMs;
        PeakAskA = initialPeakAskA;
        TroughBidA = initialTroughBidA;
    }

    public long ClusterId { get; }
    public DryRunSide Side { get; }
    public long OpenedAtMs { get; }
    public long LastActionAtMs { get; set; }
    public long? ClosedAtMs { get; set; }
    public double PeakAskA { get; set; }
    public double TroughBidA { get; set; }
    public double FloatingPnlRaw { get; set; }
    public double RealizedPnlRaw { get; set; }
    public string CloseReason { get; set; } = string.Empty;
    public List<DryRunOrder> Orders { get; } = [];
}

