using System.Globalization;
using System.IO;
using System.Text;
using LatencyArbTool.Core.Models;

namespace LatencyArbTool.Core.Services;

public sealed class CsvLogger : IDisposable
{
    private readonly StreamWriter _ticks;
    private readonly StreamWriter _decisions;
    private readonly StreamWriter _clusters;
    private readonly StreamWriter _signal;
    private readonly StreamWriter _fills;

    public CsvLogger(string logsDirectory)
    {
        Directory.CreateDirectory(logsDirectory);

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        _ticks = Create(Path.Combine(logsDirectory, $"ticks_{stamp}.csv"));
        _decisions = Create(Path.Combine(logsDirectory, $"decisions_{stamp}.csv"));
        _clusters = Create(Path.Combine(logsDirectory, $"clusters_{stamp}.csv"));
        _signal = Create(Path.Combine(logsDirectory, $"signal_{stamp}.csv"));
        _fills = Create(Path.Combine(logsDirectory, $"fills_{stamp}.csv"));

        _ticks.WriteLine("timestamp,bidA,askA,bidB,askB,spreadA,spreadB,latencyA,latencyASource,latencyB,latencyBSource,gapBuy,gapSell,openBuyThreshold,openSellThreshold,medianBuy,medianSell,stdBuy,stdSell,medianSpreadB,aRangePoints,sampleCount,isWarmup,feedASilenceMs,feedBSilenceMs,feedASeqDelta,feedBSeqDelta");
        _decisions.WriteLine("timestamp,state,decision,reason,gapBuy,gapSell,openBuyThreshold,openSellThreshold,medianBuy,medianSell,stdBuy,stdSell,medianSpreadB,spreadB,aRangePoints,feedASilenceMs,feedBSilenceMs,shadowBlockReasons");
        _clusters.WriteLine("clusterId,event,side,orderCount,openPrice,closePrice,lot,pnlRaw,holdMs,closeReason,shadowBlockReasons,peakBidB,troughAskB,trailingActive");
        _signal.WriteLine("timestamp,gapBuy,gapSell,openBuyThreshold,openSellThreshold,extremeSinceBuyMs,confirmReachedBuyMs,peakGapBuy,extremeSinceSellMs,confirmReachedSellMs,peakGapSell,signalReturned");
        _fills.WriteLine("kind,ticket,clusterId,side,decideTimeMs,fillTimeMs,slippageMs,decideGap,fillObservedGap,decidePrice,fillPrice,slippagePrice");
    }

    public void LogTick(MarketSnapshot snapshot, GapThresholds thresholds)
    {
        _ticks.WriteLine(string.Join(',',
            snapshot.NowMs,
            F(snapshot.A.Bid),
            F(snapshot.A.Ask),
            F(snapshot.B.Bid),
            F(snapshot.B.Ask),
            F(snapshot.A.Spread),
            F(snapshot.B.Spread),
            snapshot.FeedALatencyMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            snapshot.FeedALatency.Source,
            snapshot.FeedBLatencyMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            snapshot.FeedBLatency.Source,
            snapshot.GapBuy,
            snapshot.GapSell,
            thresholds.OpenBuy,
            thresholds.OpenSell,
            F(thresholds.MedianBuy),
            F(thresholds.MedianSell),
            F(thresholds.StdBuy),
            F(thresholds.StdSell),
            F(thresholds.MedianSpreadB),
            thresholds.ARangePoints == int.MaxValue ? string.Empty : thresholds.ARangePoints.ToString(CultureInfo.InvariantCulture),
            thresholds.SampleCount,
            thresholds.IsWarmup,
            snapshot.FeedASilenceMs,
            snapshot.FeedBSilenceMs,
            snapshot.FeedASeqDelta,
            snapshot.FeedBSeqDelta));
    }

    public void LogDecision(DryRunEvent dryRunEvent, MarketSnapshot snapshot, GapThresholds thresholds)
    {
        _decisions.WriteLine(string.Join(',',
            dryRunEvent.TimestampMs,
            dryRunEvent.State,
            Escape(dryRunEvent.Decision),
            Escape(dryRunEvent.Reason),
            snapshot.GapBuy,
            snapshot.GapSell,
            thresholds.OpenBuy,
            thresholds.OpenSell,
            F(thresholds.MedianBuy),
            F(thresholds.MedianSell),
            F(thresholds.StdBuy),
            F(thresholds.StdSell),
            F(thresholds.MedianSpreadB),
            F(snapshot.B.Spread),
            thresholds.ARangePoints == int.MaxValue ? string.Empty : thresholds.ARangePoints.ToString(CultureInfo.InvariantCulture),
            snapshot.FeedASilenceMs,
            snapshot.FeedBSilenceMs,
            Escape(dryRunEvent.ShadowBlockReasons)));

        if (dryRunEvent.Decision.StartsWith("live ", StringComparison.OrdinalIgnoreCase))
        {
            _clusters.WriteLine(string.Join(',',
                dryRunEvent.ClusterId,
                Escape(dryRunEvent.Decision),
                dryRunEvent.Side,
                dryRunEvent.OrderCount,
                F(dryRunEvent.OpenPrice),
                F(dryRunEvent.ClosePrice),
                F(dryRunEvent.Lot),
                F(dryRunEvent.PnlRaw),
                dryRunEvent.HoldMs,
                Escape(dryRunEvent.Reason),
                Escape(dryRunEvent.ShadowBlockReasons),
                F(dryRunEvent.PeakBidB),
                F(dryRunEvent.TroughAskB),
                dryRunEvent.TrailingActive));
        }
    }

    public void LogSignal(
        long timestampMs,
        MarketSnapshot snapshot,
        GapThresholds thresholds,
        LeadFollowSignalEngine engine,
        SignalSide? signal)
    {
        _signal.WriteLine(string.Join(',',
            timestampMs,
            snapshot.GapBuy,
            snapshot.GapSell,
            thresholds.OpenBuy,
            thresholds.OpenSell,
            engine.ExtremeSinceBuyMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            engine.ConfirmReachedAtBuyMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            engine.PeakGapBuy?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            engine.ExtremeSinceSellMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            engine.ConfirmReachedAtSellMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            engine.PeakGapSell?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            signal?.ToString() ?? string.Empty));
    }

    public void LogFill(FillEvent fill)
    {
        _fills.WriteLine(string.Join(',',
            fill.IsClose ? "close" : "open",
            fill.Ticket,
            fill.ClusterId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            fill.Side,
            fill.DecideTimeMs,
            fill.FillTimeMs,
            fill.SlippageMs,
            fill.DecideGap,
            fill.FillObservedGap,
            F(fill.DecidePrice),
            F(fill.FillPrice),
            F(fill.SlippagePrice)));
    }

    public void Flush()
    {
        _ticks.Flush();
        _decisions.Flush();
        _clusters.Flush();
        _signal.Flush();
        _fills.Flush();
    }

    public void Dispose()
    {
        _ticks.Dispose();
        _decisions.Dispose();
        _clusters.Dispose();
        _signal.Dispose();
        _fills.Dispose();
    }

    private static StreamWriter Create(string path)
    {
        return new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string F(double value)
    {
        return value.ToString("0.#####", CultureInfo.InvariantCulture);
    }

    private static string Escape(string? value)
    {
        value ??= string.Empty;
        return value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;
    }
}
