using System.Globalization;
using System.IO;
using System.Text;
using LatencyArbTool.Core.Models;

namespace LatencyArbTool.Core.Services;

// Logs open/close decisions (events_*.csv) and broker fill confirmations with
// slippage (fills_*.csv). Per-tick logging was dropped — no consumer.
public sealed class CsvLogger : IDisposable
{
    private readonly StreamWriter _events;
    private readonly StreamWriter _fills;
    private readonly StreamWriter _snapshot;

    public CsvLogger(string logsDirectory)
    {
        Directory.CreateDirectory(logsDirectory);

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        _events = Create(Path.Combine(logsDirectory, $"events_{stamp}.csv"));
        _fills = Create(Path.Combine(logsDirectory, $"fills_{stamp}.csv"));
        _snapshot = Create(Path.Combine(logsDirectory, $"snapshot_{stamp}.csv"));

        _events.WriteLine("timestamp,clusterId,decision,reason,side,entryPoint,openPrice,closePrice,pnlPoints,gapAtOpen,holdMs,trailingActive,windowN,windowMin,windowMax,windowFirst,windowLast,windowAvg,windowDurMs,windowGaps");
        _fills.WriteLine("kind,ticket,clusterId,side,decideTimeMs,fillTimeMs,latencyMs,decideGap,fillObservedGap,decidePrice,fillPrice,slippagePrice,realizedUsd,commission");
        _snapshot.WriteLine("timestamp,aBid,aAsk,bBid,bAsk,spreadA,spreadB,gapBuy,gapSell,winState,winDurMs,winMin,winMax,winN");
    }

    // Periodic (~1s) market + gap + signal-window snapshot for analysis.
    public void LogSnapshot(long ts, TickRecord a, TickRecord b, int gapBuy, int gapSell,
        string winState, long winDurMs, int winMin, int winMax, int winN)
    {
        _snapshot.WriteLine(string.Join(',',
            ts,
            F(a.Bid), F(a.Ask), F(b.Bid), F(b.Ask),
            F(a.Spread), F(b.Spread),
            gapBuy, gapSell,
            winState, winDurMs, winMin, winMax, winN));
    }

    // window is populated only for "live open" rows (the confirm-window snapshot).
    public void LogEvent(DryRunEvent e, int gapAtOpen, int entryPoint, SignalWindow? window = null)
    {
        _events.WriteLine(string.Join(',',
            e.TimestampMs,
            e.ClusterId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Escape(e.Decision),
            Escape(e.Reason),
            e.Side?.ToString() ?? string.Empty,
            entryPoint,
            F(e.OpenPrice),
            F(e.ClosePrice),
            F(e.PnlRaw),
            gapAtOpen,
            e.HoldMs,
            e.TrailingActive,
            window?.Count.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            window is null ? string.Empty : window.Min.ToString(CultureInfo.InvariantCulture),
            window is null ? string.Empty : window.Max.ToString(CultureInfo.InvariantCulture),
            window is null ? string.Empty : window.First.ToString(CultureInfo.InvariantCulture),
            window is null ? string.Empty : window.Last.ToString(CultureInfo.InvariantCulture),
            window is null ? string.Empty : F(window.Avg),
            window?.DurationMs.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            window is null ? string.Empty : Escape(string.Join(';', window.Gaps))));
    }

    public void LogFill(FillEvent f)
    {
        _fills.WriteLine(string.Join(',',
            f.IsClose ? "close" : "open",
            f.Ticket,
            f.ClusterId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            f.Side,
            f.DecideTimeMs,
            f.FillTimeMs,
            f.SlippageMs,
            f.DecideGap,
            f.FillObservedGap,
            F(f.DecidePrice),
            F(f.FillPrice),
            F(f.SlippagePrice),
            F(f.RealizedUsd),
            F(f.Commission)));
    }

    public void Flush()
    {
        _events.Flush();
        _fills.Flush();
        _snapshot.Flush();
    }

    public void Dispose()
    {
        _events.Dispose();
        _fills.Dispose();
        _snapshot.Dispose();
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
