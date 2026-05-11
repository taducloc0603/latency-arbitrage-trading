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
    int ARangePoints = int.MaxValue,
    double GapBuyVelocityPtsPerSec = 0,
    double GapSellVelocityPtsPerSec = 0,
    // Signed change of A mid / B mid over the velocity window (points = USD*100).
    // Used by LeadFollowSignalEngine to verify A actually led the move that
    // produced the gap — without this check, a B-led gap looks identical to an
    // A-led gap and the bot ends up on the wrong side.
    int MidAChangePtsInWindow = 0,
    int MidBChangePtsInWindow = 0);
