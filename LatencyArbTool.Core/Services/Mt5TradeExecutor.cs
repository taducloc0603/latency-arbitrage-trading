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

    // closeTicket: broker ticket of the position the engine wants to close (null
    // until the open fill was observed). With a ticket the close targets that
    // exact row; without one it only proceeds when the target is unambiguous.
    // symbol: the strategy's B symbol; rows on other symbols (manual trades,
    // other bots) are ignored/protected.
    public LiveTradeResult Execute(
        DryRunEvent dryRunEvent,
        string chartHwndText,
        string tradeHwndText,
        TradeReadResult? bTrades = null,
        ulong? closeTicket = null,
        string? symbol = null)
    {
        var (safety, closeRowIndex) = ValidateBTradeState(dryRunEvent, bTrades, closeTicket, symbol);
        if (safety is not null)
        {
            return safety;
        }

        if (!_gateway.IsAvailable(out var availabilityError))
        {
            return LiveTradeResult.Failed($"native unavailable: {availabilityError}");
        }

        return dryRunEvent.Decision switch
        {
            "live open" when dryRunEvent.Side == DryRunSide.BuyB =>
                ExecuteOpen("click buy", chartHwndText, _gateway.ClickBuy),
            "live open" when dryRunEvent.Side == DryRunSide.SellB =>
                ExecuteOpen("click sell", chartHwndText, _gateway.ClickSell),
            "live close" => ExecuteClose(tradeHwndText, closeRowIndex),
            _ => LiveTradeResult.Skipped($"ignored event {dryRunEvent.Decision}")
        };
    }

    private static (LiveTradeResult? Failure, int CloseRowIndex) ValidateBTradeState(
        DryRunEvent dryRunEvent,
        TradeReadResult? bTrades,
        ulong? closeTicket,
        string? symbol)
    {
        if (dryRunEvent.Decision is not ("live open" or "live close"))
        {
            return (null, 0);
        }

        if (bTrades is null)
        {
            return (LiveTradeResult.Failed("B trade state unavailable"), 0);
        }

        if (!bTrades.Success)
        {
            return (LiveTradeResult.Failed($"B trade state unavailable: {bTrades.Error}"), 0);
        }

        if (dryRunEvent.Decision == "live open")
        {
            // Only positions on our symbol block a new open; manual trades on
            // other symbols are none of our business.
            var blocking = CountOnSymbol(bTrades, symbol);
            return blocking == 0
                ? (null, 0)
                : (LiveTradeResult.Failed($"B trade already open ({blocking} on symbol)"), 0);
        }

        var expectedSide = dryRunEvent.Side switch
        {
            DryRunSide.BuyB => TradeSide.Buy,
            DryRunSide.SellB => TradeSide.Sell,
            _ => (TradeSide?)null
        };

        if (expectedSide is null)
        {
            return (LiveTradeResult.Failed("live close side missing"), 0);
        }

        var rowIndex = FindCloseRow(bTrades, closeTicket, symbol);
        if (rowIndex < 0)
        {
            return closeTicket is { } t
                ? (LiveTradeResult.Failed($"close ticket #{t} not in trades map"), 0)
                : (LiveTradeResult.Failed("B trade not open"), 0);
        }

        if (rowIndex == AmbiguousRow)
        {
            return (LiveTradeResult.Failed("no ticket known and multiple positions open; refusing blind close"), 0);
        }

        var target = bTrades.Trades[rowIndex];
        if (target.Side != expectedSide.Value)
        {
            return (LiveTradeResult.Failed(
                $"B trade row {rowIndex} side mismatch: expected {expectedSide}, actual {target.Side}"), 0);
        }

        if (!SymbolMatches(target.Symbol, symbol))
        {
            return (LiveTradeResult.Failed(
                $"B trade row {rowIndex} symbol mismatch: expected {symbol}, actual {target.Symbol}"), 0);
        }

        return (null, rowIndex);
    }

    private const int AmbiguousRow = int.MaxValue;

    // The trades map rows are sorted oldest-first, matching the MT5 trade grid,
    // so a map index doubles as the native close row index.
    private static int FindCloseRow(TradeReadResult bTrades, ulong? closeTicket, string? symbol)
    {
        if (closeTicket is { } ticket)
        {
            for (var i = 0; i < bTrades.Trades.Count; i++)
            {
                if (bTrades.Trades[i].Ticket == ticket)
                {
                    return i;
                }
            }

            return -1;
        }

        // No ticket (open fill never observed): close only when there is exactly
        // one position on our symbol, else it is ambiguous.
        var matchIndex = -1;
        var matches = 0;
        for (var i = 0; i < bTrades.Trades.Count; i++)
        {
            if (SymbolMatches(bTrades.Trades[i].Symbol, symbol))
            {
                matches++;
                matchIndex = i;
            }
        }

        return matches switch
        {
            0 => -1,
            1 => matchIndex,
            _ => AmbiguousRow,
        };
    }

    private static int CountOnSymbol(TradeReadResult bTrades, string? symbol)
    {
        var count = 0;
        foreach (var t in bTrades.Trades)
        {
            if (SymbolMatches(t.Symbol, symbol))
            {
                count++;
            }
        }

        return count;
    }

    private static bool SymbolMatches(string rowSymbol, string? expected)
    {
        return string.IsNullOrWhiteSpace(expected)
            || string.Equals(rowSymbol, expected, StringComparison.OrdinalIgnoreCase);
    }

    private LiveTradeResult ExecuteOpen(string action, string chartHwndText, HwndAction execute)
    {
        if (!TryParseAndValidate(chartHwndText, "chart", out var chartHwnd, out var error))
        {
            return LiveTradeResult.Failed(error);
        }

        return ExecuteClick(action, chartHwnd, execute);
    }

    private LiveTradeResult ExecuteClose(string tradeHwndText, int rowIndex)
    {
        if (!TryParseAndValidate(tradeHwndText, "trade", out var tradeHwnd, out var error))
        {
            return LiveTradeResult.Failed(error);
        }

        if (!_gateway.EnsureContextFromParent(tradeHwnd, out var contextError))
        {
            return LiveTradeResult.Failed($"close row {rowIndex} context failed: {contextError}");
        }

        if (_gateway.ClosePositionMt5(rowIndex, out var closeError))
        {
            return LiveTradeResult.Executed($"close MT5 row {rowIndex}");
        }

        // The cached context may point at a dead window (MT5 restarted): rebuild
        // it once and retry before reporting failure.
        if (_gateway.RecreateContextFromParent(tradeHwnd, out var recreateError)
            && _gateway.ClosePositionMt5(rowIndex, out closeError))
        {
            return LiveTradeResult.Executed($"close MT5 row {rowIndex} (context refreshed)");
        }

        return LiveTradeResult.Failed($"close row {rowIndex} failed: {closeError}");
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
