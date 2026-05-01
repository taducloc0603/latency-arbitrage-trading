using LatencyArbTool.Core.Models;

namespace LatencyArbTool.Core.Services;

public sealed class RollingGapStats
{
    private readonly long _windowMs;
    private readonly Queue<GapSample> _samples = new();

    public RollingGapStats(int windowMinutes = StrategyDefaults.MedianWindowMinutes)
    {
        _windowMs = TimeSpan.FromMinutes(windowMinutes).Ticks / TimeSpan.TicksPerMillisecond;
    }

    public int Count => _samples.Count;

    public void Add(long timestampMs, int gapBuy, int gapSell, double spreadB)
    {
        _samples.Enqueue(new GapSample(timestampMs, gapBuy, gapSell, spreadB));
        Trim(timestampMs);
    }

    public GapThresholds GetThresholds()
    {
        if (_samples.Count < StrategyDefaults.WarmupMinSamples)
        {
            return new GapThresholds(
                StrategyDefaults.FixedOpenBuyFallback,
                StrategyDefaults.FixedOpenSellFallback,
                StrategyDefaults.CloseBuyRevertFallback,
                StrategyDefaults.CloseSellRevertFallback,
                0,
                0,
                0,
                0,
                Median(_samples.Select(s => s.SpreadB)),
                _samples.Count,
                IsWarmup: true);
        }

        var buys = _samples.Select(s => (double)s.GapBuy).ToArray();
        var sells = _samples.Select(s => (double)s.GapSell).ToArray();
        var medianBuy = Median(buys);
        var medianSell = Median(sells);
        var stdBuy = StdDev(buys, medianBuy);
        var stdSell = StdDev(sells, medianSell);

        return new GapThresholds(
            (int)Math.Round(medianBuy - StrategyDefaults.KStd * stdBuy, MidpointRounding.AwayFromZero),
            (int)Math.Round(medianSell + StrategyDefaults.KStd * stdSell, MidpointRounding.AwayFromZero),
            StrategyDefaults.CloseBuyRevertFallback,
            StrategyDefaults.CloseSellRevertFallback,
            medianBuy,
            medianSell,
            stdBuy,
            stdSell,
            Median(_samples.Select(s => s.SpreadB)),
            _samples.Count,
            IsWarmup: false);
    }

    private void Trim(long nowMs)
    {
        while (_samples.Count > 0 && nowMs - _samples.Peek().TimestampMs > _windowMs)
        {
            _samples.Dequeue();
        }
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0)
        {
            return 0;
        }

        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[middle - 1] + sorted[middle]) / 2.0
            : sorted[middle];
    }

    private static double StdDev(IReadOnlyCollection<double> values, double center)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var variance = values.Average(value => Math.Pow(value - center, 2));
        return Math.Sqrt(variance);
    }

    private sealed record GapSample(long TimestampMs, int GapBuy, int GapSell, double SpreadB);
}

