using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;

namespace LatencyArbTool.Core.Services;

// App -> EA command channel over the Cmd shared memory map (created by the EA).
// Single command slot dispatched by opcode. The app writes payload + opcode then
// bumps cmd_seq; the EA acks with ack_seq/ack_result.
//
// Layout (little-endian, mirrors CommandMemory.mqh):
//   0  int32  cmd_seq
//   4  int32  opcode      (1 = set SL, 2 = reset history)
//   8  ulong  ticket
//   16 double sl_price
//   24 int32  ack_seq
//   28 int32  ack_result (1 = OK)
//   32 int32  ack_retcode
[SupportedOSPlatform("windows")]
public sealed class SharedMemoryCommandWriter
{
    private const int MapSize = 64;
    private const int OpSetSl = 1;
    private const int OpResetHistory = 2;

    public bool TryWriteSetSl(string mapName, ulong ticket, double slPrice, out int seq, out string error)
    {
        return TryWrite(mapName, OpSetSl, accessor =>
        {
            accessor.Write(8, ticket);
            accessor.Write(16, slPrice);
        }, out seq, out error);
    }

    // Tells the EA to start a new session: it re-baselines the history map so
    // only deals closing after this call are exported. No payload.
    public bool TryWriteResetHistory(string mapName, out int seq, out string error)
    {
        return TryWrite(mapName, OpResetHistory, _ => { }, out seq, out error);
    }

    private static bool TryWrite(string mapName, int opcode, Action<MemoryMappedViewAccessor> writePayload, out int seq, out string error)
    {
        seq = 0;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(mapName))
        {
            error = "cmd map name is empty";
            return false;
        }

        try
        {
            using var mmf = MemoryMappedFile.OpenExisting(mapName, MemoryMappedFileRights.ReadWrite);
            using var accessor = mmf.CreateViewAccessor(0, MapSize, MemoryMappedFileAccess.ReadWrite);

            var current = accessor.ReadInt32(0);
            seq = current <= 0 ? 1 : current + 1;

            // Payload + opcode first, sequence last: the EA only reacts to a seq change.
            accessor.Write(4, opcode);
            writePayload(accessor);
            accessor.Write(0, seq);
            return true;
        }
        catch (FileNotFoundException)
        {
            error = "cmd map not found (EA not running?)";
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            error = ex.Message;
            return false;
        }
    }

    public CommandAck? TryReadAck(string mapName)
    {
        if (string.IsNullOrWhiteSpace(mapName))
        {
            return null;
        }

        try
        {
            using var mmf = MemoryMappedFile.OpenExisting(mapName);
            using var accessor = mmf.CreateViewAccessor(0, MapSize, MemoryMappedFileAccess.Read);
            return new CommandAck(
                Seq: accessor.ReadInt32(24),
                Ok: accessor.ReadInt32(28) == 1,
                Retcode: accessor.ReadInt32(32));
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}

public readonly record struct CommandAck(int Seq, bool Ok, int Retcode);
