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

    public CsvLogger(string baseDirectory)
    {
        var logsDirectory = Path.Combine(baseDirectory, "logs");
        Directory.CreateDirectory(logsDirectory);

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        _ticks = Create(Path.Combine(logsDirectory, $"ticks_{stamp}.csv"));
        _decisions = Create(Path.Combine(logsDirectory, $"decisions_{stamp}.csv"));
        _clusters = Create(Path.Combine(logsDirectory, $"clusters_{stamp}.csv"));

        _ticks.WriteLine("timestamp,bidA,askA,bidB,askB,spreadA,spreadB,latencyA,latencyASource,latencyB,latencyBSource,gapBuy,gapSell,openBuyThreshold,openSellThreshold,isWarmup");
        _decisions.WriteLine("timestamp,state,decision,reason,gapBuy,gapSell,openBuyThreshold,openSellThreshold");
        _clusters.WriteLine("clusterId,event,side,orderCount,openPrice,closePrice,lot,pnlRaw,holdMs,closeReason");
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
            thresholds.IsWarmup));
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
            thresholds.OpenSell));

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
                Escape(dryRunEvent.Reason)));
        }
    }

    public void Flush()
    {
        _ticks.Flush();
        _decisions.Flush();
        _clusters.Flush();
    }

    public void Dispose()
    {
        _ticks.Dispose();
        _decisions.Dispose();
        _clusters.Dispose();
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
