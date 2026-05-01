namespace LatencyArbTool.Core.Models;

public sealed record HistoryRecord(
    ulong Ticket,
    TradeSide Side,
    double Volume,
    double OpenPrice,
    double ClosePrice,
    double StopLoss,
    double TakeProfit,
    double Commission,
    double Profit,
    ulong OpenTimeMsc,
    ulong CloseTimeMsc,
    ulong CloseEaTimeLocal,
    string Symbol);
