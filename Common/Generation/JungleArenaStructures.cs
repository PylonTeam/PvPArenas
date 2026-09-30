using PvPArenas.Common.Game;
using System;
using System.Collections.Generic;
using Terraria.ID;

namespace PvPArenas.Common.Generation;

/// <summary>Bounded Jungle landmarks. Mirror their terrain first, then place each side's vanilla objects.</summary>
internal static class JungleArenaStructures
{
    internal static IEnumerable<Rectangle> Place(int seed, ArenaLayout layout, Rectangle protectedLobby,
        IReadOnlyList<JungleChamber> landmarks)
    {
        if (Main.netMode != NetmodeID.Server) yield break;
        if (landmarks.Count != 4)
            throw new InvalidOperationException("The Jungle needs a hive, mahogany tree, shrine, and cabin site.");

        Rectangle reflectedLobby = Reflect(protectedLobby);
        Rectangle leftHalf = Rectangle.Intersect(layout.ArenaBounds,
            new Rectangle(1, 1, Main.maxTilesX / 2 - 1, Main.maxTilesY - 2));
        for (int kind = 0; kind < landmarks.Count; kind++)
        {
            if (Main.netMode != NetmodeID.Server) yield break;
            JungleChamber site = landmarks[kind];
            Rectangle bounds = new(site.Center.X - site.RadiusX, site.Center.Y - site.RadiusY,
                site.RadiusX * 2 + 1, site.RadiusY * 2 + 1);
            Rectangle guarded = bounds;
            guarded.Inflate(2, 2);
            if (!leftHalf.Contains(bounds) || guarded.Intersects(protectedLobby) || guarded.Intersects(reflectedLobby))
                throw new InvalidOperationException("A Jungle landmark would overlap the lobby or arena edge.");

            Landmark builder = new(bounds, new Random(unchecked(seed ^ (0x4A756E67 + kind * 7919))));
            Point objectBottom = kind switch
            {
                0 => builder.Hive(),
                1 => builder.Mahogany(),
                2 => builder.Shrine(),
                _ => builder.Shelter(cabin: true)
            };
            MirrorTerrain(bounds);
            // Object coordinates are mirrored; their frames are placed in normal left-to-right order.
            if (kind == 0)
            {
                Larva(objectBottom);
                Larva(new Point(Main.maxTilesX - 1 - objectBottom.X, objectBottom.Y));
            }
            else
            {
                Chest(objectBottom, kind);
                Chest(new Point(Main.maxTilesX - 2 - objectBottom.X, objectBottom.Y), kind);
            }
            Log.Debug($"[worldgen] PASS | Jungle landmark: {new[] { "Hive", "Mahogany", "Shrine", "Cabin" }[kind]} | "
                + $"MirroredPairs: 1/1 | Left: {bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}");
            yield return bounds;
            yield return Reflect(bounds);
        }
    }

    private static Rectangle Reflect(Rectangle bounds) =>
        new(Main.maxTilesX - bounds.Right, bounds.Y, bounds.Width, bounds.Height);

    private static void MirrorTerrain(Rectangle bounds)
    {
        for (int x = bounds.Left; x < bounds.Right; x++)
        for (int y = bounds.Top; y < bounds.Bottom; y++)
        {
            Tile source = Main.tile[x, y], target = Main.tile[Main.maxTilesX - 1 - x, y];
            target.CopyFrom(source);
            target.Slope = source.Slope switch
            {
                SlopeType.SlopeDownLeft => SlopeType.SlopeDownRight,
                SlopeType.SlopeDownRight => SlopeType.SlopeDownLeft,
                SlopeType.SlopeUpLeft => SlopeType.SlopeUpRight,
                SlopeType.SlopeUpRight => SlopeType.SlopeUpLeft,
                _ => source.Slope
            };
        }
    }

    private static void Larva(Point bottom)
    {
        WorldGen.Place3x3(bottom.X, bottom.Y, TileID.Larva);
        for (int x = -1; x <= 1; x++)
        for (int y = -2; y <= 0; y++)
        {
            Tile tile = Main.tile[bottom.X + x, bottom.Y + y];
            if (!tile.HasTile || tile.TileType != TileID.Larva)
                throw new InvalidOperationException("The Jungle hive's larva could not be placed.");
        }
    }

