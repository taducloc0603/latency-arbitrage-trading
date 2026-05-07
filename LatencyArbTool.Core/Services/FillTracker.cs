using LatencyArbTool.Core.Models;

namespace LatencyArbTool.Core.Services;

// Pairs each click the bot fires with the broker's eventual fill so we can
// measure slippage. Opens are matched by FIFO on side (the bot's MaxStack=1
// means the queue is usually 0 or 1 deep). Closes are matched by ticket — the
// bot captures the ticket at click time, then we wait for it to disappear from
// the trades map and look up the close price/time in the history map.
public sealed class FillTracker
{
    private readonly Queue<ClickContext> _pendingOpens = new();
    private readonly Dictionary<ulong, ClickContext> _pendingCloses = new();
    private readonly Queue<int> _recentDriftMagnitudes = new();
    private HashSet<ulong> _knownTickets = new();
    private bool _initialized;

    // Number of open fills observed since (re)start. Caller uses this to decide
    // whether MedianDriftMagnitude has enough samples to be trusted.
    public int OpenFillCount { get; private set; }

    // Median |decideGap - fillObservedGap| across the last
    // StrategyDefaults.SlippageWindowFills opens. 0 if no fills yet.
    public int MedianDriftMagnitude
    {
        get
        {
            if (_recentDriftMagnitudes.Count == 0)
            {
                return 0;
            }
            var sorted = _recentDriftMagnitudes.ToArray();
            Array.Sort(sorted);
            return sorted[sorted.Length / 2];
        }
    }

    public void RecordOpenClick(ClickContext context)
    {
        _pendingOpens.Enqueue(context);
    }

    public void RecordCloseClick(ulong ticket, ClickContext context)
    {
        _pendingCloses[ticket] = context;
    }

    public IReadOnlyList<FillEvent> Observe(
        TradeReadResult trades,
        HistoryReadResult history,
        MarketSnapshot snapshot)
    {
        var events = new List<FillEvent>();
        if (!trades.Success)
        {
            return events;
        }

        var current = new HashSet<ulong>();
        foreach (var t in trades.Trades)
        {
            current.Add(t.Ticket);
        }

        // First successful read seeds the known set; we don't try to retro-match
        // tickets that already existed when the bot started.
        if (!_initialized)
        {
            _knownTickets = current;
            _initialized = true;
            return events;
        }

        foreach (var trade in trades.Trades)
        {
            if (_knownTickets.Contains(trade.Ticket))
            {
                continue;
            }

            var match = DequeueMatchingOpen(trade.Side);
            if (match is null)
            {
                continue;
            }

            var fill = BuildOpenFill(match, trade, snapshot);
            events.Add(fill);
            RecordDrift(match.DecideGap - fill.FillObservedGap);
            OpenFillCount++;
        }

        foreach (var ticket in _knownTickets)
        {
            if (current.Contains(ticket))
            {
                continue;
            }

            if (!_pendingCloses.Remove(ticket, out var ctx))
            {
                continue;
            }

            var historyRecord = FindHistory(history, ticket);
            events.Add(BuildCloseFill(ctx, ticket, historyRecord, snapshot));
        }

        _knownTickets = current;
        return events;
    }

    public void Reset()
    {
        _pendingOpens.Clear();
        _pendingCloses.Clear();
        _recentDriftMagnitudes.Clear();
        _knownTickets = new HashSet<ulong>();
        _initialized = false;
        OpenFillCount = 0;
    }

    private void RecordDrift(int signedDrift)
    {
        var magnitude = Math.Abs(signedDrift);
        _recentDriftMagnitudes.Enqueue(magnitude);
        while (_recentDriftMagnitudes.Count > StrategyDefaults.SlippageWindowFills)
        {
            _recentDriftMagnitudes.Dequeue();
        }
    }

    private ClickContext? DequeueMatchingOpen(TradeSide side)
    {
        var expected = side == TradeSide.Buy ? DryRunSide.BuyB : DryRunSide.SellB;
        if (_pendingOpens.Count == 0)
        {
            return null;
        }

        var buffer = new List<ClickContext>(_pendingOpens.Count);
        ClickContext? match = null;
        while (_pendingOpens.Count > 0)
        {
            var ctx = _pendingOpens.Dequeue();
            if (match is null && ctx.Side == expected)
            {
                match = ctx;
                continue;
            }
            buffer.Add(ctx);
        }
        foreach (var ctx in buffer)
        {
            _pendingOpens.Enqueue(ctx);
        }
        return match;
    }

    private static HistoryRecord? FindHistory(HistoryReadResult history, ulong ticket)
    {
        if (!history.Success)
        {
            return null;
        }
        foreach (var rec in history.History)
        {
            if (rec.Ticket == ticket)
            {
                return rec;
            }
        }
        return null;
    }

    private static FillEvent BuildOpenFill(ClickContext ctx, TradeRecord trade, MarketSnapshot snapshot)
    {
        var fillTimeMs = (long)trade.TimeMsc;
        var fillObservedGap = ctx.Side == DryRunSide.BuyB ? snapshot.GapBuy : snapshot.GapSell;
        return new FillEvent(
            IsClose: false,
            Ticket: trade.Ticket,
            Side: ctx.Side,
            ClusterId: ctx.ClusterId,
            DecideTimeMs: ctx.DecideTimeMs,
            FillTimeMs: fillTimeMs,
            SlippageMs: fillTimeMs - ctx.DecideTimeMs,
            DecideGap: ctx.DecideGap,
            FillObservedGap: fillObservedGap,
            DecidePrice: ctx.DecidePrice,
            FillPrice: trade.Price,
            SlippagePrice: trade.Price - ctx.DecidePrice);
    }

    private static FillEvent BuildCloseFill(ClickContext ctx, ulong ticket, HistoryRecord? historyRec, MarketSnapshot snapshot)
    {
        long fillTimeMs = historyRec is not null ? (long)historyRec.CloseTimeMsc : 0;
        double fillPrice = historyRec is not null ? historyRec.ClosePrice : 0;
        var fillObservedGap = ctx.Side == DryRunSide.BuyB ? snapshot.GapBuy : snapshot.GapSell;
        return new FillEvent(
            IsClose: true,
            Ticket: ticket,
            Side: ctx.Side,
            ClusterId: ctx.ClusterId,
            DecideTimeMs: ctx.DecideTimeMs,
            FillTimeMs: fillTimeMs,
            SlippageMs: fillTimeMs > 0 ? fillTimeMs - ctx.DecideTimeMs : 0,
            DecideGap: ctx.DecideGap,
            FillObservedGap: fillObservedGap,
            DecidePrice: ctx.DecidePrice,
            FillPrice: fillPrice,
            SlippagePrice: fillPrice > 0 ? fillPrice - ctx.DecidePrice : 0);
    }
}
