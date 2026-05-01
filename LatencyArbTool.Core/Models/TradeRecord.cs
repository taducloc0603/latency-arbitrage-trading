namespace LatencyArbTool.Core.Models;

public sealed record TradeRecord(
    ulong Ticket,
    TradeSide Side,
    double Lot,
    double Price,
    double StopLoss,
    double TakeProfit,
    double Profit,
    ulong TimeMsc,
    ulong OpenEaTimeLocal,
    string Symbol);

public enum TradeSide
{
    Buy,
    Sell
}
