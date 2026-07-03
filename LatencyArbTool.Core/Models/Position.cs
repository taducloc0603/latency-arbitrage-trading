namespace LatencyArbTool.Core.Models;

// A single open position on broker B. Only one is held at a time (no stacking).
// All prices are stored in points (price * Point).
public sealed class Position
{
    public Position(SignalSide side, int entryPoint, long openedAtMs)
    {
        Side = side;
        EntryPoint = entryPoint;
        OpenedAtMs = openedAtMs;
        HighestPoint = entryPoint;
        LowestPoint = entryPoint;
    }

    public SignalSide Side { get; }

    // BUY entry = B.Ask*point ; SELL entry = B.Bid*point. Settable so the live
    // path can correct it to the broker's actual fill price once known.
    public int EntryPoint { get; set; }

    public long OpenedAtMs { get; }

    public bool TrailingActive { get; set; }

    // Highest current price since trailing activated (BUY trailing reference).
    public int HighestPoint { get; set; }

    // Lowest current price since trailing activated (SELL trailing reference).
    public int LowestPoint { get; set; }

    public long ClusterId { get; init; }

    // Broker ticket, known once the open fill is observed in the trades map.
    // Null until then; used to close the exact position instead of "row 0".
    public ulong? Ticket { get; set; }

    // A close was decided but the close click has not been confirmed yet. The
    // position stays owned by the engine (and keeps emitting retry closes)
    // until ConfirmClose reports the click succeeded.
    public bool CloseRequested { get; set; }

    public string? CloseReason { get; set; }

    public long LastCloseAttemptMs { get; set; }
}
