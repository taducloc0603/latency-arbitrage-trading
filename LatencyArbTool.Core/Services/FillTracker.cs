using LatencyArbTool.Core.Models;

namespace LatencyArbTool.Core.Services;

// Pairs each click the bot fires with the broker's eventual fill so we can
// measure slippage (price, gap drift, latency). Opens are matched FIFO by side
// (MaxStack=1 means the queue is 0 or 1 deep). Closes are matched by ticket: the
// bot captures the ticket at click time, then we wait for it to disappear from
// the trades map and read the close price / realized profit from the history map.
//
// The history map often lags the trades map by a few polls, so a disappeared
// ticket waits up to HistoryGraceMs for its history record before we give up
// and emit a zero-priced close. Pending open contexts expire after OpenTtlMs so
// a click that never produced a ticket cannot poison a much later match.
//
// Logging / recheck only — it does NOT feed back into thresholds.
public sealed class FillTracker
{
    private const long HistoryGraceMs = 5_000;
    private const long OpenTtlMs = 120_000;

    private readonly Queue<ClickContext> _pendingOpens = new();
    private readonly Dictionary<ulong, ClickContext> _pendingCloses = new();
    private readonly Dictionary<ulong, (ClickContext Context, long DeadlineMs)> _awaitingHistory = new();
    private HashSet<ulong> _knownTickets = new();
    private bool _initialized;

    public void RecordOpenClick(ClickContext context) => _pendingOpens.Enqueue(context);

    public void RecordCloseClick(ulong ticket, ClickContext context) => _pendingCloses[ticket] = context;

    public IReadOnlyList<FillEvent> Observe(TradeReadResult trades, HistoryReadResult history, int gapBuy, int gapSell, long nowMs)
    {
        var events = new List<FillEvent>();
        if (!trades.Success)
        {
            return events;
        }

        ExpireStaleOpens(nowMs);

        var current = new HashSet<ulong>();
        foreach (var t in trades.Trades)
        {
            current.Add(t.Ticket);
        }

        // First successful read seeds the known set; we don't retro-match tickets
        // that already existed when the bot started.
        if (!_initialized)
        {
            _knownTickets = current;
            _initialized = true;
            return events;
        }

        // New tickets -> open fills.
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

            events.Add(BuildOpenFill(match, trade, gapBuy, gapSell));
        }

        // Disappeared tickets -> close fills. The history record may not be
        // written yet; wait for it within a grace window before emitting zeros.
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

            _awaitingHistory[ticket] = (ctx, nowMs + HistoryGraceMs);
        }

        if (_awaitingHistory.Count > 0)
        {
            foreach (var (ticket, pending) in _awaitingHistory.ToArray())
            {
                var rec = FindHistory(history, ticket);
                if (rec is null && nowMs < pending.DeadlineMs)
                {
                    continue;
                }

                _awaitingHistory.Remove(ticket);
                events.Add(BuildCloseFill(pending.Context, ticket, rec, gapBuy, gapSell));
            }
        }

        _knownTickets = current;
        return events;
    }

    public void Reset()
    {
        _pendingOpens.Clear();
        _pendingCloses.Clear();
        _awaitingHistory.Clear();
        _knownTickets = new HashSet<ulong>();
        _initialized = false;
    }

    private void ExpireStaleOpens(long nowMs)
    {
        while (_pendingOpens.TryPeek(out var head) && nowMs - head.DecideTimeMs > OpenTtlMs)
        {
            _pendingOpens.Dequeue();
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

    private static FillEvent BuildOpenFill(ClickContext ctx, TradeRecord trade, int gapBuy, int gapSell)
    {
        var fillTimeMs = (long)trade.TimeMsc;
        var fillTickCount = (long)trade.OpenEaTimeLocal;
        var fillObservedGap = ctx.Side == DryRunSide.BuyB ? gapBuy : gapSell;
        return new FillEvent(
            IsClose: false,
            Ticket: trade.Ticket,
            Side: ctx.Side,
            ClusterId: ctx.ClusterId,
            DecideTimeMs: ctx.DecideTimeMs,
            FillTimeMs: fillTimeMs,
            SlippageMs: fillTickCount - ctx.DecideTickCount,
            DecideGap: ctx.DecideGap,
            FillObservedGap: fillObservedGap,
            DecidePrice: ctx.DecidePrice,
            FillPrice: trade.Price,
            SlippagePrice: trade.Price - ctx.DecidePrice);
    }

    private static FillEvent BuildCloseFill(ClickContext ctx, ulong ticket, HistoryRecord? historyRec, int gapBuy, int gapSell)
    {
        var fillTimeMs = historyRec is not null ? (long)historyRec.CloseTimeMsc : 0;
        var fillTickCount = historyRec is not null ? (long)historyRec.CloseEaTimeLocal : 0;
        var fillPrice = historyRec is not null ? historyRec.ClosePrice : 0;
        var fillObservedGap = ctx.Side == DryRunSide.BuyB ? gapBuy : gapSell;
        return new FillEvent(
            IsClose: true,
            Ticket: ticket,
            Side: ctx.Side,
            ClusterId: ctx.ClusterId,
            DecideTimeMs: ctx.DecideTimeMs,
            FillTimeMs: fillTimeMs,
            SlippageMs: fillTickCount > 0 ? fillTickCount - ctx.DecideTickCount : 0,
            DecideGap: ctx.DecideGap,
            FillObservedGap: fillObservedGap,
            DecidePrice: ctx.DecidePrice,
            FillPrice: fillPrice,
            SlippagePrice: fillPrice > 0 ? fillPrice - ctx.DecidePrice : 0,
            RealizedUsd: historyRec?.Profit ?? 0,
            Commission: historyRec?.Commission ?? 0);
    }
}
