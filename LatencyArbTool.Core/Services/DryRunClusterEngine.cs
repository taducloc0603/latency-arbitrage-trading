using LatencyArbTool.Core.Models;

namespace LatencyArbTool.Core.Services;

public sealed class DryRunClusterEngine
{
    private long _nextClusterId = 1;
    private int _healthyATickCount;

    public BotState State { get; private set; } = BotState.Idle;
    public DryRunCluster? CurrentCluster { get; private set; }

    public IReadOnlyList<DryRunEvent> Step(
        MarketSnapshot snapshot,
        GapThresholds thresholds,
        SignalSide? signal)
    {
        var events = new List<DryRunEvent>();

        if (snapshot.FeedAIsStale)
        {
            EnterEmergency(snapshot, events, snapshot.HasValidFeedATimestamp ? "feed A stale" : "feed A invalid tick timestamp");
            return events;
        }

        TrackEmergencyRecovery(snapshot, events);

        if (CurrentCluster is not null)
        {
            UpdateFloatingPnl(snapshot);
            UpdatePeakTrough(snapshot);

            if (TryClose(snapshot, thresholds, events))
            {
                return events;
            }

            TryStack(snapshot, thresholds, events);
            return events;
        }

        if (State == BotState.Emergency)
        {
            return events;
        }

        if (!CanOpenOrStack(snapshot, thresholds, events))
        {
            return events;
        }

        if (signal is SignalSide.BuyB)
        {
            Open(snapshot, DryRunSide.BuyB, snapshot.B.Ask, events);
        }
        else if (signal is SignalSide.SellB)
        {
            Open(snapshot, DryRunSide.SellB, snapshot.B.Bid, events);
        }

        return events;
    }

    public void Reset()
    {
        State = BotState.Idle;
        CurrentCluster = null;
        _healthyATickCount = 0;
    }

    private void Open(MarketSnapshot snapshot, DryRunSide side, double price, List<DryRunEvent> events)
    {
        var cluster = new DryRunCluster(
            _nextClusterId++,
            side,
            snapshot.NowMs,
            snapshot.A.Ask,
            snapshot.A.Bid);

        CurrentCluster = cluster;
        State = BotState.Holding;
        AddOrder(snapshot, price, "dry open", "confirmed signal", events);
    }

    private void TryStack(MarketSnapshot snapshot, GapThresholds thresholds, List<DryRunEvent> events)
    {
        if (CurrentCluster is null || !CanOpenOrStack(snapshot, thresholds, events))
        {
            return;
        }

        if (snapshot.NowMs - CurrentCluster.LastActionAtMs < StrategyDefaults.StackCooldownMs)
        {
            return;
        }

        var allowed = MaxOrdersForGap(CurrentCluster.Side == DryRunSide.BuyB ? snapshot.GapBuy : snapshot.GapSell);
        if (CurrentCluster.Orders.Count >= Math.Min(StrategyDefaults.MaxStack, allowed))
        {
            return;
        }

        if (CurrentCluster.Side == DryRunSide.BuyB && snapshot.GapBuy <= thresholds.OpenBuy)
        {
            AddOrder(snapshot, snapshot.B.Ask, "dry stack", "buy gap still extreme", events);
        }
        else if (CurrentCluster.Side == DryRunSide.SellB && snapshot.GapSell >= thresholds.OpenSell)
        {
            AddOrder(snapshot, snapshot.B.Bid, "dry stack", "sell gap still extreme", events);
        }
    }

    private bool TryClose(MarketSnapshot snapshot, GapThresholds thresholds, List<DryRunEvent> events)
    {
        if (CurrentCluster is null)
        {
            return false;
        }

        var holdMs = snapshot.NowMs - CurrentCluster.OpenedAtMs;
        var emergencyClose = State == BotState.Emergency || snapshot.FeedAIsStale;
        var maxHoldClose = holdMs >= StrategyDefaults.MaxHoldMs;

        if (!emergencyClose && !maxHoldClose && holdMs < StrategyDefaults.MinHoldMs)
        {
            return false;
        }

        string? reason = null;
        var closePrice = 0.0;

        if (CurrentCluster.Side == DryRunSide.BuyB)
        {
            closePrice = snapshot.B.Bid;
            if (snapshot.A.Ask <= CurrentCluster.PeakAskA - StrategyDefaults.AReversalUsd)
            {
                reason = "A ask reversal";
            }
            else if (snapshot.GapBuy >= thresholds.CloseBuyRevert)
            {
                reason = "buy gap reverted";
            }
        }
        else
        {
            closePrice = snapshot.B.Ask;
            if (snapshot.A.Bid >= CurrentCluster.TroughBidA + StrategyDefaults.AReversalUsd)
            {
                reason = "A bid reversal";
            }
            else if (snapshot.GapSell <= thresholds.CloseSellRevert)
            {
                reason = "sell gap reverted";
            }
        }

        reason ??= maxHoldClose ? "max hold reached" : null;
        reason ??= emergencyClose ? "emergency close" : null;

        if (reason is null)
        {
            return false;
        }

        Close(snapshot, closePrice, reason, events);
        return true;
    }

