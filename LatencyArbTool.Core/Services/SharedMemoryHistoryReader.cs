using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;
using System.Text;
using LatencyArbTool.Core.Models;

namespace LatencyArbTool.Core.Services;

public sealed class SharedMemoryHistoryReader
{
    private const int MapSize = 16384;
    private const int HeaderSize = 16;
    private const int RecordSize = 124;
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
    public bool MapExistsForTickMap(string tickMapName)
    {
        return MapExists(SharedMemoryMapNames.HistoryFromTick(tickMapName));
    }

    [SupportedOSPlatform("windows")]
    public HistoryReadResult TryReadForTickMap(string tickMapName)
    {
        return TryRead(SharedMemoryMapNames.HistoryFromTick(tickMapName));
    }

    [SupportedOSPlatform("windows")]
    public HistoryReadResult TryRead(string mapName)
    {
        if (string.IsNullOrWhiteSpace(mapName))
        {
            return HistoryReadResult.Fail(mapName, "map name is empty");
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
            return HistoryReadResult.Fail(mapName, "map not found");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return HistoryReadResult.Fail(mapName, ex.Message);
        }
    }

    public static HistoryReadResult Parse(string mapName, ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderSize)
        {
            return HistoryReadResult.Fail(mapName, "map data too short");
        }

        var count = BitConverter.ToInt32(bytes[..4]);
        var eaTickCountMs = BitConverter.ToUInt64(bytes.Slice(4, 8));
        if (count < 0 || count > MaxRecords || HeaderSize + count * RecordSize > bytes.Length)
        {
            return HistoryReadResult.Fail(mapName, $"invalid history count {count}");
        }

        var history = new List<HistoryRecord>(count);
        var offset = HeaderSize;
        for (var i = 0; i < count; i++)
        {
            var ticket = BitConverter.ToUInt64(bytes.Slice(offset, 8));
            var tradeType = BitConverter.ToInt32(bytes.Slice(offset + 8, 4));
            var volume = BitConverter.ToDouble(bytes.Slice(offset + 12, 8));
            var openPrice = BitConverter.ToDouble(bytes.Slice(offset + 20, 8));
            var closePrice = BitConverter.ToDouble(bytes.Slice(offset + 28, 8));
            var sl = BitConverter.ToDouble(bytes.Slice(offset + 36, 8));
            var tp = BitConverter.ToDouble(bytes.Slice(offset + 44, 8));
            var commission = BitConverter.ToDouble(bytes.Slice(offset + 52, 8));
            var profit = BitConverter.ToDouble(bytes.Slice(offset + 60, 8));
            var openTimeMsc = BitConverter.ToUInt64(bytes.Slice(offset + 68, 8));
            var closeTimeMsc = BitConverter.ToUInt64(bytes.Slice(offset + 76, 8));
            var closeEaTimeLocal = BitConverter.ToUInt64(bytes.Slice(offset + 84, 8));
            var symbol = ReadSymbol(bytes.Slice(offset + 92, 32));

            if (!TryGetSide(tradeType, out var side) ||
                !IsValidNonNegative(volume) ||
                !IsValidPositive(openPrice) ||
                !IsValidPositive(closePrice) ||
                !IsFinite(sl) ||
                !IsFinite(tp) ||
                !IsFinite(commission) ||
                !IsFinite(profit))
            {
                return HistoryReadResult.Fail(mapName, $"invalid history record {i}");
            }

            history.Add(new HistoryRecord(
                ticket,
                side,
                volume,
                openPrice,
                closePrice,
                sl,
                tp,
                commission,
                profit,
                openTimeMsc,
                closeTimeMsc,
                closeEaTimeLocal,
                symbol));
            offset += RecordSize;
        }

        return HistoryReadResult.Ok(mapName, eaTickCountMs, history);
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

public sealed record HistoryReadResult(
    string MapName,
    ulong EaTickCountMs,
    IReadOnlyList<HistoryRecord> History,
    string? Error)
{
    public bool Success => Error is null;
    public int Count => History.Count;

    public static HistoryReadResult Ok(string mapName, ulong eaTickCountMs, IReadOnlyList<HistoryRecord> history)
    {
        return new HistoryReadResult(mapName, eaTickCountMs, history, null);
    }

    public static HistoryReadResult Fail(string mapName, string error)
    {
        return new HistoryReadResult(mapName, 0, Array.Empty<HistoryRecord>(), error);
    }
}
