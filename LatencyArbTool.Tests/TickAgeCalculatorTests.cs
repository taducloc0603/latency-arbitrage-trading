using LatencyArbTool.Core.Models;
using LatencyArbTool.Core.Services;

namespace LatencyArbTool.Tests;

public sealed class TickAgeCalculatorTests
{
    private const long NowMs = 1_777_397_957_338;

    [Fact]
    public void TryGetAgeMs_UsesTimestampMsWhenValid()
    {
        var result = TickAgeCalculator.ResolveLatencyMs(NowMs, NowMs - 123, NowMs - 999);

        Assert.Equal(123, result.AgeMs);
        Assert.Equal(TickAgeSource.TimestampMs, result.Source);
    }

    [Fact]
    public void TryGetAgeMs_FallsBackToTickTimeMsc()
    {
        var result = TickAgeCalculator.ResolveLatencyMs(NowMs, 0, NowMs - 456);

        Assert.Equal(456, result.AgeMs);
        Assert.Equal(TickAgeSource.TickTimeMsc, result.Source);
    }

    [Fact]
    public void TryGetAgeMs_ReturnsNullWhenBothTimestampsAreInvalid()
    {
        var result = TickAgeCalculator.ResolveLatencyMs(NowMs, 0, 1);

        Assert.Null(result.AgeMs);
        Assert.Equal(TickAgeSource.Null, result.Source);
    }

    [Fact]
    public void TryGetAgeMs_RejectsTimestampTooFarInFuture()
    {
        var age = TickAgeCalculator.TryGetAgeMs(NowMs, NowMs + 60001, 0);

        Assert.Null(age);
    }

    [Fact]
    public void TryGetAgeMs_RejectsTimestampBeforeYear2000()
    {
        var age = TickAgeCalculator.TryGetAgeMs(NowMs, 946684799999, 0);

        Assert.Null(age);
    }

    [Fact]
    public void MarketSnapshot_ReportsUnknownAgeForInvalidTimestamp()
    {
        var tick = new TickRecord(1, 0, 100, 101, 1, 0, "XAUUSD");
        var snapshot = new MarketSnapshot(tick, tick, NowMs, -50, 30);

        Assert.Null(snapshot.FeedAAgeMs);
        Assert.Equal(TickAgeSource.Null, snapshot.FeedAAge.Source);
        Assert.True(snapshot.FeedAIsStale);
    }
}
