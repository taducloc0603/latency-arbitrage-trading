using LatencyArbTool.Core.Models;
using LatencyArbTool.Core.Services;

namespace LatencyArbTool.Tests;

public sealed class SharedMemoryTradeReaderTests
{
    [Fact]
    public void Parse_ReturnsEmptyTradesForCountZero()
    {
        var bytes = Header(count: 0, eaMs: 1234);

        var result = SharedMemoryTradeReader.Parse("Local\\MT_B_Trade", bytes);

        Assert.True(result.Success);
        Assert.Equal(1234UL, result.EaTickCountMs);
        Assert.Empty(result.Trades);
    }

    [Fact]
    public void Parse_ReadsOneTradeAndTrimsSymbol()
    {
        var bytes = Header(count: 1, eaMs: 1234);
        WriteTrade(bytes, 16, ticket: 42, TradeSide.Buy, lot: 1.25, price: 2020.5, profit: 7.5, symbol: "XAUUSD");

        var result = SharedMemoryTradeReader.Parse("Local\\MT_B_Trade", bytes);

        Assert.True(result.Success);
        var trade = Assert.Single(result.Trades);
        Assert.Equal(42UL, trade.Ticket);
        Assert.Equal(TradeSide.Buy, trade.Side);
        Assert.Equal(1.25, trade.Lot);
        Assert.Equal(2020.5, trade.Price);
        Assert.Equal(7.5, trade.Profit);
        Assert.Equal("XAUUSD", trade.Symbol);
    }

    [Fact]
    public void Parse_ReadsMultipleTrades()
    {
        var bytes = Header(count: 2, eaMs: 1234);
        WriteTrade(bytes, 16, ticket: 1, TradeSide.Buy, lot: 1, price: 2000, profit: 1, symbol: "A");
        WriteTrade(bytes, 116, ticket: 2, TradeSide.Sell, lot: 2, price: 2001, profit: -1, symbol: "B");

        var result = SharedMemoryTradeReader.Parse("Local\\MT_B_Trade", bytes);

        Assert.True(result.Success);
        Assert.Equal(2, result.Count);
        Assert.Equal(TradeSide.Sell, result.Trades[1].Side);
    }

    [Fact]
    public void Parse_RejectsInvalidCount()
    {
        var bytes = Header(count: 1000, eaMs: 1234);

        var result = SharedMemoryTradeReader.Parse("Local\\MT_B_Trade", bytes);

        Assert.False(result.Success);
        Assert.Contains("invalid trade count", result.Error);
    }

    [Fact]
    public void Parse_RejectsInvalidPrice()
    {
        var bytes = Header(count: 1, eaMs: 1234);
        WriteTrade(bytes, 16, ticket: 42, TradeSide.Buy, lot: 1, price: -1, profit: 0, symbol: "XAUUSD");

        var result = SharedMemoryTradeReader.Parse("Local\\MT_B_Trade", bytes);

        Assert.False(result.Success);
        Assert.Contains("invalid trade record", result.Error);
    }

    [Fact]
    public void MapNameResolver_DerivesBTradeAndFallbackNames()
    {
        Assert.Equal(@"Local\MT_B_Trade", SharedMemoryMapNames.TradeFromTick(@"Local\MT_B_Tick"));
        Assert.Equal(@"Local\MT_B_Trades", SharedMemoryMapNames.TradesFallbackFromTick(@"Local\MT_B_Tick"));
    }

    private static byte[] Header(int count, ulong eaMs)
    {
        var bytes = new byte[4096];
        BitConverter.GetBytes(count).CopyTo(bytes, 0);
        BitConverter.GetBytes(eaMs).CopyTo(bytes, 4);
        return bytes;
    }

    private static void WriteTrade(
        byte[] bytes,
        int offset,
        ulong ticket,
        TradeSide side,
        double lot,
        double price,
        double profit,
        string symbol)
    {
        BitConverter.GetBytes(ticket).CopyTo(bytes, offset);
        BitConverter.GetBytes(lot).CopyTo(bytes, offset + 8);
        BitConverter.GetBytes(price).CopyTo(bytes, offset + 16);
        BitConverter.GetBytes(0d).CopyTo(bytes, offset + 24);
        BitConverter.GetBytes(0d).CopyTo(bytes, offset + 32);
        BitConverter.GetBytes(profit).CopyTo(bytes, offset + 40);
        BitConverter.GetBytes(side == TradeSide.Buy ? 0 : 1).CopyTo(bytes, offset + 48);
        BitConverter.GetBytes(1UL).CopyTo(bytes, offset + 52);
        BitConverter.GetBytes(2UL).CopyTo(bytes, offset + 60);
        WriteSymbol(bytes, offset + 68, symbol);
    }

    private static void WriteSymbol(byte[] bytes, int offset, string symbol)
    {
        var symbolBytes = System.Text.Encoding.UTF8.GetBytes(symbol);
        Array.Copy(symbolBytes, 0, bytes, offset, symbolBytes.Length);
    }
}
