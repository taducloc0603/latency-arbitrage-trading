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
        Assert.Equal(-50, thresholds.OpenBuy);
        Assert.Equal(30, thresholds.OpenSell);
        Assert.Equal(-15, thresholds.CloseBuyRevert);
        Assert.Equal(20, thresholds.CloseSellRevert);
    }

    [Fact]
    public void GetThresholds_SwitchesToDynamicAfterWarmup()
    {
        var stats = new RollingGapStats();
        for (var i = 0; i < StrategyDefaults.WarmupMinSamples; i++)
        {
            stats.Add(i, i % 2 == 0 ? -10 : -20, i % 2 == 0 ? 10 : 20, 1);
        }

        var thresholds = stats.GetThresholds();

        Assert.False(thresholds.IsWarmup);
        Assert.Equal(StrategyDefaults.WarmupMinSamples, thresholds.SampleCount);
        Assert.NotEqual(StrategyDefaults.FixedOpenBuyFallback, thresholds.OpenBuy);
        Assert.NotEqual(StrategyDefaults.FixedOpenSellFallback, thresholds.OpenSell);
    }
}

