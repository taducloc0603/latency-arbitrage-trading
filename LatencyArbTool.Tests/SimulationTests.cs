using System.Globalization;
using LatencyArbTool.Core.Models;
using LatencyArbTool.Core.Services;
using Xunit.Abstractions;

namespace LatencyArbTool.Tests;

public sealed class SimulationTests
{
    private static readonly string? DataDir = ResolveDataDir();

    private readonly ITestOutputHelper _out;

    public SimulationTests(ITestOutputHelper output)
    {
        _out = output;
    }

    [Theory]
    [InlineData("20260504_002742_265")]
    [InlineData("20260504_034135_243")]
    [InlineData("20260504_120733_286")]
    [InlineData("20260504_114822_008")]
    [InlineData("20260505_023742_297")]
    [InlineData("20260505_072352_271")]
    public void Simulate(string runId)
    {
        if (DataDir is null)
        {
            _out.WriteLine($"Skipping {runId}: tick data directory not found near repo root");
            return;
        }

        var aPath = Path.Combine(DataDir, $"{runId}_tickA.csv");
        var bPath = Path.Combine(DataDir, $"{runId}_tickB.csv");
        if (!File.Exists(aPath) || !File.Exists(bPath))
        {
            _out.WriteLine($"Skipping {runId}: tick CSVs not present in {DataDir}");
            return;
        }

        var ticksA = LoadTicks(aPath);
        var ticksB = LoadTicks(bPath);

        var events = new List<(long ms, char side, TickRecord tick)>(ticksA.Count + ticksB.Count);
        foreach (var t in ticksA) events.Add((t.ms, 'A', t.tick));
        foreach (var t in ticksB) events.Add((t.ms, 'B', t.tick));
        events.Sort((x, y) => x.ms.CompareTo(y.ms));

        var stats = new RollingGapStats();
        var signalEngine = new LeadFollowSignalEngine();
        var clusterEngine = new DryRunClusterEngine();
        var feedAFreshness = new FeedFreshnessTracker();
        var feedBFreshness = new FeedFreshnessTracker();
        // Sim sees every tick (no polling skip) so seq increments by 1 each step.
        // SequenceTracker would still produce delta=1, satisfying the engine's
        // PollMissedTicks check.
        var feedASeq = new SequenceTracker();
        var feedBSeq = new SequenceTracker();
        var aSeqCounter = 0;
        var bSeqCounter = 0;

        TickRecord? curA = null;
        TickRecord? curB = null;

        var opens = new List<DryRunEvent>();
        var closes = new List<DryRunEvent>();
        var blocks = new Dictionary<string, int>();
        var openContexts = new List<OpenContext>();

        // Diagnostic tracking
        var bIntervals = new List<long>();
        long? lastBMs = null;
        // Rolling history of recent gap samples for trade context lookup
        var history = new LinkedList<GapSample>();
        const int historyWindowMs = 2000;

        foreach (var (ms, side, tick) in events)
        {
            if (side == 'A')
            {
                aSeqCounter++;
                curA = tick with { EaTickCountMs = ms, Count = aSeqCounter };
            }
            else
            {
                bSeqCounter++;
                curB = tick with { EaTickCountMs = ms, Count = bSeqCounter };
                if (lastBMs is not null) bIntervals.Add(ms - lastBMs.Value);
                lastBMs = ms;
            }

            if (curA is null || curB is null) continue;

            var (gapBuy, gapSell) = GapCalculator.Calculate(curA, curB);
            var feedASilenceMs = feedAFreshness.Observe(curA.EaTickCountMs, ms);
            var feedBSilenceMs = feedBFreshness.Observe(curB.EaTickCountMs, ms);
            var feedASeqDelta = feedASeq.ObserveDelta(curA.Count);
            var feedBSeqDelta = feedBSeq.ObserveDelta(curB.Count);
            var snapshot = new MarketSnapshot(curA, curB, ms, gapBuy, gapSell, ms, feedASilenceMs, feedBSilenceMs, feedASeqDelta, feedBSeqDelta);
            stats.Add(ms, gapBuy, gapSell, curB.Spread, (curA.Bid + curA.Ask) / 2.0);
            var thresholds = stats.GetThresholds();
            var signal = signalEngine.Evaluate(snapshot, thresholds);
            var stepEvents = clusterEngine.Step(snapshot, thresholds, signal);

            history.AddLast(new GapSample(ms, gapBuy, gapSell, curB.Spread));
            while (history.First is not null && ms - history.First.Value.Ms > historyWindowMs)
            {
                history.RemoveFirst();
            }

            foreach (var ev in stepEvents)
            {
                if (ev.Decision == "live open")
                {
                    opens.Add(ev);
                    openContexts.Add(BuildOpenContext(ev, ms, gapBuy, gapSell, history, thresholds));
                }
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
        _out.WriteLine($"Tick events: A={ticksA.Count}, B={ticksB.Count}, ratio A/B = {(double)ticksA.Count / Math.Max(1, ticksB.Count):F2}x");
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

        // B tick interval distribution
        if (bIntervals.Count > 0)
        {
            bIntervals.Sort();
            var med = bIntervals[bIntervals.Count / 2];
            var p95 = bIntervals[(int)(bIntervals.Count * 0.95)];
            var p99 = bIntervals[(int)(bIntervals.Count * 0.99)];
            var stallCount1s = bIntervals.Count(x => x > 1000);
            var stallCount2s = bIntervals.Count(x => x > 2000);
            var stallCount5s = bIntervals.Count(x => x > 5000);
            _out.WriteLine("");
            _out.WriteLine("B tick interval (ms):");
            _out.WriteLine($"  median={med}, p95={p95}, p99={p99}, max={bIntervals[^1]}");
            _out.WriteLine($"  count >1s: {stallCount1s}, >2s: {stallCount2s}, >5s: {stallCount5s}");
        }

        _out.WriteLine("");
        _out.WriteLine("Blocks by reason (top 10):");
        foreach (var kv in blocks.OrderByDescending(k => k.Value).Take(10))
        {
            _out.WriteLine($"  {kv.Key}: {kv.Value}");
        }

        _out.WriteLine("");
        _out.WriteLine("Trade detail (with pre-open context):");
        for (var i = 0; i < closes.Count; i++)
        {
            var c = closes[i];
            var o = i < opens.Count ? opens[i] : null;
            var ctx = i < openContexts.Count ? openContexts[i] : null;
            var side = c.Side?.ToString() ?? "?";
            var openPrice = o?.OpenPrice ?? 0;
            _out.WriteLine($"  #{i + 1} {side} open={openPrice:F2} close={c.ClosePrice:F2} hold={c.HoldMs}ms pnl={c.PnlRaw:+0.00;-0.00} reason={c.Reason}");
            if (ctx is not null)
            {
                _out.WriteLine($"     gap@open: buy={ctx.GapBuyAtOpen}, sell={ctx.GapSellAtOpen}");
                _out.WriteLine($"     thresholds: openBuy={ctx.OpenBuy}, openSell={ctx.OpenSell}");
                _out.WriteLine($"     1500ms window: peakBuy={ctx.PeakBuy}, peakSell={ctx.PeakSell}, troughBuy={ctx.TroughBuy}, troughSell={ctx.TroughSell}");
                _out.WriteLine($"     samples in window: {ctx.WindowSamples}, B updates in window: {ctx.BUpdatesInWindow}");
            }
        }
    }

    private static OpenContext BuildOpenContext(
        DryRunEvent openEvent,
        long openMs,
        int gapBuyAtOpen,
        int gapSellAtOpen,
        LinkedList<GapSample> history,
        GapThresholds thresholds)
    {
        const int contextWindowMs = 1500;
        var cutoff = openMs - contextWindowMs;
        int peakBuy = gapBuyAtOpen, troughBuy = gapBuyAtOpen;
        int peakSell = gapSellAtOpen, troughSell = gapSellAtOpen;
        int samples = 0;
        int bUpdates = 0;
        double? lastSpreadB = null;
        foreach (var s in history)
        {
            if (s.Ms < cutoff) continue;
            samples++;
            if (s.GapBuy < peakBuy) peakBuy = s.GapBuy;
            if (s.GapBuy > troughBuy) troughBuy = s.GapBuy;
            if (s.GapSell > peakSell) peakSell = s.GapSell;
            if (s.GapSell < troughSell) troughSell = s.GapSell;
            if (lastSpreadB is null || Math.Abs(s.SpreadB - lastSpreadB.Value) > 1e-9)
            {
                bUpdates++;
                lastSpreadB = s.SpreadB;
            }
        }
        return new OpenContext(
            openEvent.ClusterId ?? 0,
            gapBuyAtOpen,
            gapSellAtOpen,
            thresholds.OpenBuy,
            thresholds.OpenSell,
            peakBuy,
            peakSell,
            troughBuy,
            troughSell,
            samples,
            bUpdates);
    }

    private static string? ResolveDataDir()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "data", "tick");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
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

    /// <summary>
    /// Polling-emulated simulation: instead of processing every tick CSV row
    /// (which assumes perfect information), iterate time in `pollIntervalMs`
    /// steps and at each step take the latest tick available. This matches what
    /// the real bot sees: ticks that arrived between two polls are coalesced
    /// (only the latest is read), with the EA-side seq counter advancing across
    /// the gap so SequenceTracker reports the miss.
    ///
    /// Compares directly with `Simulate` (event-driven) to quantify the polling
    /// artifact's impact on engine behavior.
    /// </summary>
    [Theory]
    [InlineData("20260504_002742_265")]
    [InlineData("20260504_034135_243")]
    [InlineData("20260504_120733_286")]
    [InlineData("20260504_114822_008")]
    [InlineData("20260505_023742_297")]
    [InlineData("20260505_072352_271")]
    public void SimulatePolled(string runId)
    {
        const long pollIntervalMs = 25;
        if (DataDir is null)
        {
            _out.WriteLine($"Skipping {runId}: tick data directory not found near repo root");
            return;
        }

        var aPath = Path.Combine(DataDir, $"{runId}_tickA.csv");
        var bPath = Path.Combine(DataDir, $"{runId}_tickB.csv");
        if (!File.Exists(aPath) || !File.Exists(bPath))
        {
            _out.WriteLine($"Skipping {runId}: tick CSVs not present in {DataDir}");
            return;
        }

        var ticksA = LoadTicks(aPath);
        var ticksB = LoadTicks(bPath);
        if (ticksA.Count == 0 || ticksB.Count == 0)
        {
            _out.WriteLine($"Skipping {runId}: empty tick streams");
            return;
        }

        var startMs = Math.Min(ticksA[0].ms, ticksB[0].ms);
        var endMs = Math.Max(ticksA[^1].ms, ticksB[^1].ms);

        var stats = new RollingGapStats();
        var signalEngine = new LeadFollowSignalEngine();
        var clusterEngine = new DryRunClusterEngine();
        var feedAFreshness = new FeedFreshnessTracker();
        var feedBFreshness = new FeedFreshnessTracker();
        var feedASeq = new SequenceTracker();
        var feedBSeq = new SequenceTracker();

        var aIdx = 0;
        var bIdx = 0;
        var aSeqCounter = 0;
        var bSeqCounter = 0;

        var opens = new List<DryRunEvent>();
        var closes = new List<DryRunEvent>();
        var blocks = new Dictionary<string, int>();
        var pollsTotal = 0;
        var pollsWithMissedTicks = 0;
        var totalAMissed = 0L;
        var totalBMissed = 0L;

        for (var nowMs = startMs; nowMs <= endMs; nowMs += pollIntervalMs)
        {
            // Advance pointers — count every tick the EA produced since last poll.
            // The seq counter mirrors what the EA would have written.
            while (aIdx < ticksA.Count && ticksA[aIdx].ms <= nowMs)
            {
                aSeqCounter++;
                aIdx++;
            }
            while (bIdx < ticksB.Count && ticksB[bIdx].ms <= nowMs)
            {
                bSeqCounter++;
                bIdx++;
            }

            if (aIdx == 0 || bIdx == 0) continue;

            var aTick = ticksA[aIdx - 1].tick with { Count = aSeqCounter, EaTickCountMs = ticksA[aIdx - 1].ms };
            var bTick = ticksB[bIdx - 1].tick with { Count = bSeqCounter, EaTickCountMs = ticksB[bIdx - 1].ms };

            var (gapBuy, gapSell) = GapCalculator.Calculate(aTick, bTick);
            var feedASilenceMs = feedAFreshness.Observe(aTick.EaTickCountMs, nowMs);
            var feedBSilenceMs = feedBFreshness.Observe(bTick.EaTickCountMs, nowMs);
            var feedASeqDelta = feedASeq.ObserveDelta(aTick.Count);
            var feedBSeqDelta = feedBSeq.ObserveDelta(bTick.Count);
            var snapshot = new MarketSnapshot(aTick, bTick, nowMs, gapBuy, gapSell, nowMs, feedASilenceMs, feedBSilenceMs, feedASeqDelta, feedBSeqDelta);

            stats.Add(nowMs, gapBuy, gapSell, bTick.Spread, (aTick.Bid + aTick.Ask) / 2.0);
            var thresholds = stats.GetThresholds();
            var signal = signalEngine.Evaluate(snapshot, thresholds);
            var stepEvents = clusterEngine.Step(snapshot, thresholds, signal);

            pollsTotal++;
            if (snapshot.PollMissedTicks)
            {
                pollsWithMissedTicks++;
                if (feedASeqDelta > 1) totalAMissed += feedASeqDelta - 1;
                if (feedBSeqDelta > 1) totalBMissed += feedBSeqDelta - 1;
            }

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

        _out.WriteLine($"=== Run {runId} (polling-emulated, {pollIntervalMs}ms) ===");
        _out.WriteLine($"Tick events: A={ticksA.Count}, B={ticksB.Count}");
        _out.WriteLine($"Polls processed: {pollsTotal}, with missed ticks: {pollsWithMissedTicks} ({(pollsTotal > 0 ? 100.0 * pollsWithMissedTicks / pollsTotal : 0):F1}%)");
        _out.WriteLine($"Total A ticks missed: {totalAMissed} ({(ticksA.Count > 0 ? 100.0 * totalAMissed / ticksA.Count : 0):F1}% of CSV)");
        _out.WriteLine($"Total B ticks missed: {totalBMissed} ({(ticksB.Count > 0 ? 100.0 * totalBMissed / ticksB.Count : 0):F1}% of CSV)");
        _out.WriteLine($"Opens: {opens.Count}, Closes: {closes.Count}");
        _out.WriteLine($"Wins: {wins}, Losses: {losses}, Win rate: {(wins + losses > 0 ? 100.0 * wins / (wins + losses) : 0):F1}%");
        _out.WriteLine($"Total PnL (raw, sim units): {totalPnl:F2}");
        if (closes.Count > 0)
        {
            _out.WriteLine($"Avg win: {(wins > 0 ? closes.Where(c => c.PnlRaw > 0).Average(c => c.PnlRaw) : 0):F2}");
            _out.WriteLine($"Avg loss: {(losses > 0 ? closes.Where(c => c.PnlRaw < 0).Average(c => c.PnlRaw) : 0):F2}");
        }
        _out.WriteLine("Blocks by reason (top 10):");
        foreach (var kv in blocks.OrderByDescending(k => k.Value).Take(10))
        {
            _out.WriteLine($"  {kv.Key}: {kv.Value}");
        }
    }

    private sealed record GapSample(long Ms, int GapBuy, int GapSell, double SpreadB);

    private sealed record OpenContext(
        long ClusterId,
        int GapBuyAtOpen,
        int GapSellAtOpen,
        int OpenBuy,
        int OpenSell,
        int PeakBuy,
        int PeakSell,
        int TroughBuy,
        int TroughSell,
        int WindowSamples,
        int BUpdatesInWindow);
}
