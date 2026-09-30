using PvPArenas.Common.Game;
using System;
using System.Collections.Generic;
using Terraria.ID;
using Terraria.Utilities;

namespace PvPArenas.Common.Generation;

/// <summary>Bounded counterparts of vanilla Jungle Plants/Vines, after all terrain and landmark carving.</summary>
internal static class JungleArenaFoliage
{
    internal static IEnumerable<Rectangle> Place(int seed, ArenaLayout layout, Rectangle protectedLobby)
    {
        if (Main.netMode != NetmodeID.Server) yield break;
        Rectangle area = layout.ArenaBounds;
        Rectangle avoid = protectedLobby;
        avoid.Inflate(3, 3);
        UnifiedRandom random = new(seed ^ 0x4F19AC);
        int half = Main.maxTilesX / 2, smallPlants = 0;
        bool fruitPlaced = false;
        bool Allowed(Rectangle patch) => area.Contains(patch) && !avoid.Intersects(patch)
            && !avoid.Intersects(new Rectangle(Main.maxTilesX - patch.Right, patch.Y, patch.Width, patch.Height));

        for (int start = area.Left + 2; start < half - 2; start += 16)
        {
            if (Main.netMode != NetmodeID.Server) yield break;
            int end = Math.Min(half - 2, start + 16);
            for (int x = start; x < end; x++)
            for (int y = area.Top + 2; y < area.Bottom - 2; y++)
            {
                Tile ground = Main.tile[x, y];
                if (!ground.HasUnactuatedTile || ground.TileType != TileID.JungleGrass
                    || ground.Slope != SlopeType.Solid || ground.IsHalfBlock) continue;

                // Vanilla spores are the frame-144 variant of the one-tile Jungle plant.
                if (Empty(x, y - 1) && Allowed(new Rectangle(x - 1, y - 3, 3, 5)))
                {
                    if ((!fruitPlaced || random.Next(7) == 0) && Empty(x - 1, y - 1)
                        && Empty(x - 1, y - 2) && Empty(x, y - 2) && WorldGen.SolidTile(x - 1, y)
                        && Main.tile[x - 1, y].TileType == TileID.JungleGrass)
                    {
                        ushort type = !fruitPlaced ? TileID.LifeFruit : TileID.PlantDetritus;
                        int style = fruitPlaced ? random.Next(12) : random.Next(3);
                        WorldGen.PlaceJunglePlant(x, y - 1, type, style, 1);
                        WorldGen.PlaceJunglePlant(Main.maxTilesX - x, y - 1, type, style, 1);
                        if (!fruitPlaced && Main.tile[x, y - 1].HasTile && Main.tile[x, y - 1].TileType == TileID.LifeFruit)
                            fruitPlaced = true;
                    }
                    else if (random.Next(3) == 0)
                    {
                        Tile plant = Main.tile[x, y - 1];
                        plant.ClearTile();
                        plant.HasTile = true;
                        plant.TileType = TileID.JunglePlants;
                        plant.TileFrameX = (short)((smallPlants++ % 8 == 0 ? 8 : random.Next(8)) * 18);
                        plant.TileFrameY = 0;
                        Main.tile[Main.maxTilesX - 1 - x, y - 1].CopyFrom(plant);
                    }
                }

                // Short dangling runs decorate ceilings without filling whole passages with vines.
                if (random.Next(5) != 0 || !Empty(x, y + 1)) continue;
                int length = random.Next(2, 10);
                for (int step = 1; step <= length && y + step < area.Bottom - 2; step++)
                {
                    if (!Empty(x, y + step) || !Allowed(new Rectangle(x, y + step, 1, 1))) break;
                    Tile vine = Main.tile[x, y + step];
                    vine.ClearTile();
                    vine.HasTile = true;
                    vine.TileType = TileID.JungleVines;
                    Main.tile[Main.maxTilesX - 1 - x, y + step].CopyFrom(vine);
                }
            }
            // Large plants may begin one column before the stripe.
            Rectangle changed = new(start - 1, area.Top, end - start + 2, area.Height);
            yield return changed;
            yield return new Rectangle(Main.maxTilesX - changed.Right, changed.Y, changed.Width, changed.Height);
        }
    }

    private static bool Empty(int x, int y) => !Main.tile[x, y].HasTile && Main.tile[x, y].LiquidAmount == 0;
}
