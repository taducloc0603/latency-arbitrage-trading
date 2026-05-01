using LatencyArbTool.Core.Models;
using LatencyArbTool.Core.Services;

namespace LatencyArbTool.Tests;

public sealed class TickLatencyCalculatorTests
{
    private const long NowMs = 1_777_397_957_338;

    [Fact]
    public void TryGetLatencyMs_UsesTimestampMsWhenValid()
    {
        var result = TickLatencyCalculator.ResolveLatencyMs(NowMs, NowMs - 123, NowMs - 999);

        Assert.Equal(123, result.LatencyMs);
        Assert.Equal(TickLatencySource.TimestampMs, result.Source);
    }

    [Fact]
    public void TryGetLatencyMs_FallsBackToTickTimeMsc()
    {
        var result = TickLatencyCalculator.ResolveLatencyMs(NowMs, 0, NowMs - 456);

        Assert.Equal(456, result.LatencyMs);
        Assert.Equal(TickLatencySource.TickTimeMsc, result.Source);
    }

    [Fact]
    public void TryGetLatencyMs_NormalizesUnixSeconds()
    {
        var result = TickLatencyCalculator.ResolveLatencyMs(NowMs, (NowMs - 1338) / 1000, 0);

        Assert.Equal(1338, result.LatencyMs);
        Assert.Equal(TickLatencySource.TimestampMs, result.Source);
    }

    [Fact]
    public void TryGetLatencyMs_ReturnsNullWhenBothTimestampsAreInvalid()
    {
        var result = TickLatencyCalculator.ResolveLatencyMs(NowMs, 0, 1);

        Assert.Null(result.LatencyMs);
        Assert.Equal(TickLatencySource.Null, result.Source);
    }

    [Fact]
    public void TryGetLatencyMs_RejectsTimestampTooFarInFuture()
    {
        var latency = TickLatencyCalculator.TryGetLatencyMs(NowMs, NowMs + 60001, 0);

        Assert.Null(latency);
    }

    [Fact]
    public void TryGetLatencyMs_RejectsTimestampBeforeYear2000()
    {
        var latency = TickLatencyCalculator.TryGetLatencyMs(NowMs, 946684799999, 0);

        Assert.Null(latency);
    }

    [Fact]
    public void MarketSnapshot_ReportsUnknownLatencyForInvalidTimestamp()
    {
        var tick = new TickRecord(1, 0, 100, 101, 1, 0, "XAUUSD");
        var snapshot = new MarketSnapshot(tick, tick, NowMs, -50, 30);

        Assert.Null(snapshot.FeedALatencyMs);
        Assert.Equal(TickLatencySource.Null, snapshot.FeedALatency.Source);
        Assert.True(snapshot.FeedAIsStale);
    }
}
