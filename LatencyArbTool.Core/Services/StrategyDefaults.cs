namespace LatencyArbTool.Core.Services;

public static class StrategyDefaults
{
    public const int PointMultiplier = 100;

    public const int MedianWindowMinutes = 5;
    public const double KStd = 2.0;

    public const int WarmupMinSamples = 100;
    public const int FixedOpenBuyFallback = -75;
    public const int FixedOpenSellFallback = 65;

    public const int ConfirmMs = 200;
    public const int ReCheckMs = 100;
    public const double StabilityRatio = 0.4;
    public const int StackCooldownMs = 1000;
    public const int MinHoldMs = 1500;
    public const int MaxHoldMs = 90000;
    public const int MaxStack = 1;

    public const int CloseBuyRevertFallback = 0;
    public const int CloseSellRevertFallback = 0;
    public const double AReversalUsd = 1.20;

    // Silence threshold: ms since last new tick (ea_ms change). Larger = more lenient
    // for sparse feeds. Different from raw latency which grows during quiet markets.
    public const int FeedAStaleMs = 10000;
    public const int FeedBStaleMs = 3000;
    public const double SpreadBMaxMultiplier = 5.0;

    public const int AVolWindowMs = 60_000;
    public const int MinAVolPoints = 20;

    public const int LotBandOneMaxGap = 60;
    public const int LotBandTwoMaxGap = 70;

    // Adaptive slippage compensation: bias OpenBuy/OpenSell by the rolling-median
    // gap drift between decide-click and broker-fill. Same code works on both a
    // VN VPS (large drift) and a London VPS (small drift) — threshold auto-shifts.
    public const int SlippageWindowFills = 30;
    public const int SlippageWarmupMinFills = 5;
    public const int SlippageDefaultBiasPts = 30;

    // Velocity filter: skip "stuck" gaps that are about to revert. Measured as
    // points/sec slope of gap over a short window. Favorable direction is more
    // negative for buy candidates, more positive for sell candidates.
    public const int VelocityWindowMs = 200;
    public const double MinFavorableVelocityPtsPerSec = 30.0;

    // Cool-down between close and the next open click — prevents the bot from
    // hammering open while MT5 is still processing the previous close, which
    // produced ~50% rejection rate ("B trade already open") in earlier runs.
    public const int CooldownAfterCloseMs = 750;

    // Profit target / loss cap close based on broker's live profit (USD).
    // Independent of gap-revert / A-reversal, so a slip-aided winner is locked
    // in fast and a slip-driven loser is cut before it deepens.
    public const double ProfitTargetUsd = 30.0;
    public const double LossCapUsd = 50.0;
}
