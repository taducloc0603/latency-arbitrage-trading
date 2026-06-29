namespace LatencyArbTool.App.ViewModels;

// Row shown in the "B Trade" tab. Profit updates live (every poll) while the
// position is held, so it is mutable/observable; the rest is set once at open.
public sealed class BTradeRow : ObservableObject
{
    private double _profit;

    public ulong Ticket { get; init; }
    public string Side { get; init; } = string.Empty;
    public double Lot { get; init; }
    public double OpenPrice { get; init; }
    public double StopLoss { get; init; }
    public double TakeProfit { get; init; }

    public double Profit
    {
        get => _profit;
        set => SetProperty(ref _profit, value);
    }
}

// Row shown in the "B History" tab (closed trades). Immutable.
public sealed record BHistoryRow(
    ulong Ticket,
    string Side,
    double OpenPrice,
    double ClosePrice,
    double Profit,
    double Commission,
    string CloseTime);
