using LatencyArbTool.Core.Models;
using LatencyArbTool.Core.Services;

namespace LatencyArbTool.Tests;

public sealed class SharedMemoryHistoryReaderTests
{
    [Fact]
    public void Parse_ReturnsEmptyHistoryForCountZero()
    {
        var bytes = Header(count: 0, eaMs: 1234);

        var result = SharedMemoryHistoryReader.Parse("Local\\MT_B_History", bytes);

        Assert.True(result.Success);
        Assert.Equal(1234UL, result.EaTickCountMs);
        Assert.Empty(result.History);
    }

    [Fact]
    public void Parse_ReadsOneHistoryRecord()
    {
        var bytes = Header(count: 1, eaMs: 1234);
        WriteHistory(bytes, 16, ticket: 42, TradeSide.Sell, volume: 1.25, openPrice: 2020.5, closePrice: 2021.5, profit: -4.5, symbol: "XAUUSD");

        var result = SharedMemoryHistoryReader.Parse("Local\\MT_B_History", bytes);

        Assert.True(result.Success);
        var history = Assert.Single(result.History);
        Assert.Equal(42UL, history.Ticket);
        Assert.Equal(TradeSide.Sell, history.Side);
        Assert.Equal(1.25, history.Volume);
        Assert.Equal(2020.5, history.OpenPrice);
        Assert.Equal(2021.5, history.ClosePrice);
        Assert.Equal(-4.5, history.Profit);
        Assert.Equal("XAUUSD", history.Symbol);
    }

    [Fact]
    public void Parse_RejectsInvalidCount()
    {
        var bytes = Header(count: 1000, eaMs: 1234);

        var result = SharedMemoryHistoryReader.Parse("Local\\MT_B_History", bytes);

        Assert.False(result.Success);
        Assert.Contains("invalid history count", result.Error);
    }

    private static byte[] Header(int count, ulong eaMs)
    {
        var bytes = new byte[16384];
        BitConverter.GetBytes(count).CopyTo(bytes, 0);
        BitConverter.GetBytes(eaMs).CopyTo(bytes, 4);
        return bytes;
    }

    private static void WriteHistory(
        byte[] bytes,
        int offset,
        ulong ticket,
        TradeSide side,
        double volume,
        double openPrice,
        double closePrice,
        double profit,
        string symbol)
    {
        BitConverter.GetBytes(ticket).CopyTo(bytes, offset);
        BitConverter.GetBytes(side == TradeSide.Buy ? 0 : 1).CopyTo(bytes, offset + 8);
        BitConverter.GetBytes(volume).CopyTo(bytes, offset + 12);
        BitConverter.GetBytes(openPrice).CopyTo(bytes, offset + 20);
        BitConverter.GetBytes(closePrice).CopyTo(bytes, offset + 28);
        BitConverter.GetBytes(0d).CopyTo(bytes, offset + 36);
        BitConverter.GetBytes(0d).CopyTo(bytes, offset + 44);
        BitConverter.GetBytes(0d).CopyTo(bytes, offset + 52);
        BitConverter.GetBytes(profit).CopyTo(bytes, offset + 60);
        BitConverter.GetBytes(1UL).CopyTo(bytes, offset + 68);
        BitConverter.GetBytes(2UL).CopyTo(bytes, offset + 76);
        BitConverter.GetBytes(3UL).CopyTo(bytes, offset + 84);
        WriteSymbol(bytes, offset + 92, symbol);
    }

    private static void WriteSymbol(byte[] bytes, int offset, string symbol)
    {
        var symbolBytes = System.Text.Encoding.UTF8.GetBytes(symbol);
        Array.Copy(symbolBytes, 0, bytes, offset, symbolBytes.Length);
    }
}
