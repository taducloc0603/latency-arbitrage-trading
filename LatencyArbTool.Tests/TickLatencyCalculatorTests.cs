using LatencyArbTool.Core.Models;
using LatencyArbTool.Core.Services;

namespace LatencyArbTool.Tests;

public sealed class TickLatencyCalculatorTests
{
    private const long NowUnixMs = 1_777_397_957_338;
    private const long NowTickCountMs = 10_000_000;

    [Fact]
    public void TryGetLatencyMs_UsesEaTickCountWhenValid()
    {
        var result = TickLatencyCalculator.ResolveLatencyMs(NowUnixMs, NowTickCountMs, NowTickCountMs - 35, NowUnixMs - 999);

        Assert.Equal(35, result.LatencyMs);
        Assert.Equal(TickLatencySource.EaTickCount, result.Source);
    }

    [Fact]
    public void TryGetLatencyMs_FallsBackWhenEaTickCountIsFuture()
    {
        var result = TickLatencyCalculator.ResolveLatencyMs(NowUnixMs, NowTickCountMs, NowTickCountMs + 1, NowUnixMs - 456);

        Assert.Equal(456, result.LatencyMs);
        Assert.Equal(TickLatencySource.TickTimeMscFallback, result.Source);
    }

    [Fact]
    public void TryGetLatencyMs_FallsBackWhenEaTickCountLatencyIsTooLarge()
    {
        var result = TickLatencyCalculator.ResolveLatencyMs(
            NowUnixMs,
            NowTickCountMs,
            NowTickCountMs - TickLatencyCalculator.MaxReasonableLatencyMs - 1,
            NowUnixMs - 1338);

        Assert.Equal(1338, result.LatencyMs);
        Assert.Equal(TickLatencySource.TickTimeMscFallback, result.Source);
    }

    [Fact]
    public void TryGetLatencyMs_FallsBackWhenEaTickCountIsMissing()
    {
        var result = TickLatencyCalculator.ResolveLatencyMs(NowUnixMs, NowTickCountMs, 0, NowUnixMs - 789);

        Assert.Equal(789, result.LatencyMs);
        Assert.Equal(TickLatencySource.TickTimeMscFallback, result.Source);
    }

    [Fact]
    public void TryGetLatencyMs_ReturnsNullWhenTickTimeMscIsFuture()
    {
        var result = TickLatencyCalculator.ResolveLatencyMs(NowUnixMs, NowTickCountMs, 0, NowUnixMs + 1);

        Assert.Null(result.LatencyMs);
        Assert.Equal(TickLatencySource.Null, result.Source);
    }

    [Fact]
    public void TryGetLatencyMs_ReturnsNullWhenBothSourcesAreInvalid()
    {
        var result = TickLatencyCalculator.ResolveLatencyMs(NowUnixMs, NowTickCountMs, NowTickCountMs + 1, 1);

        Assert.Null(result.LatencyMs);
        Assert.Equal(TickLatencySource.Null, result.Source);
    }

    [Fact]
    public void MarketSnapshot_ReportsUnknownLatencyForInvalidSources()
    {
        var tick = new TickRecord(1, 0, 100, 101, 1, 0, "XAUUSD");
        var snapshot = new MarketSnapshot(tick, tick, NowUnixMs, -50, 30, NowTickCountMs);

        Assert.Null(snapshot.FeedALatencyMs);
        Assert.Equal(TickLatencySource.Null, snapshot.FeedALatency.Source);
        Assert.True(snapshot.FeedAIsStale);
    }
}
