using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;

namespace LatencyArbTool.Core.Services;

// App -> EA control channel over the Ctrl shared memory map (created by the EA).
// Not a trade command (no AutoTrading needed): the only signal is a reset
// sequence the app bumps at Start so the EA re-baselines its history export to
// this session (map then holds only session deals). Layout: int32 resetSeq @0.
[SupportedOSPlatform("windows")]
public sealed class SharedMemoryControlWriter
{
    private const int MapSize = 16;

    public bool TryBumpResetSeq(string mapName, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(mapName))
        {
            error = "ctrl map name is empty";
            return false;
        }

        try
        {
            using var mmf = MemoryMappedFile.OpenExisting(mapName, MemoryMappedFileRights.ReadWrite);
            using var accessor = mmf.CreateViewAccessor(0, MapSize, MemoryMappedFileAccess.ReadWrite);
            var current = accessor.ReadInt32(0);
            accessor.Write(0, current <= 0 ? 1 : current + 1);
            return true;
        }
        catch (FileNotFoundException)
        {
            error = "ctrl map not found (EA cũ chưa có control channel)";
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            error = ex.Message;
            return false;
        }
    }
}
