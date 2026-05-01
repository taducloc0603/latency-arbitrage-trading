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

    public LiveTradeResult Execute(
        DryRunEvent dryRunEvent,
        bool liveMode,
        string chartHwndText,
        string tradeHwndText)
    {
        if (!liveMode)
        {
            return LiveTradeResult.Skipped("live mode disabled");
        }

        if (!_gateway.IsAvailable(out var availabilityError))
        {
            return LiveTradeResult.Failed($"native unavailable: {availabilityError}");
        }

        return dryRunEvent.Decision switch
        {
            "dry open" when dryRunEvent.Side == DryRunSide.BuyB =>
                ExecuteOpen("click buy", chartHwndText, _gateway.ClickBuy),
            "dry open" when dryRunEvent.Side == DryRunSide.SellB =>
                ExecuteOpen("click sell", chartHwndText, _gateway.ClickSell),
            "dry close" => ExecuteCloseRowZero(tradeHwndText),
            _ => LiveTradeResult.Skipped($"ignored event {dryRunEvent.Decision}")
        };
    }

    private LiveTradeResult ExecuteOpen(string action, string chartHwndText, HwndAction execute)
    {
        if (!TryParseAndValidate(chartHwndText, "chart", out var chartHwnd, out var error))
        {
            return LiveTradeResult.Failed(error);
        }

        return ExecuteClick(action, chartHwnd, execute);
    }

    private LiveTradeResult ExecuteCloseRowZero(string tradeHwndText)
    {
        if (!TryParseAndValidate(tradeHwndText, "trade", out var tradeHwnd, out var error))
        {
            return LiveTradeResult.Failed(error);
        }

        if (!_gateway.EnsureContextFromParent(tradeHwnd, out var contextError))
        {
            return LiveTradeResult.Failed($"close row 0 context failed: {contextError}");
        }

        return _gateway.ClosePositionMt5(0, out var closeError)
            ? LiveTradeResult.Executed("close MT5 row 0")
            : LiveTradeResult.Failed($"close row 0 failed: {closeError}");
    }

    private bool TryParseAndValidate(string hwndText, string label, out ulong hwnd, out string error)
    {
        if (!HwndParser.TryParse(hwndText, out hwnd, out var parseError))
        {
            error = $"{label} HWND invalid: {parseError}";
            return false;
        }

        if (!_gateway.IsValidWindow(hwnd, out var validWindowError))
        {
            error = $"{label} HWND invalid: {validWindowError}";
            return false;
        }

        error = string.Empty;
        return true;
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
