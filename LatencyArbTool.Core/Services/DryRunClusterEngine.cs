using LatencyArbTool.Core.Models;

namespace LatencyArbTool.Core.Services;

public sealed class DryRunClusterEngine
{
    private long _nextClusterId = 1;
    private int _healthyATickCount;
    private long _lastCloseAtMs;

    public BotState State { get; private set; } = BotState.Idle;
    public DryRunCluster? CurrentCluster { get; private set; }

    public IReadOnlyList<DryRunEvent> Step(
        MarketSnapshot snapshot,
        GapThresholds thresholds,
        SignalSide? signal,
        double? brokerProfitUsd = null)
    {
        var events = new List<DryRunEvent>();

        if (snapshot.FeedAIsStale)
        {
            EnterEmergency(snapshot, events, snapshot.HasValidFeedALatency ? "feed A stale" : "feed A invalid tick latency");
            return events;
        }

        TrackEmergencyRecovery(snapshot, events);

        if (CurrentCluster is not null)
        {
            UpdateFloatingPnl(snapshot);
            UpdatePeakTrough(snapshot);

            if (TryClose(snapshot, thresholds, events, brokerProfitUsd))
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

        // Cooldown gate: previous run rejected ~50% of opens because the bot
        // hammered "open" while MT5 was still settling the previous close.
        if (_lastCloseAtMs > 0 && snapshot.NowMs - _lastCloseAtMs < StrategyDefaults.CooldownAfterCloseMs)
        {
            if (signal is SignalSide.BuyB or SignalSide.SellB)
            {
                events.Add(new DryRunEvent(
                    "guard block",
                    "cooldown after close",
                    State,
                    snapshot.NowMs));
            }
            return events;
        }

        if (signal is SignalSide.BuyB)
        {
            EmitShadowBlocks(snapshot, thresholds, events, out var shadowReasons);
            Open(snapshot, DryRunSide.BuyB, snapshot.B.Ask, events, shadowReasons);
        }
        else if (signal is SignalSide.SellB)
        {
            EmitShadowBlocks(snapshot, thresholds, events, out var shadowReasons);
            Open(snapshot, DryRunSide.SellB, snapshot.B.Bid, events, shadowReasons);
        }

        return events;
    }

    public void Reset()
    {
        State = BotState.Idle;
        CurrentCluster = null;
        _healthyATickCount = 0;
        _lastCloseAtMs = 0;
    }

    private void Open(MarketSnapshot snapshot, DryRunSide side, double price, List<DryRunEvent> events, string shadowReasons = "")
    {
        var cluster = new DryRunCluster(
            _nextClusterId++,
            side,
            snapshot.NowMs,
            snapshot.A.Ask,
            snapshot.A.Bid,
            snapshot.B.Bid,
            snapshot.B.Ask);

        CurrentCluster = cluster;
        State = BotState.Holding;
        AddOrder(snapshot, price, "live open", "confirmed signal", events, shadowReasons);
    }

    private void TryStack(MarketSnapshot snapshot, GapThresholds thresholds, List<DryRunEvent> events)
    {
        if (CurrentCluster is null)
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
            EmitShadowBlocks(snapshot, thresholds, events, out var shadowReasons);
            AddOrder(snapshot, snapshot.B.Ask, "live stack", "buy gap still extreme", events, shadowReasons);
        }
        else if (CurrentCluster.Side == DryRunSide.SellB && snapshot.GapSell >= thresholds.OpenSell)
        {
            EmitShadowBlocks(snapshot, thresholds, events, out var shadowReasons);
            AddOrder(snapshot, snapshot.B.Bid, "live stack", "sell gap still extreme", events, shadowReasons);
        }
    }

