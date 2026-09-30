using PvPArenas.Common.AdminTools.WorldGenManager;
using PvPArenas.Common.Game;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Terraria.Enums;
using Terraria.ID;

namespace PvPArenas.Common.Generation;

/// <summary>Runs native Jungle passes in private server buffers, then publishes mirrored arena strips.</summary>
internal static class MirroredJungleGenerator
{
    private const int ColumnsPerStep = 16;
    private const int Surface = 110, Rock = 170;
    private static readonly string[] JunglePasses =
    [
        "Small Holes", "Dirt Layer Caves", "Rock Layer Caves", "Surface Caves",
        "Jungle", "Mud Caves To Grass", "Wet Jungle", "Hives", "Settle Liquids",
        "Smooth World", "Muds Walls In Jungle", "Jungle Plants", "Vines", "Planting Trees"
    ];

    internal static ArenaLayout CreateLayout()
    {
        int width = Main.maxTilesX, height = Main.maxTilesY;
        Point red = new(width * 32 / 100, height * 43 / 100 + 22);
        return new ArenaLayout(new Rectangle(40, 150, width - 80, height - 370),
            new Point(width - 1 - red.X, red.Y), red)
        {
            BossSpawn = new Point(width / 2, height * 43 / 100)
        };
    }

    /// <summary>Empty results mean the native worker is still running; nonempty results need framing and sync.</summary>
    internal static IEnumerable<Rectangle> Generate(int seed, Rectangle protectedLobby)
    {
        if (Main.netMode != NetmodeID.Server)
            yield break;
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

        using NativeWorldGenSession session = new(seed, 3, width, height, 1, Main.GameMode);
        Task worker = null;
        try
        {
            session.BindTileArrays(); // Capture and pin private arrays on the server thread.
            worker = Task.Run(() => BuildJungle(session, width));
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

                    FinishArenaTile(x, y, layout);
                    if (reflectedX != x)
                        CopyReflected(x, reflectedX, y);
                }
                yield return strip;
                yield return new Rectangle(width - end, 0, end - start, height);
            }
        }
        finally
        {
            if (worker != null)
            {
                if (!worker.IsCompleted)
                    session.RequestCancel();
                // Normal completion observes errors above; disposal joins cancelled work before unpinning/freeing it.
                try { worker.GetAwaiter().GetResult(); }
                catch (Exception) { }
            }
        }
    }

    private static void BuildJungle(NativeWorldGenSession session, int width)
    {
        session.Initialize();
        Run(session, ["Reset", "Terrain"]);
        // Reset/Terrain overwrite these fields, so configure the compact jungle only after they finish.
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
        Run(session, JunglePasses);
        session.SyncToTml(); // Native output goes to the session's private arrays, never the live tilemap.
    }

    private static void Run(NativeWorldGenSession session, string[] passes)
    {
        WgResult result = session.RunPasses(passes);
        if (result != WgResult.Ok)
            throw new InvalidOperationException("WorldGen++ Jungle generation failed: " + result);
    }

    private static void FinishArenaTile(int x, int y, ArenaLayout layout)
    {
        Tile tile = Main.tile[x, y];
        // Furniture needs separate entities and directional frames; it cannot be mirrored as raw tiles.
        if (tile.HasTile && Main.tileFrameImportant[tile.TileType])
            tile.ClearTile();

        Point spawn = layout.RedSpawn;
        bool ledge = Math.Abs(x - spawn.X) <= 14 && y >= spawn.Y && y <= spawn.Y + 3;
        if (ledge)
        {
            tile.ClearTile();
            tile.LiquidAmount = 0;
            tile.HasTile = true;
            tile.TileType = TileID.JungleGrass;
            return;
        }
        bool spawnRoom = Math.Abs(x - spawn.X) <= 14 && y >= spawn.Y - 12 && y < spawn.Y;
        double bossX = (x - (Main.maxTilesX - 1) / 2d) / 22;
        double bossY = (y - layout.BossSpawn.Y) / 18;
        float progress = Math.Clamp((x - spawn.X) / (float)(layout.BossSpawn.X - spawn.X), 0f, 1f);
        float passageY = MathHelper.Lerp(spawn.Y - 6, layout.BossSpawn.Y, progress);
        bool passage = x >= spawn.X - 14 && Math.Abs(y - passageY) <= 8;
        if (spawnRoom || bossX * bossX + bossY * bossY < 1 || passage)
        {
            tile.ClearTile();
            tile.LiquidAmount = 0;
        }
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
