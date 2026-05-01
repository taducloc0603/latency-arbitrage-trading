namespace LatencyArbTool.Core.Models;

public sealed record DryRunOrder(
    int OrderNumber,
    DryRunSide Side,
    double OpenPrice,
    double Lot,
    long OpenedAtMs);

