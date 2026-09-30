using PvPArenas.Common.Game;
using System;
using System.Collections.Generic;
using Terraria.Chat;
using Terraria.ID;
using Terraria.Localization;

namespace PvPArenas.Common.Generation;

/// <summary>Server-owned, bounded Jungle generation. No native passes or previous-world terrain are inputs.</summary>
internal static class MirroredJungleGenerator
{
    private const int ColumnsPerStep = 16;
    private const int Surface = 55, Rock = 100;

    internal static ArenaLayout CreateLayout()
    {
        int width = Main.maxTilesX;
        Point red = new(width * 26 / 100, 276);
        return new ArenaLayout(new Rectangle(60, 75, width - 120, 310),
            new Point(width - 1 - red.X, red.Y), red)
        {
            BossSpawn = new Point(width / 2, 230)
        };
    }

    internal static IEnumerable<Rectangle> Generate(int seed, Rectangle protectedLobby)
    {
        if (Main.netMode != NetmodeID.Server) yield break;
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

    internal static IEnumerable<Rectangle> GenerateWithProgress(int seed, Rectangle protectedLobby, WorldGenProgressLog trace)
    {
        if (Main.netMode != NetmodeID.Server) yield break;
        foreach (Rectangle area in GenerateCore(seed, protectedLobby, trace)) yield return area;
    }

    private static IEnumerable<Rectangle> GenerateCore(int seed, Rectangle protectedLobby, WorldGenProgressLog trace)
    {
        if (Main.maxTilesX != ArenaWorldSystem.Width || Main.maxTilesY != ArenaWorldSystem.Height)
            throw new InvalidOperationException("The Jungle generator requires the compact arena world.");
        int width = Main.maxTilesX, height = Main.maxTilesY, half = (width + 1) / 2;
        ArenaLayout layout = CreateLayout();
        Rectangle reflectedLobby = new(width - protectedLobby.Right, protectedLobby.Y,
            protectedLobby.Width, protectedLobby.Height);
        Rectangle sourceHalf = new(0, 0, half, height);
        Rectangle left = Rectangle.Intersect(protectedLobby, sourceHalf);
        Rectangle right = Rectangle.Intersect(reflectedLobby, sourceHalf);
        Rectangle excluded = left.IsEmpty ? right : right.IsEmpty ? left : Rectangle.Union(left, right);

        // Plan all reservations and routes before writing even the first tile. This uses seed data only.
        trace.Report("Planning custom Jungle caves and landmarks", 0);
        JungleArenaPlan plan = new(seed, layout, excluded);
        trace.Report("Planning custom Jungle caves and landmarks", 1);
        Main.worldSurface = Surface;
        Main.rockLayer = Rock;
        Liquid.ReInit();
        ArenaTemplate.ClearEntitiesOutside(protectedLobby);
        int initialized = 0;
        trace.Report("Replacing every world tile", 0);
        for (int start = 0; start < half; start += ColumnsPerStep)
        {
            if (Main.netMode != NetmodeID.Server) yield break;
            int end = Math.Min(half, start + ColumnsPerStep);
            for (int x = start; x < end; x++)
            for (int y = 0; y < height; y++)
            {
                int reflectedX = width - 1 - x;
                bool preserveLeft = protectedLobby.Contains(x, y), preserveRight = protectedLobby.Contains(reflectedX, y);
                if (preserveLeft || preserveRight)
                {
                    if (preserveLeft != preserveRight)
                        Main.tile[preserveLeft ? reflectedX : x, y].CopyFrom(Main.tile[preserveLeft ? x : reflectedX, y]);
                    continue;
                }
                Tile tile = Main.tile[x, y];
                // ClearEverything clears all five tML tile components, including paint, wires and stale liquids.
                tile.ClearEverything();
                if (y >= Surface)
                {
                    tile.HasTile = true;
                    tile.TileType = TileID.Mud;
                    tile.WallType = WallID.JungleUnsafe;
                    plan.ApplyTerrain(x, y);
                }
                Main.tile[reflectedX, y].CopyFrom(tile);
                initialized += x == reflectedX ? 1 : 2;
            }
            trace.Report("Replacing every world tile", end / (double)half);
            yield return new Rectangle(start, 0, end - start, height);
            yield return new Rectangle(width - end, 0, end - start, height);
        }
        Log.Debug($"[worldgen] PASS | Custom Jungle initialized | TilesReplaced: {initialized}/{width * height} | PreservedLobby: {protectedLobby.Width * protectedLobby.Height} | NativePasses: 0");

        trace.Report("Building cave-connected Jungle landmarks", 0);
        int landmarks = 0;
        foreach (Rectangle changed in JungleArenaStructures.Place(seed, layout, protectedLobby, plan.Chambers))
        {
            trace.Report("Building cave-connected Jungle landmarks", ++landmarks / (double)(plan.Chambers.Count * 2));
            yield return changed;
        }
        trace.Report("Growing Jungle foliage", 0);
        foreach (Rectangle changed in JungleArenaFoliage.Place(seed, layout, protectedLobby)) yield return changed;
        trace.Report("Growing Jungle foliage", 1);

        // Never frame a new strip against tiles left over from the previous arena. All neighbors now exist.
        trace.Report("Framing completed Jungle", 0);
        Rectangle guard = protectedLobby;
        if (!guard.IsEmpty) guard.Inflate(2, 2);
        for (int start = 6; start < width - 6; start += ColumnsPerStep)
        {
            if (Main.netMode != NetmodeID.Server) yield break;
            int end = Math.Min(width - 6, start + ColumnsPerStep);
            bool previousLiquidCheck = WorldGen.noLiquidCheck;
            try
            {
                WorldGen.noLiquidCheck = true;
                for (int x = start; x < end; x++)
                for (int y = 6; y < height - 6; y++)
                {
                    if (guard.Contains(x, y) || guard.Contains(width - 1 - x, y)) continue;
                    WorldGen.TileFrame(x, y, noBreak: true);
                    Framing.WallFrame(x, y);
                }
            }
            finally { WorldGen.noLiquidCheck = previousLiquidCheck; }
            trace.Report("Framing completed Jungle", end / (double)(width - 6));
            yield return new Rectangle(start, 0, end - start, height);
        }
        Liquid.ReInit();
        Main.spawnTileX = layout.BossSpawn.X;
        Main.spawnTileY = layout.BossSpawn.Y + 10;
    }
}
