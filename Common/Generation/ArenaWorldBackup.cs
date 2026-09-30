using System;
using System.IO;
using Terraria.ID;
using Terraria.IO;

namespace PvPArenas.Common.Generation;

/// <summary>Keeps the first local world and mod-data backup before the server loads its disposable arena.</summary>
internal static class ArenaWorldBackup
{
    internal const string Suffix = "_PvPArenasMadeThisBackup";

    internal static void Create(WorldFileData world)
    {
        if (Main.netMode != NetmodeID.Server)
            return;
        if (world == null || string.IsNullOrWhiteSpace(world.Path))
            throw new InvalidDataException("No server world was selected to back up.");
        if (world.IsCloudSave)
            throw new NotSupportedException("PvPArenas requires a local server world so its backup can be created safely. Move the world out of cloud storage first.");

        string source = Path.GetFullPath(world.Path);
        string backup = Path.Combine(Path.GetDirectoryName(source)!, Path.GetFileNameWithoutExtension(source) + Suffix + ".wld");
        CopyFirst(source, backup);
        string modData = Path.ChangeExtension(source, ".twld");
        if (File.Exists(modData))
            CopyFirst(modData, Path.ChangeExtension(backup, ".twld"));
    }

    private static void CopyFirst(string source, string destination)
    {
        if (File.Exists(destination))
            return;

        // Publish only a complete copy; failed writes cannot masquerade as an existing backup on retry.
        string pending = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(source, pending, false);
            File.Move(pending, destination, false);
        }
        finally
        {
            if (File.Exists(pending))
                File.Delete(pending);
        }
    }
}
