using PvPArenas.Common.AdminTools.WorldGenManager;
using PvPArenas.Common.Game;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Terraria.Chat;
using Terraria.Enums;
using Terraria.ID;
using Terraria.Localization;

namespace PvPArenas.Common.Generation;

/// <summary>Runs native Jungle passes in private server buffers, then publishes mirrored arena strips.</summary>
internal static class MirroredJungleGenerator
{
    private const int ColumnsPerStep = 16;
    private const int Surface = 55, Rock = 100;
    private static readonly string[] JunglePasses =
    [
        "Small Holes", "Dirt Layer Caves", "Rock Layer Caves", "Surface Caves",
        "Jungle", "Mud Caves To Grass", "Wet Jungle", "Settle Liquids",
        "Smooth World", "Muds Walls In Jungle"
    ];

    internal static ArenaLayout CreateLayout()
    {
        int width = Main.maxTilesX;
        Point red = new(width * 26 / 100, 276);
        // Stay below the surface and above the compact world's UnderworldLayer (400).
        return new ArenaLayout(new Rectangle(60, 75, width - 120, 310),
            new Point(width - 1 - red.X, red.Y), red)
        {
            BossSpawn = new Point(width / 2, 230)
        };
    }

    /// <summary>Empty results mean the native worker is still running; nonempty results need framing and sync.</summary>
    internal static IEnumerable<Rectangle> Generate(int seed, Rectangle protectedLobby)
    {
        if (Main.netMode != NetmodeID.Server)
            yield break;
        bool active = true;
        using WorldGenProgressLog trace = new("Plantera arena", seed, Main.maxTilesX, Main.maxTilesY,
            (message, stalled) => Main.QueueMainThreadAction(() =>
            {
                if (active && Main.netMode == NetmodeID.Server)
                    ChatHelper.BroadcastChatMessage(NetworkText.FromLiteral(message), stalled ? Color.OrangeRed : Color.LightGreen);
            }));
        try
        {
            using IEnumerator<Rectangle> steps = GenerateCore(seed, protectedLobby, trace).GetEnumerator();
            while (true)
            {
                bool next;
                try { next = steps.MoveNext(); }
                catch (Exception error) { trace.Fail(error); throw; }
                if (!next)
                {
                    if (Main.netMode == NetmodeID.Server) trace.Complete();
                    yield break;
                }
                yield return steps.Current;
            }
        }
        finally { active = false; }
    }

    // ArenaPreparation owns this trace until its final framing and client synchronization finish.
    internal static IEnumerable<Rectangle> GenerateWithProgress(int seed, Rectangle protectedLobby, WorldGenProgressLog trace)
    {
        if (Main.netMode != NetmodeID.Server) yield break;
        foreach (Rectangle area in GenerateCore(seed, protectedLobby, trace)) yield return area;
    }

