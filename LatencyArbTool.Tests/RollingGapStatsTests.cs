using LatencyArbTool.Core.Services;

namespace LatencyArbTool.Tests;

public sealed class RollingGapStatsTests
{
    [Fact]
    public void GetThresholds_UsesScaledFallbacksDuringWarmup()
    {
        var stats = new RollingGapStats();
        stats.Add(1, -40, 20, 2);

        var thresholds = stats.GetThresholds();

        Assert.True(thresholds.IsWarmup);
        Assert.Equal(StrategyDefaults.FixedOpenBuyFallback, thresholds.OpenBuy);
        Assert.Equal(StrategyDefaults.FixedOpenSellFallback, thresholds.OpenSell);
        Assert.Equal(StrategyDefaults.CloseBuyRevertFallback, thresholds.CloseBuyRevert);
        Assert.Equal(StrategyDefaults.CloseSellRevertFallback, thresholds.CloseSellRevert);
    }

    [Fact]
    public void GetThresholds_ComputesARangeFromMidA()
    {
        var stats = new RollingGapStats();
        stats.Add(0, -10, 10, 1, midA: 4675.20);
        stats.Add(10_000, -10, 10, 1, midA: 4675.80);
        stats.Add(50_000, -10, 10, 1, midA: 4675.10);

        var thresholds = stats.GetThresholds();

        Assert.Equal(70, thresholds.ARangePoints);
    }

    [Fact]
    public void GetThresholds_ARangeIgnoresSamplesOutsideWindow()
    {
        var stats = new RollingGapStats();
        stats.Add(0, -10, 10, 1, midA: 4670.00);
        stats.Add(StrategyDefaults.AVolWindowMs + 1_000, -10, 10, 1, midA: 4675.00);
        stats.Add(StrategyDefaults.AVolWindowMs + 2_000, -10, 10, 1, midA: 4675.20);

        var thresholds = stats.GetThresholds();

        Assert.Equal(20, thresholds.ARangePoints);
    }

    [Fact]
    public void GetThresholds_SwitchesToDynamicAfterWarmup_WhenMoreExtremeThanFallback()
    {
        var stats = new RollingGapStats();
        for (var i = 0; i < StrategyDefaults.WarmupMinSamples; i++)
        {
            stats.Add(i, i % 2 == 0 ? -100 : -50, i % 2 == 0 ? 100 : 50, 1);
        }

        var thresholds = stats.GetThresholds();

        Assert.False(thresholds.IsWarmup);
        Assert.Equal(StrategyDefaults.WarmupMinSamples, thresholds.SampleCount);
        Assert.True(thresholds.OpenBuy < StrategyDefaults.FixedOpenBuyFallback);
        Assert.True(thresholds.OpenSell > StrategyDefaults.FixedOpenSellFallback);
    }

    [Fact]
    public void GetThresholds_ClampsToFallbackWhenDynamicTooLenient()
    {
        var stats = new RollingGapStats();
        for (var i = 0; i < StrategyDefaults.WarmupMinSamples; i++)
        {
            stats.Add(i, i % 2 == 0 ? -10 : -20, i % 2 == 0 ? 10 : 20, 1);
        }

        var thresholds = stats.GetThresholds();

        Assert.False(thresholds.IsWarmup);
        Assert.Equal(StrategyDefaults.FixedOpenBuyFallback, thresholds.OpenBuy);
        Assert.Equal(StrategyDefaults.FixedOpenSellFallback, thresholds.OpenSell);
    }
}