    private static void Chest(Point bottom, int kind)
    {
        int index = WorldGen.PlaceChest(bottom.X, bottom.Y, TileID.Containers, style: kind == 3 ? 8 : 10);
        if (index < 0)
            throw new InvalidOperationException("The Jungle landmark's chest could not be placed.");
        // Matching modest supplies on both sides; never roll independent loot for a mirrored arena.
        (int Type, int Count)[] supplies = kind switch
        {
            1 => [(ItemID.RichMahogany, 25), (ItemID.JungleSpores, 3)],
            2 => [(ItemID.JungleSpores, 6), (ItemID.Stinger, 3), (ItemID.Vine, 2)],
            _ => [(ItemID.Torch, 20), (ItemID.HealingPotion, 2), (ItemID.RichMahogany, 25)]
        };
        for (int i = 0; i < supplies.Length; i++)
        {
            Main.chest[index].item[i].SetDefaults(supplies[i].Type);
            Main.chest[index].item[i].stack = supplies[i].Count;
        }
    }

    private sealed class Landmark(Rectangle bounds, Random random)
    {
        private readonly int cx = bounds.Center.X, cy = bounds.Center.Y;

        internal Point Hive()
        {
            int rx = bounds.Width / 2 - 2, ry = bounds.Height / 2 - 2;
            double phase = random.NextDouble() * Math.Tau;
            for (int x = bounds.Left; x < bounds.Right; x++)
            for (int y = bounds.Top; y < bounds.Bottom; y++)
            {
                double dx = (x - cx) / (double)rx, dy = (y - cy) / (double)ry;
                double angle = Math.Atan2(dy, dx);
                // Uneven connected lobes and a thick shell, like HiveBiome's overlapping tunnel runners.
                double edge = 1 + .13 * Math.Sin(angle * 3 + phase) + .07 * Math.Sin(angle * 5 - phase);
                double radius = dx * dx + dy * dy;
                if (radius > edge) continue;
                if (radius > edge * .65) Solid(x, y, TileID.Hive, WallID.HiveUnsafe);
                else Air(x, y, WallID.HiveUnsafe);
            }

            int entry = cy + bounds.Height / 4;
            Entrance(entry, WallID.HiveUnsafe);
            // A closed honey basin stays below the walking entrances, with a raised larva stand beside it.
            int basinFloor = Math.Min(bounds.Bottom - 3, entry + 8);
            for (int x = cx + 2; x <= cx + 15; x++)
            for (int y = entry + 4; y <= basinFloor; y++)
            {
                if (x == cx + 2 || x == cx + 15 || y == basinFloor)
                    Solid(x, y, TileID.Hive, WallID.HiveUnsafe);
                else
                {
                    Air(x, y, WallID.HiveUnsafe);
                    Tile honey = Main.tile[x, y];
                    honey.LiquidType = LiquidID.Honey;
                    honey.LiquidAmount = byte.MaxValue;
                }
            }
            int standX = cx - 8, standY = entry - 2;
            for (int x = standX - 2; x <= standX + 2; x++)
            for (int y = standY - 4; y <= basinFloor; y++)
            {
                if (y < standY) Air(x, y, WallID.HiveUnsafe);
                else Solid(x, y, TileID.Hive, WallID.HiveUnsafe);
            }
            return new Point(standX, standY - 1);
        }

