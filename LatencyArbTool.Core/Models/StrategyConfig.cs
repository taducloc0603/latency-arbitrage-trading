namespace LatencyArbTool.Core.Models;

// Strategy parameters loaded per-machine from the DB (Supabase). Replaces the
// old compile-time StrategyDefaults consts. All point fields are in "points"
// (price * Point).
public sealed record StrategyConfig(
    string Id,                 // DB row id (for write-back); empty for Default
    string GroupName,
    string Hostname,
    int Point,                 // price -> point multiplier
    int OpenPts,               // x: final gap must reach this to fire
    int OpenHoldConfirmMs,     // y: gap must hold the sustain floor for this long
    int OpenConfirmGapPts,     // z: sustain floor across the whole confirm window
    int StopLossPoint,         // SL distance (used while trailing not active)
    int TrailingStartPoint,    // profit distance that activates trailing
    int TrailingStepPoint,     // trailing give-back from peak/trough
    string MapA,
    string MapB,
    string? ChartHwndB,        // HWND of B chart (click buy/sell)
    string? TradeHwndB)        // HWND of B trade panel (close)
{
    // Sensible offline fallback for tests / first run before a row is loaded.
    public static StrategyConfig Default { get; } = new(
        Id: "",
        GroupName: "default",
        Hostname: "",
        Point: 100,
        OpenPts: 100,
        OpenHoldConfirmMs: 1000,
        OpenConfirmGapPts: 0,
        StopLossPoint: 50,
        TrailingStartPoint: 200,
        TrailingStepPoint: 30,
        MapA: @"Local\MT_A_Tick",
        MapB: @"Local\MT_B_Tick",
        ChartHwndB: null,
        TradeHwndB: null);
}
