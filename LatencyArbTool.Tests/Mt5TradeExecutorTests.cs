using LatencyArbTool.Core.Models;
using LatencyArbTool.Core.Services;

namespace LatencyArbTool.Tests;

public sealed class Mt5TradeExecutorTests
{
    [Fact]
    public void Execute_LiveDisabledDoesNotCallNative()
    {
        var gateway = new FakeGateway();
        var executor = new Mt5TradeExecutor(gateway);

        var result = executor.Execute(Event("dry open", DryRunSide.BuyB), liveMode: false, "123");

        Assert.False(result.Attempted);
        Assert.Equal(0, gateway.BuyCalls);
    }

    [Fact]
    public void Execute_InvalidHwndDoesNotCallNative()
    {
        var gateway = new FakeGateway();
        var executor = new Mt5TradeExecutor(gateway);

        var result = executor.Execute(Event("dry open", DryRunSide.BuyB), liveMode: true, "0");

        Assert.True(result.Attempted);
        Assert.False(result.Success);
        Assert.Equal(0, gateway.BuyCalls);
    }

    [Fact]
    public void Execute_DryOpenBuyClicksBuy()
    {
        var gateway = new FakeGateway();
        var executor = new Mt5TradeExecutor(gateway);

        var result = executor.Execute(Event("dry open", DryRunSide.BuyB), liveMode: true, "0x3039");

        Assert.True(result.Success);
        Assert.Equal(1, gateway.BuyCalls);
        Assert.Equal(0, gateway.SellCalls);
    }

    [Fact]
    public void Execute_DryOpenSellClicksSell()
    {
        var gateway = new FakeGateway();
        var executor = new Mt5TradeExecutor(gateway);

        var result = executor.Execute(Event("dry open", DryRunSide.SellB), liveMode: true, "12345");

        Assert.True(result.Success);
        Assert.Equal(0, gateway.BuyCalls);
        Assert.Equal(1, gateway.SellCalls);
    }

    [Fact]
    public void Execute_DryCloseClosesRowZero()
    {
        var gateway = new FakeGateway();
        var executor = new Mt5TradeExecutor(gateway);

        var result = executor.Execute(Event("dry close", DryRunSide.BuyB), liveMode: true, "12345");

        Assert.True(result.Success);
        Assert.Equal(1, gateway.EnsureContextCalls);
        Assert.Equal(0, gateway.ClosedRow);
    }

    [Fact]
    public void Execute_NativeUnavailableFailsWithoutCallingActions()
    {
        var gateway = new FakeGateway { Available = false };
        var executor = new Mt5TradeExecutor(gateway);

        var result = executor.Execute(Event("dry open", DryRunSide.BuyB), liveMode: true, "12345");

        Assert.True(result.Attempted);
        Assert.False(result.Success);
        Assert.Equal(0, gateway.BuyCalls);
    }

    private static DryRunEvent Event(string decision, DryRunSide side)
    {
        return new DryRunEvent(decision, "test", BotState.Holding, 1, Side: side);
    }

    private sealed class FakeGateway : IMt5TradeGateway
    {
        public bool Available { get; init; } = true;
        public int BuyCalls { get; private set; }
        public int SellCalls { get; private set; }
        public int EnsureContextCalls { get; private set; }
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
            return true;
        }

        public bool ClickSell(ulong chartHwnd, out string error)
        {
            error = string.Empty;
            SellCalls++;
            return true;
        }

        public bool EnsureContextFromParent(ulong parentHwnd, out string error)
        {
            error = string.Empty;
            EnsureContextCalls++;
            return true;
        }

        public bool ClosePositionMt5(int rowIndex, out string error)
        {
            error = string.Empty;
            ClosedRow = rowIndex;
            return true;
        }
    }
}