        internal Point Mahogany()
        {
            int entry = cy + bounds.Height / 2 - 8;
            int top = bounds.Top + 8, floor = entry + 4;
            double phase = random.NextDouble() * Math.Tau;
            // Make a ragged, tall pocket around the tree rather than a circular arena chamber.
            for (int y = top - 3; y < floor; y++)
            {
                double progress = (y - top + 3d) / (floor - top + 3d);
                int radius = (int)(13 + 10 * Math.Sin(progress * Math.PI)
                    + 2 * Math.Sin(y * .27 + phase));
                for (int x = cx - radius; x <= cx + radius; x++) Air(x, y, WallID.JungleUnsafe);
            }

            int trunkTop = top + 12;
            int Trunk(int y) => cx + (int)Math.Round(2 * Math.Sin((y - top) * .10 + phase));
            for (int y = trunkTop; y <= floor; y++)
            for (int x = Trunk(y) - 4; x <= Trunk(y) + 4; x++)
            {
                if (Math.Abs(x - Trunk(y)) >= 3 || y == floor)
                    Solid(x, y, TileID.LivingMahogany, WallID.LivingWoodUnsafe);
                else Air(x, y, WallID.LivingWoodUnsafe);
            }
            for (int branch = 0; branch < 5; branch++)
            {
                int side = branch % 2 == 0 ? -1 : 1;
                int y = trunkTop + 3 + branch * 7;
                Point start = new(Trunk(y), y);
                Point end = new(cx + side * random.Next(16, 24), y - random.Next(7, 14));
                Branch(start, end, 2, TileID.LivingMahogany);
                Leaves(end.X, end.Y, random.Next(5, 8), random.Next(4, 7), phase + branch);
            }
            Leaves(Trunk(trunkTop), trunkTop - 4, 10, 6, phase);
            for (int side = -1; side <= 1; side += 2)
            {
                Branch(new Point(Trunk(floor), floor - 1), new Point(cx + side * 22, bounds.Bottom - 2), 2,
                    TileID.LivingMahogany);
                Branch(new Point(Trunk(floor), floor), new Point(cx + side * 12, bounds.Bottom - 1), 1,
                    TileID.LivingMahogany);
            }
            Entrance(entry, WallID.JungleUnsafe, preserveMiddle: 7);
            // The lower hollow is wide enough to enter; a real Ivy Chest rests inside its roots.
            int chestX = Trunk(floor) - 1;
            // The wavering trunk can lean two tiles right: bridge that offset to the fixed side entrance.
            for (int x = Math.Min(cx - 8, chestX - 6); x <= chestX + 3; x++)
            {
                for (int y = floor - 4; y < floor; y++) Air(x, y, WallID.LivingWoodUnsafe);
                Solid(x, floor, TileID.LivingMahogany, WallID.LivingWoodUnsafe);
            }
            return new Point(chestX, floor - 1);
        }

        internal Point Shrine()
        {
            const ushort block = TileID.Mudstone, wall = WallID.MudstoneBrick;
            int floor = cy + 9, roof = cy - 7;
            // A small stepped Jungle shrine embedded into the cave floor, with an accessible Ivy Chest.
            // Its reserved envelope includes the approach, foundation and roof so later passes cannot cut it.
            for (int x = bounds.Left; x < bounds.Right; x++)
            for (int y = bounds.Top; y < bounds.Bottom; y++)
            {
                if (y >= floor + 3) Solid(x, y, TileID.Mud, WallID.JungleUnsafe);
                else if (Math.Abs(x - cx) <= 15 && y >= floor)
                    Solid(x, y, block, wall);
                else Air(x, y, WallID.JungleUnsafe);
            }
            for (int x = cx - 11; x <= cx + 11; x++)
            {
                int rise = Math.Max(0, 3 - Math.Abs(x - cx) / 3);
                for (int y = roof - rise; y <= roof + 1; y++) Solid(x, y, block, wall);
                for (int y = roof + 2; y < floor; y++)
                    Main.tile[x, y].WallType = wall;
            }
            for (int side = -1; side <= 1; side += 2)
            {
                int column = cx + side * 9;
                for (int y = roof + 2; y < floor; y++)
                for (int x = column - 1; x <= column + 1; x++) Solid(x, y, block, wall);
                // Doorway through the lower columns, with a continuous seven-tile-high approach.
                for (int x = side < 0 ? bounds.Left : cx + 5; x <= (side < 0 ? cx - 5 : bounds.Right - 1); x++)
                for (int y = floor - 7; y < floor; y++) Air(x, y, wall);
                for (int step = 0; step < 3; step++)
                for (int y = floor + step; y < bounds.Bottom; y++)
                    Solid(cx + side * (16 + step), y, block, WallID.JungleUnsafe);
            }
            return new Point(cx - 1, floor - 1);
        }

