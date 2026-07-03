using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;

namespace LatencyArbTool.Core.Services;

// App -> EA command channel over the Cmd shared memory map (created by the EA).
// Single command slot; currently the only command is "set hard SL for ticket".
// The app writes payload then bumps cmd_seq; the EA acks with ack_seq/ack_result.
//
// Layout (little-endian, mirrors CommandMemory.mqh):
//   0  int32  cmd_seq
//   8  ulong  ticket
//   16 double sl_price
//   24 int32  ack_seq
//   28 int32  ack_result (1 = OK)
//   32 int32  ack_retcode
[SupportedOSPlatform("windows")]
public sealed class SharedMemoryCommandWriter
{
    private const int MapSize = 64;

    public bool TryWriteSetSl(string mapName, ulong ticket, double slPrice, out int seq, out string error)
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

            // Payload first, sequence last: the EA only reacts to a seq change.
            accessor.Write(8, ticket);
            accessor.Write(16, slPrice);
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