    private static IEnumerable<Rectangle> GenerateCore(int seed, Rectangle protectedLobby, WorldGenProgressLog trace)
    {
        trace.Report("Loading native generator", 0);
        if (Main.maxTilesX != ArenaWorldSystem.Width || Main.maxTilesY != ArenaWorldSystem.Height)
            throw new InvalidOperationException("The Jungle generator requires the compact arena world.");
        if (!NativeLibraryLoader.IsLoaded
            && !NativeLibraryLoader.TryLoad(ModContent.GetInstance<PvPArenas>(), out string error))
            throw new InvalidOperationException(error);

        int width = Main.maxTilesX, height = Main.maxTilesY, half = (width + 1) / 2;
        ArenaLayout layout = CreateLayout();
        Rectangle sourceHalf = new(0, 0, half, height);
        Rectangle reflectedLobby = new(width - protectedLobby.Right, protectedLobby.Y,
            protectedLobby.Width, protectedLobby.Height);
        Rectangle left = Rectangle.Intersect(protectedLobby, sourceHalf);
        Rectangle right = Rectangle.Intersect(reflectedLobby, sourceHalf);
        Rectangle excluded = left.IsEmpty ? right : right.IsEmpty ? left : Rectangle.Union(left, right);

        NativeWorldGenSession session = null;
        Task worker = null;
        try
        {
            NativePassProgress nativeProgress = new(trace);
            trace.Report("Creating native session", 0);
            session = new(seed, 3, width, height, 1, Main.GameMode, (_, _, value, pass, message) =>
                nativeProgress.Report(pass, message == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(message)));
            trace.Report("Capturing world tiles", 0);
            session.BindTileArrays(); // Capture and pin private arrays on the server thread.
            worker = Task.Run(() => BuildJungle(session, width, trace, nativeProgress));
            while (!worker.IsCompleted)
            {
                if (Main.netMode != NetmodeID.Server)
                    yield break;
                yield return Rectangle.Empty;
            }
            worker.GetAwaiter().GetResult();
            if (Main.netMode != NetmodeID.Server)
                yield break;

            // Only the two layer anchors belong to this arena; preserve world identity, spawn, and game mode.
            Main.worldSurface = Surface;
            Main.rockLayer = Rock;
            Liquid.ReInit();
            ArenaTemplate.ClearEntitiesOutside(protectedLobby);
            trace.Report("Publishing mirrored native terrain", 0);
            for (int start = 0; start < half; start += ColumnsPerStep)
            {
                if (Main.netMode != NetmodeID.Server)
                    yield break;
                int end = Math.Min(half, start + ColumnsPerStep);
                Rectangle strip = new(start, 0, end - start, height);
                session.CommitTiles(strip, excluded);
                for (int x = start; x < end; x++)
                for (int y = 0; y < height; y++)
                {
                    int reflectedX = width - 1 - x;
                    bool leftProtected = protectedLobby.Contains(x, y);
                    bool rightProtected = protectedLobby.Contains(reflectedX, y);
                    if (leftProtected || rightProtected)
                    {
                        if (leftProtected != rightProtected)
                            CopyReflected(leftProtected ? x : reflectedX, leftProtected ? reflectedX : x, y);
                        continue;
                    }

                    // Native furniture has no managed chest/entity records. Real landmarks are placed last.
                    Tile tile = Main.tile[x, y];
                    if (tile.HasTile && Main.tileFrameImportant[tile.TileType]) tile.ClearTile();
                    if (reflectedX != x)
                        CopyReflected(x, reflectedX, y);
                }
                trace.Report("Publishing mirrored native terrain", end / (double)half);
                yield return strip;
                yield return new Rectangle(width - end, 0, end - start, height);
            }

            // Inspect the complete native terrain before shaping winding caves around the lobby.
            // Keep planning separate from publication so no unfinished strip influences the layout.
            if (Main.netMode != NetmodeID.Server)
                yield break;
            trace.Report("Planning connected jungle caves", 0);
            JungleArenaPlan plan = new(seed, layout, excluded);
            trace.Report("Carving mirrored caves", 0);
            for (int start = 0; start < half; start += ColumnsPerStep)
            {
                if (Main.netMode != NetmodeID.Server)
                    yield break;
                int end = Math.Min(half, start + ColumnsPerStep);
                for (int x = start; x < end; x++)
                for (int y = layout.ArenaBounds.Top; y < layout.ArenaBounds.Bottom; y++)
                {
                    int reflectedX = width - 1 - x;
                    if (protectedLobby.Contains(x, y) || protectedLobby.Contains(reflectedX, y))
                        continue;
                    plan.ApplyTerrain(x, y);
                    CopyReflected(x, reflectedX, y);
                }
                trace.Report("Carving mirrored caves", end / (double)half);
                // Full-height dirty strips share the existing framing/sync contract.
                yield return new Rectangle(start, 0, end - start, height);
                yield return new Rectangle(width - end, 0, end - start, height);
            }

            // Multitile objects are independently placed with canonical frames on each side.
            // No carving, furniture stripping or raw mirroring follows these passes.
            trace.Report("Placing jungle landmarks", 0);
            int landmarks = 0;
            foreach (Rectangle changed in JungleArenaStructures.Place(seed, layout, protectedLobby, plan.Chambers))
            {
                trace.Report("Placing jungle landmarks", ++landmarks / (double)(plan.Chambers.Count * 2));
                yield return changed;
            }
            trace.Report("Growing jungle foliage", 0);
            foreach (Rectangle changed in JungleArenaFoliage.Place(seed, layout, protectedLobby))
                yield return changed;
            trace.Report("Growing jungle foliage", 1);
        }
        finally
        {
            session?.DisposeWhenCompleted(worker);
        }
    }

    private static void BuildJungle(NativeWorldGenSession session, int width, WorldGenProgressLog trace, NativePassProgress nativeProgress)
    {
        trace.Report("Initializing native generator", 0);
        session.Initialize();
        nativeProgress.Begin(["Reset", "Terrain"]);
        Run(session, ["Reset", "Terrain"]);
        // Reset/Terrain overwrite these fields, so configure the compact jungle only after they finish.
        trace.Report("Configuring compact jungle", 0);
        session.SetField("WorldSurface", Surface);
        session.SetField("GenWorldSurface", Surface);
        session.SetField("GenWorldSurfaceLow", Surface - 10);
        session.SetField("GenWorldSurfaceHigh", Surface + 10);
        session.SetField("RockLayer", Rock);
        session.SetField("GenRockLayer", Rock);
        session.SetField("GenRockLayerLow", Rock - 15);
        session.SetField("GenRockLayerHigh", Rock + 15);
        session.SetField("GenJungleOriginX", width * 40 / 100);
        session.SetField("GenLeftBeachEnd", 20);
        session.SetField("GenRightBeachStart", width - 1);
        session.SetField("GenDungeonSide", 1);
        session.SetField("GenWaterLine", 320);
        session.SetField("GenLavaLine", 550);
        nativeProgress.Begin(JunglePasses);
        Run(session, JunglePasses);
        trace.Report("Reading native jungle tiles", 0);
        session.SyncToTml(); // Native output goes to the session's private arrays, never the live tilemap.
    }

    private static void Run(NativeWorldGenSession session, string[] passes)
    {
        WgResult result = session.RunPasses(passes);
        if (result != WgResult.Ok)
            throw new InvalidOperationException("WorldGen++ Jungle generation failed: " + result);
    }

    private static void CopyReflected(int sourceX, int destinationX, int y)
    {
        Tile source = Main.tile[sourceX, y];
        Tile target = Main.tile[destinationX, y];
        target.CopyFrom(source);
        target.Slope = source.Slope switch
        {
            SlopeType.SlopeDownLeft => SlopeType.SlopeDownRight,
            SlopeType.SlopeDownRight => SlopeType.SlopeDownLeft,
            SlopeType.SlopeUpLeft => SlopeType.SlopeUpRight,
            SlopeType.SlopeUpRight => SlopeType.SlopeUpLeft,
            _ => source.Slope
        };
        if (source.HasTile && Main.tileFrameImportant[source.TileType])
            target.ClearTile();
    }
}
