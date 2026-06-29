using System.Globalization;
using System.IO;
using System.Text;
using LatencyArbTool.Core.Models;

namespace LatencyArbTool.Core.Services;

// Logs only meaningful events (open / close). Per-tick logging was dropped — it
// produced ~40 rows/sec with no consumer after the stats/simulation code was removed.
public sealed class CsvLogger : IDisposable
{
    private readonly StreamWriter _events;

    public CsvLogger(string logsDirectory)
    {
        Directory.CreateDirectory(logsDirectory);

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        _events = Create(Path.Combine(logsDirectory, $"events_{stamp}.csv"));
        _events.WriteLine("timestamp,clusterId,decision,reason,side,entryPoint,openPrice,closePrice,pnlPoints,gapAtOpen,holdMs,trailingActive");
    }

    // gapAtOpen / entryPoint describe the position context (for a close row they are
    // the values captured when it was opened).
    public void LogEvent(DryRunEvent e, int gapAtOpen, int entryPoint)
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
            e.TrailingActive));
    }

    public void Flush() => _events.Flush();

    public void Dispose() => _events.Dispose();

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