        internal Point Shelter(bool cabin)
        {
            int halfWidth = cabin ? 12 : 9, roof = cy - 7, floor = cy + 9;
            ushort block = cabin ? TileID.RichMahogany : random.Next(2) == 0 ? TileID.IridescentBrick : TileID.Mudstone;
            ushort wall = cabin ? WallID.RichMaogany : block == TileID.IridescentBrick ? WallID.IridescentBrick : WallID.MudstoneBrick;
            for (int x = cx - halfWidth - 2; x <= cx + halfWidth + 2; x++)
            for (int y = roof - 3; y <= floor + 2; y++)
            {
                if (y >= floor) Solid(x, y, y == floor ? block : TileID.Mud, wall);
                else Air(x, y, wall);
            }
            for (int x = cx - halfWidth; x <= cx + halfWidth; x++)
            {
                int roofY = roof - Math.Max(0, (halfWidth - Math.Abs(x - cx)) / (cabin ? 5 : 4));
                Solid(x, roofY, block, wall);
                Solid(x, roofY + 1, block, wall);
                for (int y = roofY + 2; y < floor; y++)
                {
                    if (Math.Abs(x - cx) >= halfWidth - 1) Solid(x, y, block, wall);
                    else if (cabin && random.Next(15) == 0)
                    {
                        Tile agedWall = Main.tile[x, y];
                        agedWall.WallType = WallID.JungleUnsafe;
                    }
                }
            }
            // Vanilla Jungle shrines are small roofed huts with an Ivy Chest, not temple-sized arches.
            Entrance(cy + 5, wall);
            if (cabin)
                for (int x = cx - halfWidth + 2; x <= cx + halfWidth - 2; x += halfWidth * 2 - 4)
                for (int y = floor + 1; y < bounds.Bottom; y++) Solid(x, y, TileID.RichMahoganyBeam, wall);
            return new Point(cx - 1, floor - 1);
        }

        private void Entrance(int y, ushort wall, int preserveMiddle = 0)
        {
            for (int x = bounds.Left; x < bounds.Right; x++)
            for (int dy = -3; dy <= 3; dy++)
                if (Math.Abs(x - cx) >= preserveMiddle) Air(x, y + dy, wall);
        }

        private void Leaves(int x, int y, int rx, int ry, double phase)
        {
            for (int dx = -rx - 1; dx <= rx + 1; dx++)
            for (int dy = -ry - 1; dy <= ry + 1; dy++)
            {
                double radius = dx * dx / (double)(rx * rx) + dy * dy / (double)(ry * ry);
                if (radius > 1 + .13 * Math.Sin(dx * 1.1 + phase) * Math.Cos(dy * .7)) continue;
                if (bounds.Contains(x + dx, y + dy) && Main.tile[x + dx, y + dy].TileType != TileID.LivingMahogany)
                    Solid(x + dx, y + dy, TileID.LivingMahoganyLeaves, WallID.JungleUnsafe);
            }
        }

        private void Branch(Point from, Point to, int radius, ushort type)
        {
            int steps = Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y));
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)Math.Max(1, steps);
                int x = (int)MathF.Round(MathHelper.Lerp(from.X, to.X, t));
                int y = (int)MathF.Round(MathHelper.Lerp(from.Y, to.Y, t) - MathF.Sin(t * MathF.PI) * 2);
                int r = t > .7 ? Math.Max(1, radius - 1) : radius;
                for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                    if (dx * dx + dy * dy <= r * r + 1) Solid(x + dx, y + dy, type, WallID.LivingWoodUnsafe);
            }
        }

        private void Solid(int x, int y, ushort type, ushort wall)
        {
            if (!bounds.Contains(x, y)) return;
            Tile tile = Main.tile[x, y];
            tile.ClearEverything();
            tile.HasTile = true;
            tile.TileType = type;
            tile.WallType = wall;
        }

        private void Air(int x, int y, ushort wall)
        {
            if (!bounds.Contains(x, y)) return;
            Tile tile = Main.tile[x, y];
            tile.ClearEverything();
            tile.WallType = wall;
        }
    }
}
