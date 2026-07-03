using LatencyArbTool.Core.Models;
using LatencyArbTool.Core.Services;

namespace LatencyArbTool.Tests;

public sealed class Mt5TradeExecutorTests
{
    [Fact]
    public void Execute_InvalidHwndDoesNotCallNative()
    {
        var gateway = new FakeGateway();
        var executor = new Mt5TradeExecutor(gateway);

        var result = executor.Execute(Event("live open", DryRunSide.BuyB), "0", "456", EmptyTrades());

        Assert.True(result.Attempted);
        Assert.False(result.Success);
        Assert.Equal(0, gateway.BuyCalls);
    }

    [Fact]
    public void Execute_LiveOpenBuyClicksBuy()
    {
        var gateway = new FakeGateway();
        var executor = new Mt5TradeExecutor(gateway);

        var result = executor.Execute(Event("live open", DryRunSide.BuyB), "chart 0x3039", "trade 0x50226", EmptyTrades());

        Assert.True(result.Success);
        Assert.Equal(1, gateway.BuyCalls);
        Assert.Equal(0, gateway.SellCalls);
        Assert.Equal(0x3039UL, gateway.LastBuyHwnd);
    }

    [Fact]
    public void Execute_LiveOpenSellClicksSell()
    {
        var gateway = new FakeGateway();
        var executor = new Mt5TradeExecutor(gateway);

        var result = executor.Execute(Event("live open", DryRunSide.SellB), "chart 12345", "trade 0x50226", EmptyTrades());

        Assert.True(result.Success);
        Assert.Equal(0, gateway.BuyCalls);
        Assert.Equal(1, gateway.SellCalls);
        Assert.Equal(12345UL, gateway.LastSellHwnd);
    }

    [Fact]
    public void Execute_LiveCloseClosesSinglePositionRow()
    {
        var gateway = new FakeGateway();
        var executor = new Mt5TradeExecutor(gateway);

        var result = executor.Execute(Event("live close", DryRunSide.BuyB), "chart 0x3039", "trade 0x50226", OneTrade(TradeSide.Buy));

        Assert.True(result.Success);
        Assert.Equal(1, gateway.EnsureContextCalls);
        Assert.Equal(0x50226UL, gateway.LastContextHwnd);
        Assert.Equal(0, gateway.ClosedRow);
    }

    [Fact]
    public void Execute_LiveCloseWithTicketTargetsThatRow()
    {
        var gateway = new FakeGateway();
        var executor = new Mt5TradeExecutor(gateway);
        var trades = Trades(
            Trade(100, TradeSide.Buy),   // row 0: older position (orphan/manual)
            Trade(200, TradeSide.Buy));  // row 1: the engine's position

        var result = executor.Execute(
            Event("live close", DryRunSide.BuyB), "12345", "456", trades, closeTicket: 200);

        Assert.True(result.Success);
        Assert.Equal(1, gateway.ClosedRow);
    }

    [Fact]
    public void Execute_LiveCloseTicketMissingFails()
    {
        var gateway = new FakeGateway();
        var executor = new Mt5TradeExecutor(gateway);

        var result = executor.Execute(
            Event("live close", DryRunSide.BuyB), "12345", "456", OneTrade(TradeSide.Buy), closeTicket: 999);

        Assert.False(result.Success);
        Assert.Contains("not in trades map", result.Message);
        Assert.Null(gateway.ClosedRow);
    }

    [Fact]
    public void Execute_LiveCloseNoTicketMultiplePositionsRefusesBlindClose()
    {
        var gateway = new FakeGateway();
        var executor = new Mt5TradeExecutor(gateway);
        var trades = Trades(Trade(100, TradeSide.Buy), Trade(200, TradeSide.Buy));

        var result = executor.Execute(Event("live close", DryRunSide.BuyB), "12345", "456", trades);

        Assert.False(result.Success);
        Assert.Contains("multiple positions", result.Message);
        Assert.Null(gateway.ClosedRow);
    }

    [Fact]
    public void Execute_LiveCloseSymbolMismatchFails()
    {
        var gateway = new FakeGateway();
        var executor = new Mt5TradeExecutor(gateway);
        var trades = Trades(Trade(100, TradeSide.Buy, "EURUSD"));

        var result = executor.Execute(
            Event("live close", DryRunSide.BuyB), "12345", "456", trades, symbol: "XAUUSD");

        Assert.False(result.Success);
        Assert.Null(gateway.ClosedRow);
    }

    [Fact]
    public void Execute_OpenIgnoresPositionsOnOtherSymbols()
    {
        var gateway = new FakeGateway();
        var executor = new Mt5TradeExecutor(gateway);
        var trades = Trades(Trade(100, TradeSide.Buy, "EURUSD")); // manual trade elsewhere

        var result = executor.Execute(
            Event("live open", DryRunSide.BuyB), "12345", "456", trades, symbol: "XAUUSD");

        Assert.True(result.Success);
        Assert.Equal(1, gateway.BuyCalls);
    }

