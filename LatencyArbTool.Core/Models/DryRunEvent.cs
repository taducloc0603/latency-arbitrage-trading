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
    string ShadowBlockReasons = "",
    double PeakBidB = 0,
    double TroughAskB = 0,
    bool TrailingActive = false,
    // Close-only: the peak/trough the stop trailed (Max/Min) and the stop level
    // that fired (Max - SL, or Max - step once trailing). For clear SL logging.
    double StopRefPrice = 0,
    double StopLevelPrice = 0);