    private void Close(MarketSnapshot snapshot, double closePrice, string reason, List<DryRunEvent> events)
    {
        if (CurrentCluster is null)
        {
            return;
        }

        var cluster = CurrentCluster;
        var pnl = cluster.Orders.Sum(order => Pnl(order, closePrice));
        cluster.ClosedAtMs = snapshot.NowMs;
        cluster.CloseReason = reason;
        cluster.RealizedPnlRaw = pnl;

        events.Add(new DryRunEvent(
            "dry close",
            reason,
            State,
            snapshot.NowMs,
            cluster.ClusterId,
            cluster.Side,
            cluster.Orders.Count,
            ClosePrice: closePrice,
            PnlRaw: pnl,
            HoldMs: snapshot.NowMs - cluster.OpenedAtMs));

        CurrentCluster = null;
        State = State == BotState.Emergency ? BotState.Emergency : BotState.Idle;
    }

    private void AddOrder(
        MarketSnapshot snapshot,
        double price,
        string decision,
        string reason,
        List<DryRunEvent> events)
    {
        if (CurrentCluster is null)
        {
            return;
        }

        var gap = CurrentCluster.Side == DryRunSide.BuyB ? snapshot.GapBuy : snapshot.GapSell;
        var lot = LotForGap(gap);
        var order = new DryRunOrder(
            CurrentCluster.Orders.Count + 1,
            CurrentCluster.Side,
            price,
            lot,
            snapshot.NowMs);

        CurrentCluster.Orders.Add(order);
        CurrentCluster.LastActionAtMs = snapshot.NowMs;

        events.Add(new DryRunEvent(
            decision,
            reason,
            State,
            snapshot.NowMs,
            CurrentCluster.ClusterId,
            CurrentCluster.Side,
            CurrentCluster.Orders.Count,
            OpenPrice: price,
            Lot: lot));
    }

    private bool CanOpenOrStack(MarketSnapshot snapshot, GapThresholds thresholds, List<DryRunEvent> events)
    {
        if (snapshot.FeedBIsStale)
        {
            events.Add(Block(snapshot, snapshot.HasValidFeedBTimestamp ? "feed B stale" : "feed B invalid tick timestamp"));
            return false;
        }

        if (thresholds.MedianSpreadB > 0 &&
            snapshot.B.Spread > thresholds.MedianSpreadB * StrategyDefaults.SpreadBMaxMultiplier)
        {
            events.Add(Block(snapshot, "spread B abnormal"));
            return false;
        }

        return true;
    }

    private DryRunEvent Block(MarketSnapshot snapshot, string reason)
    {
        return new DryRunEvent(
            "guard block",
            reason,
            State,
            snapshot.NowMs,
            CurrentCluster?.ClusterId,
            CurrentCluster?.Side,
            CurrentCluster?.Orders.Count ?? 0);
    }

    private void EnterEmergency(MarketSnapshot snapshot, List<DryRunEvent> events, string reason)
    {
        if (State != BotState.Emergency)
        {
            State = BotState.Emergency;
            events.Add(new DryRunEvent("emergency", reason, State, snapshot.NowMs));
        }

        if (CurrentCluster is not null)
        {
            Close(snapshot, CurrentCluster.Side == DryRunSide.BuyB ? snapshot.B.Bid : snapshot.B.Ask, reason, events);
        }
    }

    private void TrackEmergencyRecovery(MarketSnapshot snapshot, List<DryRunEvent> events)
    {
        if (State != BotState.Emergency)
        {
            _healthyATickCount = 0;
            return;
        }

        _healthyATickCount = snapshot.FeedALatencyMs <= 1000 ? _healthyATickCount + 1 : 0;
        if (_healthyATickCount >= 10)
        {
            State = BotState.Idle;
            _healthyATickCount = 0;
            events.Add(new DryRunEvent("resume", "10 healthy A ticks", State, snapshot.NowMs));
        }
    }

    private void UpdatePeakTrough(MarketSnapshot snapshot)
    {
        if (CurrentCluster is null)
        {
            return;
        }

        CurrentCluster.PeakAskA = Math.Max(CurrentCluster.PeakAskA, snapshot.A.Ask);
        CurrentCluster.TroughBidA = Math.Min(CurrentCluster.TroughBidA, snapshot.A.Bid);
    }

    private void UpdateFloatingPnl(MarketSnapshot snapshot)
    {
        if (CurrentCluster is null)
        {
            return;
        }

        var closePrice = CurrentCluster.Side == DryRunSide.BuyB ? snapshot.B.Bid : snapshot.B.Ask;
        CurrentCluster.FloatingPnlRaw = CurrentCluster.Orders.Sum(order => Pnl(order, closePrice));
    }

    private static double Pnl(DryRunOrder order, double closePrice)
    {
        return order.Side == DryRunSide.BuyB
            ? (closePrice - order.OpenPrice) * order.Lot
            : (order.OpenPrice - closePrice) * order.Lot;
    }

    public static int MaxOrdersForGap(int gap)
    {
        var absGap = Math.Abs(gap);
        if (absGap <= StrategyDefaults.LotBandOneMaxGap)
        {
            return 3;
        }

        return absGap <= StrategyDefaults.LotBandTwoMaxGap ? 7 : 10;
    }

    public static double LotForGap(int gap)
    {
        var absGap = Math.Abs(gap);
        if (absGap <= StrategyDefaults.LotBandOneMaxGap)
        {
            return 8.0;
        }

        return absGap <= StrategyDefaults.LotBandTwoMaxGap ? 7.0 : 5.0;
    }
}
