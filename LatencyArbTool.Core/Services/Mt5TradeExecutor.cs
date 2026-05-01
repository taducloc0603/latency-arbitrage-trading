using LatencyArbTool.Core.Models;

namespace LatencyArbTool.Core.Services;

public sealed class Mt5TradeExecutor
{
    private readonly IMt5TradeGateway _gateway;
    private delegate bool HwndAction(ulong hwnd, out string error);

    public Mt5TradeExecutor(IMt5TradeGateway gateway)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    }

    public LiveTradeResult Execute(DryRunEvent dryRunEvent, bool liveMode, string chartHwndText)
    {
        if (!liveMode)
        {
            return LiveTradeResult.Skipped("live mode disabled");
        }

        if (!_gateway.IsAvailable(out var availabilityError))
        {
            return LiveTradeResult.Failed($"native unavailable: {availabilityError}");
        }

        if (!HwndParser.TryParse(chartHwndText, out var chartHwnd, out var parseError))
        {
            return LiveTradeResult.Failed(parseError);
        }

        if (!_gateway.IsValidWindow(chartHwnd, out var validWindowError))
        {
            return LiveTradeResult.Failed($"invalid HWND: {validWindowError}");
        }

        return dryRunEvent.Decision switch
        {
            "dry open" when dryRunEvent.Side == DryRunSide.BuyB =>
                ExecuteClick("click buy", chartHwnd, _gateway.ClickBuy),
            "dry open" when dryRunEvent.Side == DryRunSide.SellB =>
                ExecuteClick("click sell", chartHwnd, _gateway.ClickSell),
            "dry close" => ExecuteCloseRowZero(chartHwnd),
            _ => LiveTradeResult.Skipped($"ignored event {dryRunEvent.Decision}")
        };
    }

    private LiveTradeResult ExecuteCloseRowZero(ulong parentHwnd)
    {
        if (!_gateway.EnsureContextFromParent(parentHwnd, out var contextError))
        {
            return LiveTradeResult.Failed($"close row 0 context failed: {contextError}");
        }

        return _gateway.ClosePositionMt5(0, out var closeError)
            ? LiveTradeResult.Executed("close MT5 row 0")
            : LiveTradeResult.Failed($"close row 0 failed: {closeError}");
    }

    private static LiveTradeResult ExecuteClick(
        string action,
        ulong chartHwnd,
        HwndAction execute)
    {
        try
        {
            return execute(chartHwnd, out var error)
                ? LiveTradeResult.Executed(action)
                : LiveTradeResult.Failed($"{action} failed: {error}");
        }
        catch (Exception ex)
        {
            return LiveTradeResult.Failed($"{action} failed: {ex.Message}");
        }
    }
}

public sealed record LiveTradeResult(bool Attempted, bool Success, string Message)
{
    public static LiveTradeResult Skipped(string message)
    {
        return new LiveTradeResult(false, false, message);
    }

    public static LiveTradeResult Executed(string message)
    {
        return new LiveTradeResult(true, true, message);
    }

    public static LiveTradeResult Failed(string message)
    {
        return new LiveTradeResult(true, false, message);
    }
}
