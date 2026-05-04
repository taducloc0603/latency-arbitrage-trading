namespace LatencyArbTool.Core.Models;

public sealed record GapThresholds(
    int OpenBuy,
    int OpenSell,
    int CloseBuyRevert,
    int CloseSellRevert,
    double MedianBuy,
    double MedianSell,
    double StdBuy,
    double StdSell,
    double MedianSpreadB,
    int SampleCount,
    bool IsWarmup,
    int ARangePoints = int.MaxValue);

