using System.Globalization;
using LatencyArbTool.Core.Models;
using LatencyArbTool.Core.Services;
using Xunit.Abstractions;

namespace LatencyArbTool.Tests;

public sealed class SimulationTests
{
    private const string DataDir = "/Users/admin/self/latency-arbitrage-trading/data/tick";

    private readonly ITestOutputHelper _out;

    public SimulationTests(ITestOutputHelper output)
    {
        _out = output;
    }

    [Theory]
    [InlineData("20260504_002742_265")]
    [InlineData("20260504_034135_243")]
    [InlineData("20260504_120733_286")]
    public void Simulate(string runId)
    {
        var aPath = Path.Combine(DataDir, $"{runId}_tickA.csv");
        var bPath = Path.Combine(DataDir, $"{runId}_tickB.csv");

        var ticksA = LoadTicks(aPath);
        var ticksB = LoadTicks(bPath);

        var events = new List<(long ms, char side, TickRecord tick)>(ticksA.Count + ticksB.Count);
        foreach (var t in ticksA) events.Add((t.ms, 'A', t.tick));
        foreach (var t in ticksB) events.Add((t.ms, 'B', t.tick));
        events.Sort((x, y) => x.ms.CompareTo(y.ms));

        var stats = new RollingGapStats();
        var signalEngine = new LeadFollowSignalEngine();
        var clusterEngine = new DryRunClusterEngine();

        TickRecord? curA = null;
        TickRecord? curB = null;

        var opens = new List<DryRunEvent>();
        var closes = new List<DryRunEvent>();
        var blocks = new Dictionary<string, int>();

        foreach (var (ms, side, tick) in events)
        {
            if (side == 'A') curA = tick with { EaTickCountMs = ms };
            else curB = tick with { EaTickCountMs = ms };

            if (curA is null || curB is null) continue;

            var (gapBuy, gapSell) = GapCalculator.Calculate(curA, curB);
            var snapshot = new MarketSnapshot(curA, curB, ms, gapBuy, gapSell, ms);
            stats.Add(ms, gapBuy, gapSell, curB.Spread, (curA.Bid + curA.Ask) / 2.0);
            var thresholds = stats.GetThresholds();
            var signal = signalEngine.Evaluate(snapshot, thresholds);
            var stepEvents = clusterEngine.Step(snapshot, thresholds, signal);

            foreach (var ev in stepEvents)
            {
                if (ev.Decision == "live open") opens.Add(ev);
                else if (ev.Decision == "live close") closes.Add(ev);
                else if (ev.Decision == "guard block")
                {
                    blocks.TryGetValue(ev.Reason, out var c);
                    blocks[ev.Reason] = c + 1;
                }
            }
        }

        var totalPnl = closes.Sum(c => c.PnlRaw);
        var wins = closes.Count(c => c.PnlRaw > 0);
        var losses = closes.Count(c => c.PnlRaw < 0);

        _out.WriteLine($"=== Run {runId} ===");
        _out.WriteLine($"Tick events: A={ticksA.Count}, B={ticksB.Count}");
        _out.WriteLine($"Opens: {opens.Count}, Closes: {closes.Count}");
        _out.WriteLine($"Wins: {wins}, Losses: {losses}, Win rate: {(wins + losses > 0 ? 100.0 * wins / (wins + losses) : 0):F1}%");
        _out.WriteLine($"Total PnL (raw, sim units): {totalPnl:F2}");
        if (closes.Count > 0)
        {
            _out.WriteLine($"Avg win: {(wins > 0 ? closes.Where(c => c.PnlRaw > 0).Average(c => c.PnlRaw) : 0):F2}");
            _out.WriteLine($"Avg loss: {(losses > 0 ? closes.Where(c => c.PnlRaw < 0).Average(c => c.PnlRaw) : 0):F2}");
            _out.WriteLine($"Largest win: {closes.Max(c => c.PnlRaw):F2}");
            _out.WriteLine($"Largest loss: {closes.Min(c => c.PnlRaw):F2}");
        }
        _out.WriteLine("Blocks by reason:");
        foreach (var kv in blocks.OrderByDescending(k => k.Value))
        {
            _out.WriteLine($"  {kv.Key}: {kv.Value}");
        }
        _out.WriteLine("");
        _out.WriteLine("Trade detail:");
        for (var i = 0; i < closes.Count; i++)
        {
            var c = closes[i];
            var o = i < opens.Count ? opens[i] : null;
            var side = c.Side?.ToString() ?? "?";
            var openPrice = o?.OpenPrice ?? 0;
            _out.WriteLine($"  #{i + 1} {side} open={openPrice:F2} close={c.ClosePrice:F2} hold={c.HoldMs}ms pnl={c.PnlRaw:+0.00;-0.00} reason={c.Reason}");
        }
    }

    private static List<(long ms, TickRecord tick)> LoadTicks(string path)
    {
        var lines = File.ReadAllLines(path);
        var result = new List<(long, TickRecord)>(lines.Length - 1);
        for (var i = 1; i < lines.Length; i++)
        {
            var cols = lines[i].Split(',');
            if (cols.Length < 6) continue;
            var ts = DateTimeOffset.Parse(cols[0], CultureInfo.InvariantCulture);
            var ms = ts.ToUnixTimeMilliseconds();
            var symbol = cols[1];
            var bid = double.Parse(cols[2], CultureInfo.InvariantCulture);
            var ask = double.Parse(cols[3], CultureInfo.InvariantCulture);
            var spread = double.Parse(cols[4], CultureInfo.InvariantCulture);
            var tickTimeMsc = long.Parse(cols[5], CultureInfo.InvariantCulture);
            result.Add((ms, new TickRecord(1, ms, bid, ask, spread, tickTimeMsc, symbol)));
        }
        return result;
    }
}
