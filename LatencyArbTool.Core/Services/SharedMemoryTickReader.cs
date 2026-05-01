using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;
using System.Text;
using LatencyArbTool.Core.Models;

namespace LatencyArbTool.Core.Services;

[SupportedOSPlatform("windows")]
public sealed class SharedMemoryTickReader
{
    private const int MapSize = 112;
    private const int SupportedVersion = 1;

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

    public TickReadResult TryRead(string mapName)
    {
        if (string.IsNullOrWhiteSpace(mapName))
        {
            return TickReadResult.Fail("map name is empty");
        }

        try
        {
            using var mmf = MemoryMappedFile.OpenExisting(mapName);
            using var accessor = mmf.CreateViewAccessor(0, MapSize, MemoryMappedFileAccess.Read);

            var version = accessor.ReadInt32(0);
            if (version != SupportedVersion)
            {
                return TickReadResult.Fail($"unsupported version {version}");
            }

            var timestampMs = accessor.ReadInt64(4);
            var bid = accessor.ReadDouble(16);
            var ask = accessor.ReadDouble(24);
            var spread = accessor.ReadDouble(32);
            var tickTimeMsc = accessor.ReadInt64(40);
            var symbolBytes = new byte[64];
            accessor.ReadArray(48, symbolBytes, 0, symbolBytes.Length);
            var symbol = Encoding.UTF8.GetString(symbolBytes).TrimEnd('\0', ' ');

            if (!IsValidPrice(bid) || !IsValidPrice(ask) || spread < 0 || ask < bid)
            {
                return TickReadResult.Fail("invalid price fields");
            }

            return TickReadResult.Ok(new TickRecord(
                version,
                timestampMs,
                bid,
                ask,
                spread,
                tickTimeMsc,
                symbol));
        }
        catch (FileNotFoundException)
        {
            return TickReadResult.Fail("map not found");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return TickReadResult.Fail(ex.Message);
        }
    }

    private static bool IsValidPrice(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
    }
}

public sealed record TickReadResult(TickRecord? Tick, string? Error)
{
    public bool Success => Tick is not null;

    public static TickReadResult Ok(TickRecord tick)
    {
        return new TickReadResult(tick, null);
    }

    public static TickReadResult Fail(string error)
    {
        return new TickReadResult(null, error);
    }
}
