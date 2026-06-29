using System.Globalization;
using System.IO;
using System.Text;
using LatencyArbTool.Core.Models;

namespace LatencyArbTool.Core.Services;

public sealed class CsvLogger : IDisposable
{
    private readonly StreamWriter _ticks;
    private readonly StreamWriter _events;

    public CsvLogger(string logsDirectory)
    {
        Directory.CreateDirectory(logsDirectory);

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        _ticks = Create(Path.Combine(logsDirectory, $"ticks_{stamp}.csv"));
        _events = Create(Path.Combine(logsDirectory, $"events_{stamp}.csv"));

        _ticks.WriteLine("timestamp,bidA,askA,bidB,askB,gapBuy,gapSell");
        _events.WriteLine("timestamp,clusterId,decision,reason,side,openPrice,closePrice,pnlPoints,holdMs,trailingActive");
    }

    public void LogTick(long nowMs, TickRecord a, TickRecord b, int gapBuy, int gapSell)
    {
        _ticks.WriteLine(string.Join(',',
            nowMs,
            F(a.Bid),
            F(a.Ask),
            F(b.Bid),
            F(b.Ask),
            gapBuy,
            gapSell));
    }

    public void LogEvent(DryRunEvent e)
    {
        _events.WriteLine(string.Join(',',
            e.TimestampMs,
            e.ClusterId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Escape(e.Decision),
            Escape(e.Reason),
            e.Side?.ToString() ?? string.Empty,
            F(e.OpenPrice),
            F(e.ClosePrice),
            F(e.PnlRaw),
            e.HoldMs,
            e.TrailingActive));
    }

    public void Flush()
    {
        _ticks.Flush();
        _events.Flush();
    }

    public void Dispose()
    {
        _ticks.Dispose();
        _events.Dispose();
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
