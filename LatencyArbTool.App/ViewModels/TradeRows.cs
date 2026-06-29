namespace LatencyArbTool.App.ViewModels;

// Row shown in the "B Trade" tab — all TradeRecord fields, in record order.
// Profit updates live (every poll) while the position is held, so it is
// mutable/observable; the rest is set once at open.
public sealed class BTradeRow : ObservableObject
{
    private double _profit;

    public ulong Ticket { get; init; }
    public string Side { get; init; } = string.Empty;
    public double Lot { get; init; }
    public double Price { get; init; }
    public double StopLoss { get; init; }
    public double TakeProfit { get; init; }
    public string Time { get; init; } = string.Empty;        // from TimeMsc
    public ulong OpenEaTimeLocal { get; init; }
    public string Symbol { get; init; } = string.Empty;

    public double Profit
    {
        get => _profit;
        set => SetProperty(ref _profit, value);
    }
}

// Row shown in the "B History" tab — all HistoryRecord fields, in record order. Immutable.
public sealed record BHistoryRow(
    ulong Ticket,
    string Side,
    double Volume,
    double OpenPrice,
    double ClosePrice,
    double StopLoss,
    double TakeProfit,
    double Commission,
    double Profit,
    string OpenTime,          // from OpenTimeMsc
    string CloseTime,         // from CloseTimeMsc
    ulong CloseEaTimeLocal,
    string Symbol);