    [Fact]
    public void Execute_CloseFailureRecreatesContextAndRetries()
    {
        var gateway = new FakeGateway { CloseFailuresBeforeSuccess = 1 };
        var executor = new Mt5TradeExecutor(gateway);

        var result = executor.Execute(Event("live close", DryRunSide.BuyB), "12345", "456", OneTrade(TradeSide.Buy));

        Assert.True(result.Success);
        Assert.Equal(1, gateway.RecreateContextCalls);
        Assert.Equal(2, gateway.CloseCalls);
        Assert.Contains("context refreshed", result.Message);
    }

    [Fact]
    public void Execute_NativeUnavailableFailsWithoutCallingActions()
    {
        var gateway = new FakeGateway { Available = false };
        var executor = new Mt5TradeExecutor(gateway);

        var result = executor.Execute(Event("live open", DryRunSide.BuyB), "12345", "456", EmptyTrades());

        Assert.True(result.Attempted);
        Assert.False(result.Success);
        Assert.Equal(0, gateway.BuyCalls);
    }

    [Fact]
    public void Execute_BlocksOpenWhenBTradeAlreadyExists()
    {
        var gateway = new FakeGateway();
        var executor = new Mt5TradeExecutor(gateway);

        var result = executor.Execute(Event("live open", DryRunSide.BuyB), "12345", "456", OneTrade(TradeSide.Buy));

        Assert.True(result.Attempted);
        Assert.False(result.Success);
        Assert.Contains("B trade already open", result.Message);
        Assert.Equal(0, gateway.BuyCalls);
    }

    [Fact]
    public void Execute_BlocksCloseWhenNoBTradeExists()
    {
        var gateway = new FakeGateway();
        var executor = new Mt5TradeExecutor(gateway);

        var result = executor.Execute(Event("live close", DryRunSide.BuyB), "12345", "456", EmptyTrades());

        Assert.True(result.Attempted);
        Assert.False(result.Success);
        Assert.Equal("B trade not open", result.Message);
        Assert.Null(gateway.ClosedRow);
    }

    [Fact]
    public void Execute_BlocksCloseWhenBTradeSideMismatches()
    {
        var gateway = new FakeGateway();
        var executor = new Mt5TradeExecutor(gateway);

        var result = executor.Execute(Event("live close", DryRunSide.BuyB), "12345", "456", OneTrade(TradeSide.Sell));

        Assert.True(result.Attempted);
        Assert.False(result.Success);
        Assert.Contains("side mismatch", result.Message);
        Assert.Null(gateway.ClosedRow);
    }

    private static DryRunEvent Event(string decision, DryRunSide side)
    {
        return new DryRunEvent(decision, "test", BotState.Holding, 1, Side: side);
    }

    private static TradeReadResult EmptyTrades()
    {
        return TradeReadResult.Ok("Local\\MT_B_Trade", 1000, Array.Empty<TradeRecord>());
    }

    private static TradeRecord Trade(ulong ticket, TradeSide side, string symbol = "XAUUSD")
    {
        return new TradeRecord(ticket, side, 1.5, 2020.25, 0, 0, 12.34, 1, 999, symbol);
    }

    private static TradeReadResult Trades(params TradeRecord[] trades)
    {
        return TradeReadResult.Ok("Local\\MT_B_Trade", 1000, trades);
    }

    private static TradeReadResult OneTrade(TradeSide side)
    {
        return Trades(Trade(123456, side));
    }

    private sealed class FakeGateway : IMt5TradeGateway
    {
        public bool Available { get; init; } = true;
        public int CloseFailuresBeforeSuccess { get; init; }
        public int BuyCalls { get; private set; }
        public int SellCalls { get; private set; }
        public int EnsureContextCalls { get; private set; }
        public int RecreateContextCalls { get; private set; }
        public int CloseCalls { get; private set; }
        public ulong? LastBuyHwnd { get; private set; }
        public ulong? LastSellHwnd { get; private set; }
        public ulong? LastContextHwnd { get; private set; }
        public int? ClosedRow { get; private set; }

        public bool IsAvailable(out string error)
        {
            error = Available ? string.Empty : "missing dll";
            return Available;
        }

        public bool IsValidWindow(ulong hwnd, out string error)
        {
            error = hwnd == 0 ? "invalid" : string.Empty;
            return hwnd != 0;
        }

        public bool ClickBuy(ulong chartHwnd, out string error)
        {
            error = string.Empty;
            BuyCalls++;
            LastBuyHwnd = chartHwnd;
            return true;
        }

        public bool ClickSell(ulong chartHwnd, out string error)
        {
            error = string.Empty;
            SellCalls++;
            LastSellHwnd = chartHwnd;
            return true;
        }

        public bool EnsureContextFromParent(ulong parentHwnd, out string error)
        {
            error = string.Empty;
            EnsureContextCalls++;
            LastContextHwnd = parentHwnd;
            return true;
        }

        public bool RecreateContextFromParent(ulong parentHwnd, out string error)
        {
            error = string.Empty;
            RecreateContextCalls++;
            LastContextHwnd = parentHwnd;
            return true;
        }

        public bool ClosePositionMt5(int rowIndex, out string error)
        {
            CloseCalls++;
            if (CloseCalls <= CloseFailuresBeforeSuccess)
            {
                error = "simulated close failure";
                return false;
            }

            error = string.Empty;
            ClosedRow = rowIndex;
            return true;
        }
    }
}
