namespace LatencyArbTool.Core.Services;

public static class StrategyDefaults
{
    public const int PointMultiplier = 100;

    public const int MedianWindowMinutes = 5;
    public const double KStd = 3.0;

    public const int WarmupMinSamples = 500;
    public const int FixedOpenBuyFallback = -50;
    public const int FixedOpenSellFallback = 35;

    public const int ConfirmMs = 350;
    public const int ReCheckMs = 150;
    public const double StabilityRatio = 0.40;
    public const int StackCooldownMs = 1000;
    public const int MinHoldMs = 3000;
    public const int MaxHoldMs = 90000;
    public const int MaxStack = 1;

    public const int CloseBuyRevertFallback = 0;
    public const int CloseSellRevertFallback = 0;
    public const double AReversalUsd = 0.40;

    // Silence threshold: ms since last new tick (ea_ms change). Larger = more lenient
    // for sparse feeds. Different from raw latency which grows during quiet markets.
    public const int FeedAStaleMs = 10000;
    public const int FeedBStaleMs = 15000;
    public const double SpreadBMaxMultiplier = 2.5;

    public const int AVolWindowMs = 60_000;
    public const int MinAVolPoints = 50;

    public const int LotBandOneMaxGap = 60;
    public const int LotBandTwoMaxGap = 70;
}
