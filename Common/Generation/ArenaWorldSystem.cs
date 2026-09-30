using PvPArenas.Common.Game;
using System;
using System.IO;
using Terraria.ID;
using Terraria.IO;

namespace PvPArenas.Common.Generation;

/// <summary>Loads a fresh disposable arena before players connect; joins and rounds never resize it.</summary>
internal sealed class ArenaWorldSystem : ModSystem
{
    internal const string WorldAsset = "Core/WorldFiles/Arenas_v10.wld";
    internal const int Width = 850;
    internal const int Height = 600;
    internal const int Margin = 20;
    internal static Rectangle PlayBounds => new(Margin, Margin, Width - Margin * 2, Height - Margin * 2);
    internal static bool IsCompactWorld => Main.maxTilesX == Width && Main.maxTilesY == Height;

    internal static void ConfigureAuthoredLayers()
    {
        if (Main.netMode != NetmodeID.Server || !IsCompactWorld) return;
        double previousSurface = Main.worldSurface, previousRock = Main.rockLayer;
        // PlantAlch chooses [(rock + height) / 2, height - 20). The bundled
        // 558/588 anchors invert that range in a 600-tile world.
        Main.rockLayer = Math.Min(Main.rockLayer, Height - 42);
        Main.worldSurface = Math.Min(Main.worldSurface, Main.rockLayer - 30);
        Log.Debug($"[worldgen] PASS | Authored layers | Surface: {previousSurface}->{Main.worldSurface} | "
            + $"Rock: {previousRock}->{Main.rockLayer} | Underworld: {Main.UnderworldLayer}");
    }

    private WorldFileData selectedWorld;
    private WorldFileData sessionWorld;

    internal bool IsSessionWorld => sessionWorld != null && ReferenceEquals(Main.ActiveWorldFileData, sessionWorld);

    public override void Load()
    {
        On_WorldFile.LoadWorld += LoadArenaWorld;
        On_WorldFile.SaveWorld_bool_bool += SaveWorld;
    }

    public override void Unload()
    {
        On_WorldFile.LoadWorld -= LoadArenaWorld;
        On_WorldFile.SaveWorld_bool_bool -= SaveWorld;
        ReleaseSession();
    }

    private void LoadArenaWorld(On_WorldFile.orig_LoadWorld orig, bool loadFromCloud)
    {
        if (Main.netMode != NetmodeID.Server)
        {
            orig(loadFromCloud);
            return;
        }

        ReleaseSession();
        selectedWorld = Main.ActiveWorldFileData;
        string directory = Path.Combine(Path.GetTempPath(), "PvPArenas", "Worlds");
        string path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".wld");
        // Even a failed load must keep vanilla's backup recovery away from the host's selected file.
        sessionWorld = new WorldFileData(path, false);
        Main.ActiveWorldFileData = sessionWorld;
        try
        {
            ArenaWorldBackup.Create(selectedWorld);
            Directory.CreateDirectory(directory);
            sessionWorld = CreateSessionWorld(Mod.GetFileBytes(WorldAsset), path);
            Main.ActiveWorldFileData = sessionWorld;
            // Terraria initializes dimensions, layers, entities, and sections through its normal loader.
            orig(false);
        }
        catch (Exception exception)
        {
            WorldFile.LastThrownLoadException = exception;
            WorldGen.loadFailed = true;
            WorldGen.loadSuccess = false;
            Log.Error(exception);
        }
    }

    internal static WorldFileData CreateSessionWorld(byte[] bytes, string path)
    {
        File.WriteAllBytes(path, bytes);
        WorldFileData world = WorldFile.GetAllMetadata(path, false);
        if (world?.IsValid != true || world.WorldSizeX != Width || world.WorldSizeY != Height)
        {
            File.Delete(path);
            throw new InvalidDataException($"Arenas_v10 must be a valid {Width}×{Height} world.");
        }
        return world;
    }

    private void SaveWorld(On_WorldFile.orig_SaveWorld_bool_bool orig, bool useCloudSaving, bool resetTime)
    {
        if (!IsSessionWorld)
            orig(useCloudSaving, resetTime);
    }

    internal static string MissingArena(ArenaKind kind) => kind switch
    {
        ArenaKind.JungleTemple => "The custom Golem arena has not been supplied yet.",
        _ => ""
    };

    public override void OnWorldUnload() => ReleaseSession();

    private void ReleaseSession()
    {
        if (sessionWorld == null)
            return;
        if (IsSessionWorld)
            Main.ActiveWorldFileData = selectedWorld;
        string path = sessionWorld.Path;
        sessionWorld = null;
        selectedWorld = null;
        // This is the exact temporary file created above, never the world selected by the host.
        try { File.Delete(path); }
        catch (IOException exception) { Log.Warn(exception.Message); }
        catch (UnauthorizedAccessException exception) { Log.Warn(exception.Message); }
    }
}
