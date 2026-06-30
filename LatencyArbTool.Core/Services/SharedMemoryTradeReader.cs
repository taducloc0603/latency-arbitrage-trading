using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;
using System.Text;
using LatencyArbTool.Core.Models;

namespace LatencyArbTool.Core.Services;

public sealed class SharedMemoryTradeReader
{
    private const int MapSize = 4096;
    private const int HeaderSize = 16;
    private const int RecordSize = 100;
    private const int MaxRecords = (MapSize - HeaderSize) / RecordSize;

    [SupportedOSPlatform("windows")]
    public bool MapExists(string mapName)
    {
        if (string.IsNullOrWhiteSpace(mapName))
        {
            return false;
        }

        try
        {
            using var _ = MemoryMappedFile.OpenExisting(mapName);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    [SupportedOSPlatform("windows")]
    public string ResolveMapNameForTickMap(string tickMapName)
    {
        var primary = SharedMemoryMapNames.TradeFromTick(tickMapName);
        return MapExists(primary) ? primary : SharedMemoryMapNames.TradesFallbackFromTick(tickMapName);
    }

    [SupportedOSPlatform("windows")]
    public bool MapExistsForTickMap(string tickMapName)
    {
        return MapExists(SharedMemoryMapNames.TradeFromTick(tickMapName)) ||
            MapExists(SharedMemoryMapNames.TradesFallbackFromTick(tickMapName));
    }

    [SupportedOSPlatform("windows")]
    public TradeReadResult TryReadForTickMap(string tickMapName)
    {
        var primary = SharedMemoryMapNames.TradeFromTick(tickMapName);
        var fallback = SharedMemoryMapNames.TradesFallbackFromTick(tickMapName);
        return MapExists(primary) || primary == fallback ? TryRead(primary) : TryRead(fallback);
    }

    [SupportedOSPlatform("windows")]
    public TradeReadResult TryRead(string mapName)
    {
        if (string.IsNullOrWhiteSpace(mapName))
        {
            return TradeReadResult.Fail(mapName, "map name is empty");
        }

        try
        {
            using var mmf = MemoryMappedFile.OpenExisting(mapName);
            using var accessor = mmf.CreateViewAccessor(0, MapSize, MemoryMappedFileAccess.Read);
            var bytes = new byte[MapSize];
            accessor.ReadArray(0, bytes, 0, bytes.Length);
            return Parse(mapName, bytes);
        }
        catch (FileNotFoundException)
        {
            return TradeReadResult.Fail(mapName, "map not found");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return TradeReadResult.Fail(mapName, ex.Message);
        }
    }

    public static TradeReadResult Parse(string mapName, ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderSize)
        {
            return TradeReadResult.Fail(mapName, "map data too short");
        }

        var count = BitConverter.ToInt32(bytes[..4]);
        var eaTickCountMs = BitConverter.ToUInt64(bytes.Slice(4, 8));
        if (count < 0 || count > MaxRecords || HeaderSize + count * RecordSize > bytes.Length)
        {
            return TradeReadResult.Fail(mapName, $"invalid trade count {count}");
        }

        var trades = new List<TradeRecord>(count);
        var offset = HeaderSize;
        for (var i = 0; i < count; i++, offset += RecordSize)
        {
            var ticket = BitConverter.ToUInt64(bytes.Slice(offset, 8));
            var lot = BitConverter.ToDouble(bytes.Slice(offset + 8, 8));
            var price = BitConverter.ToDouble(bytes.Slice(offset + 16, 8));
            var sl = BitConverter.ToDouble(bytes.Slice(offset + 24, 8));
            var tp = BitConverter.ToDouble(bytes.Slice(offset + 32, 8));
            var profit = BitConverter.ToDouble(bytes.Slice(offset + 40, 8));
            var tradeType = BitConverter.ToInt32(bytes.Slice(offset + 48, 4));
            var timeMsc = BitConverter.ToUInt64(bytes.Slice(offset + 52, 8));
            var openEaTimeLocal = BitConverter.ToUInt64(bytes.Slice(offset + 60, 8));
            var symbol = ReadSymbol(bytes.Slice(offset + 68, 32));

            if (!TryGetSide(tradeType, out var side) ||
                !IsValidPositive(price) ||
                !IsValidNonNegative(lot) ||
                !IsFinite(sl) ||
                !IsFinite(tp) ||
                !IsFinite(profit))
            {
                continue;
            }

            trades.Add(new TradeRecord(ticket, side, lot, price, sl, tp, profit, timeMsc, openEaTimeLocal, symbol));
        }

        return TradeReadResult.Ok(mapName, eaTickCountMs, trades);
    }

    private static bool TryGetSide(int tradeType, out TradeSide side)
    {
        side = tradeType == 0 ? TradeSide.Buy : TradeSide.Sell;
        return tradeType is 0 or 1;
    }

    private static string ReadSymbol(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end >= 0 ? bytes[..end] : bytes).TrimEnd(' ');
    }

    private static bool IsValidPositive(double value)
    {
        return IsFinite(value) && value > 0;
    }

    private static bool IsValidNonNegative(double value)
    {
        return IsFinite(value) && value >= 0;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

public sealed record TradeReadResult(
    string MapName,
    ulong EaTickCountMs,
    IReadOnlyList<TradeRecord> Trades,
    string? Error)
{
    public bool Success => Error is null;
    public int Count => Trades.Count;

    public static TradeReadResult Ok(string mapName, ulong eaTickCountMs, IReadOnlyList<TradeRecord> trades)
    {
        return new TradeReadResult(mapName, eaTickCountMs, trades, null);
    }

    public static TradeReadResult Fail(string mapName, string error)
    {
        return new TradeReadResult(mapName, 0, Array.Empty<TradeRecord>(), error);
    }
}
