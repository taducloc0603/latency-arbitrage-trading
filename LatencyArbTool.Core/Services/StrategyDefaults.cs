namespace LatencyArbTool.Core.Services;

public static class StrategyDefaults
{
    public const int PointMultiplier = 100;

    public const int MedianWindowMinutes = 5;
    public const double KStd = 2.0;

    public const int WarmupMinSamples = 100;
    // Loosened back from -75/+65 toward the original -50/+40 zone now that
    // slippage on the new broker is ~0 pts (run 5 confirmed). Run 1 showed
    // the |gap| 50-75 bucket had a positive edge but was poisoned by 51-pt
    // slippage drift. With slippage solved, this zone should be profitable
    // — and triggers far more trades than -75/+65.
    public const int FixedOpenBuyFallback = -55;
    public const int FixedOpenSellFallback = 50;

    // Run 7 had 6000 qualifying-gap ticks over 3.8h but only 5 trades fired —
    // signal was rare. Tightening confirm window 200→150ms lets signal lock in
    // faster on short gap bursts without abandoning the confirm/recheck pattern.
    public const int ConfirmMs = 150;
    public const int ReCheckMs = 100;
    public const double StabilityRatio = 0.4;
    public const int StackCooldownMs = 1000;
    public const int MinHoldMs = 1500;
    public const int MaxHoldMs = 90000;
    public const int MaxStack = 1;

    public const int CloseBuyRevertFallback = 0;
    public const int CloseSellRevertFallback = 0;
    public const double AReversalUsd = 1.20;

    // Silence threshold: ms since last new tick (ea_ms change). Larger = more lenient
    // for sparse feeds. Different from raw latency which grows during quiet markets.
    public const int FeedAStaleMs = 10000;
    public const int FeedBStaleMs = 3000;
    public const double SpreadBMaxMultiplier = 5.0;

    public const int AVolWindowMs = 60_000;
    public const int MinAVolPoints = 20;

    public const int LotBandOneMaxGap = 60;
    public const int LotBandTwoMaxGap = 70;

    // Adaptive slippage compensation: bias OpenBuy/OpenSell by the rolling-median
    // gap drift between decide-click and broker-fill. Same code works on both a
    // VN VPS (large drift) and a London VPS (small drift) — threshold auto-shifts.
    public const int SlippageWindowFills = 30;
    public const int SlippageWarmupMinFills = 5;
    // 0 = no bias before measurement. Avoids the chicken-and-egg trap where a
    // big default bias (e.g. 30 pts) makes threshold so deep that no fills ever
    // happen, so the median is never measured. Once 5+ fills accumulate the
    // measured median takes over automatically.
    public const int SlippageDefaultBiasPts = 0;

    // Velocity filter: skip "stuck" gaps that are about to revert. Negative
    // value = filter effectively disabled (only excludes gap *contracting*
    // faster than this rate). Tighten upward (e.g. +30) once base volume looks
    // healthy and we can afford to drop the weakest signals.
    public const int VelocityWindowMs = 200;
    public const double MinFavorableVelocityPtsPerSec = -100.0;

    // Cool-down between close and the next open click — prevents the bot from
    // hammering open while MT5 is still processing the previous close, which
    // produced ~50% rejection rate ("B trade already open") in earlier runs.
    public const int CooldownAfterCloseMs = 750;

    // Profit target / loss cap close based on broker's live profit (USD).
    // Independent of gap-revert / A-reversal, so a slip-aided winner is locked
    // in fast and a slip-driven loser is cut before it deepens.
    public const double ProfitTargetUsd = 30.0;
    // Tightened from $50 to $10 after run 5 showed actual win/loss range is
    // $0-$10, not $40-$90 as in earlier high-lot runs. A $50 cap rarely fires
    // and lets bad trades deepen — $10 cuts losers near the avg-loss level.
    public const double LossCapUsd = 10.0;

    // Trailing stop on B price: when gap has reverted (thesis met) AND broker
    // profit cleared TrailingActivateProfitUsd, the cluster switches into
    // trailing mode — it holds until B retraces by TrailingDistanceUsd from the
    // peak (BUY) / trough (SELL). Lets winners run if A keeps moving.
    //
    // Run 7 showed trailing still didn't engage even on $1.65 winners — broker
    // POSITION_PROFIT reporting lags / under-reports on small lots. The fix is
    // to engage trailing primarily on PRICE movement (data the bot already has
    // tick-by-tick), with broker profit as a fallback trigger.
    //
    // Engagement: B price moved TrailingActivatePriceUsd favorably from open
    // OR broker profit clears TrailingActivateProfitUsd.
    // Close: B retraces TrailingDistanceUsd from peak (BUY) / trough (SELL).
    // Distance tightened $0.20 → $0.10 so we don't give back more than we
    // need to ride out short-lot noise.
    public const double TrailingActivatePriceUsd = 0.10;
    public const double TrailingDistanceUsd = 0.10;
    public const double TrailingActivateProfitUsd = 0.5;

    // Close-confirmation retry: run 5 trade 11 saw bot click close at t=3.5s
    // but broker only processed it at t=89s, costing $17.79 (62% of total loss).
    // After sending a close click, we wait CloseRetryThresholdMs for the ticket
    // to leave the trades map. If it's still there, we re-fire the close click
    // up to CloseRetryMax times. ClosePositionMt5(row=0) is idempotent and
    // ValidateBTradeState rejects retries once the position is gone, so this
    // is safe even if the original click eventually goes through.
    // Run 8 trade 2: bot decided close at t=6.6s, broker only processed it at
    // t=113s. Retry was 5×1.5s=7.5s — gave up at 7.5s and trade just happened
    // to close favorably by luck. Bumped to 30×1.5s=45s so we keep banging on
    // close clicks instead of waiting on broker whim.
    public const int CloseRetryThresholdMs = 1500;
    public const int CloseRetryMax = 30;

    // Lead detection: the strategy's edge requires A to lead the move that
    // produced the gap. When B leads instead (e.g. broker B quote jumps before
    // the rest of the market), gap looks the same but the bet is wrong and the
    // trade typically loses. To filter:
    //   1) A must have moved at least MinLeadChangePts in the velocity window
    //      AND in the direction that matches the trade thesis
    //      (positive for BUY = expect B to rise; negative for SELL = expect B to fall).
    //   2) |A move| must be at least LeadRatio × |B move| so A clearly led.
    // Run 8+9 confirmed trailing works (4/5 engagement, PnL flipped to +$0.25)
    // and trade rate doubled from 1.3 to 2.4 /hr. Push lead detection one more
    // notch: A move ≥ 8 pts is enough, dominate B by 20%. Should push trade
    // rate to ~3-4/hr while still filtering obvious B-led gaps.
    public const int MinLeadChangePts = 8;
    public const double LeadRatio = 1.2;
}