    private bool TryClose(MarketSnapshot snapshot, GapThresholds thresholds, List<DryRunEvent> events, double? brokerProfitUsd)
    {
        if (CurrentCluster is null)
        {
            return false;
        }

        var closePriceForBuy = snapshot.B.Bid;
        var closePriceForSell = snapshot.B.Ask;

        // Profit target / loss cap: lock a slip-aided winner the moment broker
        // profit clears the bar, and cut a slip-driven loser before it deepens.
        // These bypass MinHold so they fire immediately after fill if applicable.
        if (brokerProfitUsd.HasValue)
        {
            if (brokerProfitUsd.Value >= StrategyDefaults.ProfitTargetUsd)
            {
                Close(snapshot, CurrentCluster.Side == DryRunSide.BuyB ? closePriceForBuy : closePriceForSell, "profit target hit", events);
                return true;
            }
            if (brokerProfitUsd.Value <= -StrategyDefaults.LossCapUsd)
            {
                Close(snapshot, CurrentCluster.Side == DryRunSide.BuyB ? closePriceForBuy : closePriceForSell, "loss cap hit", events);
                return true;
            }
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
            else
            {
                var gapReverted = snapshot.GapBuy >= thresholds.CloseBuyRevert;

                // Engage trailing on first gap-revert tick that also clears the
                // profit gate. Once active, the close trigger is a B-side
                // retrace from PeakBidB rather than gap-revert / A-reversal.
                if (!CurrentCluster.TrailingActive
                    && gapReverted
                    && brokerProfitUsd.HasValue
                    && brokerProfitUsd.Value >= StrategyDefaults.TrailingActivateProfitUsd)
                {
                    CurrentCluster.TrailingActive = true;
                    events.Add(new DryRunEvent(
                        "trailing engaged",
                        "buy gap reverted, trailing started",
                        State,
                        snapshot.NowMs,
                        CurrentCluster.ClusterId,
                        CurrentCluster.Side,
                        CurrentCluster.Orders.Count));
                }

                if (CurrentCluster.TrailingActive
                    && snapshot.B.Bid <= CurrentCluster.PeakBidB - StrategyDefaults.TrailingDistanceUsd)
                {
                    reason = "buy trailing stop hit";
                }
                else if (!CurrentCluster.TrailingActive && gapReverted)
                {
                    reason = "buy gap reverted";
                }
            }
        }
        else
        {
            closePrice = snapshot.B.Ask;
            if (snapshot.A.Bid >= CurrentCluster.TroughBidA + StrategyDefaults.AReversalUsd)
            {
                reason = "A bid reversal";
            }
            else
            {
                var gapReverted = snapshot.GapSell <= thresholds.CloseSellRevert;

                if (!CurrentCluster.TrailingActive
                    && gapReverted
                    && brokerProfitUsd.HasValue
                    && brokerProfitUsd.Value >= StrategyDefaults.TrailingActivateProfitUsd)
                {
                    CurrentCluster.TrailingActive = true;
                    events.Add(new DryRunEvent(
                        "trailing engaged",
                        "sell gap reverted, trailing started",
                        State,
                        snapshot.NowMs,
                        CurrentCluster.ClusterId,
                        CurrentCluster.Side,
                        CurrentCluster.Orders.Count));
                }

                if (CurrentCluster.TrailingActive
                    && snapshot.B.Ask >= CurrentCluster.TroughAskB + StrategyDefaults.TrailingDistanceUsd)
                {
                    reason = "sell trailing stop hit";
                }
                else if (!CurrentCluster.TrailingActive && gapReverted)
                {
                    reason = "sell gap reverted";
                }
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
            "live close",
            reason,
            State,
            snapshot.NowMs,
            cluster.ClusterId,
            cluster.Side,
            cluster.Orders.Count,
            ClosePrice: closePrice,
            PnlRaw: pnl,
            HoldMs: snapshot.NowMs - cluster.OpenedAtMs,
            PeakBidB: cluster.PeakBidB,
            TroughAskB: cluster.TroughAskB,
            TrailingActive: cluster.TrailingActive));

        CurrentCluster = null;
        State = State == BotState.Emergency ? BotState.Emergency : BotState.Idle;
        _lastCloseAtMs = snapshot.NowMs;
    }

    private void AddOrder(
        MarketSnapshot snapshot,
        double price,
        string decision,
        string reason,
        List<DryRunEvent> events,
        string shadowReasons = "")
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
            Lot: lot,
            ShadowBlockReasons: shadowReasons));
    }

    // Loosened: guards now run in shadow mode. They no longer block opens/stacks;
    // they emit "shadow block" events so we can analyze post-run which trades
    // would have been filtered. The same reasons are also packed into the open/stack
    // event's ShadowBlockReasons field for direct correlation in CSV.
    private void EmitShadowBlocks(MarketSnapshot snapshot, GapThresholds thresholds, List<DryRunEvent> events, out string joined)
    {
        var reasons = new List<string>(3);
        if (snapshot.FeedBIsStale)
        {
            reasons.Add(snapshot.HasValidFeedBLatency ? "feed B stale" : "feed B invalid tick latency");
        }
        if (thresholds.MedianSpreadB > 0 &&
            snapshot.B.Spread > thresholds.MedianSpreadB * StrategyDefaults.SpreadBMaxMultiplier)
        {
            reasons.Add("spread B abnormal");
        }
        if (thresholds.ARangePoints < StrategyDefaults.MinAVolPoints)
        {
            reasons.Add("A volatility low");
        }

        foreach (var r in reasons)
        {
            events.Add(new DryRunEvent(
                "shadow block",
                r,
                State,
                snapshot.NowMs,
                CurrentCluster?.ClusterId,
                CurrentCluster?.Side,
                CurrentCluster?.Orders.Count ?? 0));
        }

        joined = reasons.Count == 0 ? string.Empty : string.Join("|", reasons);
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
        CurrentCluster.PeakBidB = Math.Max(CurrentCluster.PeakBidB, snapshot.B.Bid);
        CurrentCluster.TroughAskB = Math.Min(CurrentCluster.TroughAskB, snapshot.B.Ask);
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
