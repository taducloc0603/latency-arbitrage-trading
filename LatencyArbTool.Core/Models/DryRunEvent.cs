namespace LatencyArbTool.Core.Models;

public sealed record DryRunEvent(
    string Decision,
    string Reason,
    BotState State,
    long TimestampMs,
    long? ClusterId = null,
    DryRunSide? Side = null,
    int OrderCount = 0,
    double OpenPrice = 0,
    double ClosePrice = 0,
    double Lot = 0,
    double PnlRaw = 0,
    long HoldMs = 0,
    string ShadowBlockReasons = "");
