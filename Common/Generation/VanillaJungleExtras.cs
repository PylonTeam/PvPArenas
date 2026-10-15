using System;
using System.Collections.Generic;
using System.Linq;
using Terraria.GameContent.Biomes;
using Terraria.GameContent.Generation;
using Terraria.ID;
using Terraria.WorldBuilding;

namespace PvPArenas.Common.Generation;

internal readonly record struct JungleExtrasArgs(Rectangle Bounds, List<Rectangle> Reserved);
internal readonly record struct JungleExtraPlacement(string Feature, Rectangle Footprint);

/// <summary>Places actual Terraria features inside a source crop, accepting only contained, non-overlapping writes.</summary>
internal static class VanillaJungleExtras
{
    private readonly record struct ExistingFeature(string Name, ushort Tile, ushort Wall, int Minimum, Rectangle Required = default);

    internal static IReadOnlyList<JungleExtraPlacement> PlaceTemple(JungleExtrasArgs args)
    {
        List<JungleExtraPlacement> result = [];
        Rectangle existing = new(GenVars.tLeft, GenVars.tTop,
            Math.Max(0, GenVars.tRight - GenVars.tLeft + 1), Math.Max(0, GenVars.tBottom - GenVars.tTop + 1));
        Rectangle altarBounds = new(GenVars.lAltarX, GenVars.lAltarY, 3, 2);
        if (GenVars.tRooms >= 2 && args.Bounds.Contains(existing) && existing.Contains(altarBounds)
            && Count(altarBounds, TileID.LihzahrdAltar) == 6
            && TryReserveExisting(args, result, new("Temple", TileID.LihzahrdBrick, WallID.LihzahrdBrickUnsafe, 500, altarBounds)))
            return result;
        TryPlace(args, result, "Temple", Candidates(args.Bounds, 100, 75, 36), point =>
        {
            WorldGen.makeTemple(point.X, point.Y);
            Rectangle temple = new(GenVars.tLeft, GenVars.tTop,
                GenVars.tRight - GenVars.tLeft + 1, GenVars.tBottom - GenVars.tTop + 1);
            if (!args.Bounds.Contains(temple) || GenVars.tRooms < 2) return false;
            WorldGen.templePart2();
            GenPass altar = WorldGen.VanillaGenPasses["Lihzahrd Altars"];
            bool enabled = altar.Enabled;
            try
            {
                altar.Reset();
                altar.Apply(new GenerationProgress(), GenVars.configuration.GetPassConfiguration(altar.Name));
            }
            finally { if (!enabled) altar.Disable(); }
            return Count(args.Bounds, TileID.LihzahrdAltar) >= 6 && Count(args.Bounds, TileID.LihzahrdBrick) >= 500;
        });
        return result;
    }

    internal static IReadOnlyList<JungleExtraPlacement> PlaceRemaining(JungleExtrasArgs args)
    {
        List<JungleExtraPlacement> result = [];
        if (!TryReserveExisting(args, result, new("Mushroom", TileID.MushroomGrass, WallID.MushroomUnsafe, 100)))
            TryPlace(args, result, "Mushroom", Candidates(args.Bounds, 100, 110, 40), point =>
        {
            WorldGen.ShroomPatch(point.X, point.Y);
            Rectangle patch = Rectangle.Intersect(args.Bounds, new Rectangle(point.X - 75, point.Y - 100, 150, 150));
            for (int x = patch.Left; x < patch.Right; x++)
            for (int y = patch.Top; y < patch.Bottom; y++)
                if (Main.tile[x, y].HasTile && Main.tile[x, y].TileType == TileID.Mud
                    && Main.tile[x, y].WallType == WallID.MushroomUnsafe)
                {
                    WorldGen.grassSpread = 0;
                    WorldGen.SpreadGrass(x, y, TileID.Mud, TileID.MushroomGrass, repeat: false);
                }
            for (int x = patch.Left + 3; x < patch.Right - 3; x++)
            for (int y = patch.Top + 15; y < patch.Bottom - 3; y++)
                if (Main.tile[x, y].HasTile && Main.tile[x, y].TileType == TileID.MushroomGrass)
                {
                    WorldGen.GrowShroom(x, y);
                    WorldGen.PlaceTile(x, y - 1, TileID.MushroomPlants, mute: true);
                }
            return Count(args.Bounds, TileID.MushroomGrass) >= 100;
        });
        GraniteBiome granite = GenVars.configuration.CreateBiome<GraniteBiome>();
        if (!TryReserveExisting(args, result, new("Granite", TileID.Granite, WallID.GraniteUnsafe, 100)))
            TryPlace(args, result, "Granite", Candidates(args.Bounds, 105, 105, 48), point =>
                !Main.tile[point.X, point.Y].HasTile && granite.Place(point, GenVars.structures)
                && Count(args.Bounds, TileID.Granite) >= 100);
        MarbleBiome marble = GenVars.configuration.CreateBiome<MarbleBiome>();
        if (!TryReserveExisting(args, result, new("Marble", TileID.Marble, WallID.MarbleUnsafe, 100)))
            TryPlace(args, result, "Marble", Candidates(args.Bounds, 90, 65, 48), point =>
                marble.Place(point, GenVars.structures) && Count(args.Bounds, TileID.Marble) >= 100);
        if (!TryReserveExisting(args, result, new("Spider", 0, WallID.SpiderUnsafe, 40)))
            TryPlace(args, result, "Spider", OpenCaves(args.Bounds, 96), point =>
        {
            WorldGen.Spread.Spider(point.X, point.Y);
            return CountWalls(args.Bounds, WallID.SpiderUnsafe) >= 40;
        });
        TryPlace(args, result, "Gems", OpenCaves(args.Bounds, 96), point =>
        {
            WorldGen.Spread.Gem(point.X, point.Y);
            return CountGems(args.Bounds) >= 8;
        });
        if (!TryReserveExisting(args, result, new("Tracks", TileID.MinecartTrack, 0, 60)))
            TryPlace(args, result, "Tracks", Candidates(args.Bounds, 75, 35, 96), point =>
                new TrackGenerator().Place(point, 60, 140) && Count(args.Bounds, TileID.MinecartTrack) >= 60);
        TryPlace(args, result, "Traps", Floors(args.Bounds, 128), point =>
            WorldGen.placeTrap(point.X, point.Y, 0));
        if (GenVars.statueList == null || GenVars.statueList.Length < 3) WorldGen.SetupStatueList();
        TryPlace(args, result, "Statues", Floors(args.Bounds, 128), point =>
            WorldGen.Statue(point.X, point.Y, 0));
        PlacePlant(args, result, "LifeFruit", TileID.LifeFruit);
        PlacePlant(args, result, "PlanteraBulb", TileID.PlanteraBulb);
        TryPlace(args, result, "Chlorophyte", Candidates(args.Bounds, 12, 12, 96), point =>
        {
            Tile tile = Main.tile[point.X, point.Y];
            if (!tile.HasTile || tile.TileType is not (TileID.Mud or TileID.JungleGrass)) return false;
            WorldGen.OreRunner(point.X, point.Y, 6, 6, TileID.Chlorophyte);
            return Count(args.Bounds, TileID.Chlorophyte) >= 8;
        });
        PlaceSurface(args, result);
        VanillaJungleDetails.Place(args, result);
        return result;
    }

    private static void PlacePlant(JungleExtrasArgs args, List<JungleExtraPlacement> result, string feature, ushort type)
    {
        List<Point> sites = [];
        for (int x = args.Bounds.Left + 4; x < args.Bounds.Right - 4; x++)
        for (int y = args.Bounds.Top + 4; y < args.Bounds.Bottom - 4; y++)
        {
            Rectangle affected = new(x - 2, y - 2, 4, 5);
            if (args.Reserved.Any(area => area.Intersects(affected))) continue;
            bool valid = true;
            for (int xx = x - 1; xx <= x && valid; xx++)
            {
                if (!WorldGen.SolidTile(xx, y + 1) || Main.tile[xx, y + 1].TileType != TileID.JungleGrass)
                { valid = false; break; }
                for (int yy = y - 1; yy <= y; yy++)
                {
                    Tile tile = Main.tile[xx, yy];
                    // These are the replaceable tiles accepted by vanilla PlaceJunglePlant.
                    if (tile.LiquidAmount != 0 || tile.HasTile && tile.TileType is not
                        (TileID.JunglePlants or TileID.JungleVines or 655 or TileID.JungleThorns
                            or TileID.JunglePlants2 or TileID.PlantDetritus)
                        && !(tile.TileType == TileID.SmallPiles && tile.TileFrameY == 0))
                    { valid = false; break; }
                }
            }
            if (valid) sites.Add(new(x, y));
        }
        Shuffle(sites);
        Log.Debug($"[worldgen] Jungle extra {feature}: {sites.Count} eligible native plant sites.");
        TryPlace(args, result, feature, sites.Take(192), point =>
        {
            int before = Count(args.Bounds, type);
            WorldGen.PlaceJunglePlant(point.X, point.Y, type, 0, 0);
            return Count(args.Bounds, type) >= before + 4;
        });
    }

    internal static void TryPlace(JungleExtrasArgs args, List<JungleExtraPlacement> result,
        string feature, IEnumerable<Point> candidates, Func<Point, bool> place)
    {
        Point[] choices = candidates.ToArray();
        if (choices.Length == 0) return;
        using VanillaJungleTransaction transaction = new();
        int attempts = 0, rejected = 0;
        string lastFailure = "no unreserved candidate";
        foreach (Point point in choices)
        {
            if (args.Reserved.Any(area => area.Contains(point))) continue;
            attempts++;
            bool placed;
            bool previousLiquidCheck = WorldGen.noLiquidCheck;
            try { WorldGen.noLiquidCheck = true; placed = place(point); }
            catch (IndexOutOfRangeException error) { placed = false; lastFailure = error.GetType().Name; }
            catch (ArgumentOutOfRangeException error) { placed = false; lastFailure = error.GetType().Name; }
            finally { WorldGen.noLiquidCheck = previousLiquidCheck; }
            if (!placed) { transaction.Restore(); continue; }
            if (transaction.TryCommit(args.Bounds, args.Reserved, out Rectangle footprint))
            {
                args.Reserved.Add(footprint);
                GenVars.structures.AddProtectedStructure(footprint, 2);
                result.Add(new(feature, footprint));
                Log.Debug($"[worldgen] Jungle extra {feature}: placed after {attempts} attempts; footprint={footprint}.");
                return;
            }
            rejected++;
            lastFailure = transaction.LastFailure;
        }
        Log.Debug($"[worldgen] Jungle extra {feature}: no contained placement after {attempts} attempts ({rejected} rejected footprints); last rejection: {lastFailure}.");
    }

    private static bool TryReserveExisting(JungleExtrasArgs args, List<JungleExtraPlacement> result, ExistingFeature feature)
    {
        bool Matches(Tile tile) => feature.Tile != 0 && tile.HasTile && tile.TileType == feature.Tile
            || feature.Wall != 0 && tile.WallType == feature.Wall;
        bool Counts(Tile tile) => feature.Tile == 0 ? tile.WallType == feature.Wall
            : tile.HasTile && tile.TileType == feature.Tile;
        HashSet<Point> visited = [];
        Point[] directions = [new(-1, 0), new(1, 0), new(0, -1), new(0, 1),
            new(-1, -1), new(1, -1), new(-1, 1), new(1, 1)];
        for (int x = args.Bounds.Left; x < args.Bounds.Right; x++)
        for (int y = args.Bounds.Top; y < args.Bounds.Bottom; y++)
        {
            Point start = new(x, y);
            if (visited.Contains(start) || !Matches(Main.tile[x, y])) continue;
            Queue<Point> pending = new(); pending.Enqueue(start); visited.Add(start);
            int left = x, right = x, top = y, bottom = y, count = 0;
            bool contained = true;
            while (pending.TryDequeue(out Point point))
            {
                if (!args.Bounds.Contains(point)) contained = false;
                if (Counts(Main.tile[point.X, point.Y])) count++;
                left = Math.Min(left, point.X); right = Math.Max(right, point.X);
                top = Math.Min(top, point.Y); bottom = Math.Max(bottom, point.Y);
                foreach (Point direction in directions)
                {
                    Point next = point + direction;
                    if (next.X < 1 || next.Y < 1 || next.X >= Main.maxTilesX - 1 || next.Y >= Main.maxTilesY - 1
                        || visited.Contains(next) || !Matches(Main.tile[next.X, next.Y])) continue;
                    visited.Add(next); pending.Enqueue(next);
                }
            }
            Rectangle footprint = new(left, top, right - left + 1, bottom - top + 1);
            if (!contained || count < feature.Minimum || !feature.Required.IsEmpty && !footprint.Contains(feature.Required)
                || args.Reserved.Any(area => area.Intersects(footprint))) continue;
            args.Reserved.Add(footprint);
            GenVars.structures.AddProtectedStructure(footprint, 2);
            result.Add(new(feature.Name, footprint));
            Log.Debug($"[worldgen] Jungle extra {feature.Name}: reused complete native component; footprint={footprint}; tiles={count}.");
            return true;
        }
        return false;
    }

    internal static Point[] Candidates(Rectangle bounds, int insetX, int insetY, int count)
    {
        int left = bounds.Left + insetX, right = bounds.Right - insetX;
        int top = bounds.Top + insetY, bottom = bounds.Bottom - insetY;
        if (left >= right || top >= bottom) return [];
        return Enumerable.Range(0, count).Select(_ => new Point(WorldGen.genRand.Next(left, right),
            WorldGen.genRand.Next(top, bottom))).ToArray();
    }

    internal static Point[] Floors(Rectangle bounds, int count)
    {
        List<Point> floors = [];
        for (int x = bounds.Left + 8; x < bounds.Right - 8; x++)
        for (int y = bounds.Top + 12; y < bounds.Bottom - 8; y++)
            if (!Main.tile[x, y].HasTile && Main.tile[x, y].LiquidAmount == 0 && WorldGen.SolidTile(x, y + 1))
                floors.Add(new Point(x, y));
        Shuffle(floors);
        return floors.Take(count).ToArray();
    }

    private static Point[] OpenCaves(Rectangle bounds, int count)
    {
        List<Point> caves = [];
        for (int x = bounds.Left + 12; x < bounds.Right - 12; x += 3)
        for (int y = bounds.Top + 12; y < bounds.Bottom - 12; y += 3)
        {
            Tile tile = Main.tile[x, y];
            if (!tile.HasTile && tile.WallType == WallID.None && tile.LiquidAmount == 0)
                caves.Add(new Point(x, y));
        }
        Shuffle(caves);
        return caves.Take(count).ToArray();
    }

    internal static void Shuffle(List<Point> points)
    {
        for (int i = points.Count - 1; i > 0; i--)
        {
            int other = WorldGen.genRand.Next(i + 1);
            (points[i], points[other]) = (points[other], points[i]);
        }
    }

    internal static int Count(Rectangle bounds, ushort type)
    {
        int count = 0;
        for (int x = bounds.Left; x < bounds.Right; x++)
        for (int y = bounds.Top; y < bounds.Bottom; y++)
            if (Main.tile[x, y].HasTile && Main.tile[x, y].TileType == type) count++;
        return count;
    }

    private static int CountWalls(Rectangle bounds, ushort type)
    {
        int count = 0;
        for (int x = bounds.Left; x < bounds.Right; x++)
        for (int y = bounds.Top; y < bounds.Bottom; y++)
            if (Main.tile[x, y].WallType == type) count++;
        return count;
    }

    private static int CountGems(Rectangle bounds)
    {
        int count = 0;
        for (int x = bounds.Left; x < bounds.Right; x++)
        for (int y = bounds.Top; y < bounds.Bottom; y++)
            if (Main.tile[x, y].HasTile && Main.tile[x, y].TileType is >= TileID.Sapphire and <= TileID.Diamond
                or TileID.ExposedGems) count++;
        return count;
    }

    private static void PlaceSurface(JungleExtrasArgs args, List<JungleExtraPlacement> result)
    {
        SurfacePatch patch = CaptureSurface();
        if (patch == null) return;
        int centerY = args.Bounds.Top + 4 + patch.Height / 2;
        Point[] destinations = Candidates(args.Bounds, patch.Width / 2 + 2, patch.Height / 2 + 2, 64)
            .Select(point => new Point(point.X, centerY)).ToArray();
        TryPlace(args, result, "SurfaceJungle", destinations, point =>
        {
            Point destination = new(point.X - patch.Width / 2, point.Y - patch.Height / 2);
            patch.CopyTo(destination);
            // Stamping can remove support for a plant just outside the patch. Settle that native
            // edge interaction before copying either half, while the transaction measures every write.
            Rectangle frameBounds = new(destination.X - 2, destination.Y - 2, patch.Width + 4, patch.Height + 4);
            for (int x = frameBounds.Left; x < frameBounds.Right; x++)
            for (int y = frameBounds.Top; y < frameBounds.Bottom; y++)
                WorldGen.TileFrame(x, y);
            return true;
        });
    }

    private static SurfacePatch CaptureSurface()
    {
        // Grow in the original surface context, then copy value data before restoring it.
        // The transplanted patch is visual surface terrain; combat depth remains the arena's policy.
        List<Point> floors = [];
        int bottom = Math.Min(Main.maxTilesY - 30, (int)Math.Ceiling(Main.worldSurface) + 70);
        for (int x = 30; x < Main.maxTilesX - 30; x += 3)
        {
            int y = 30;
            // Plants and trunks can be the first active tile in an otherwise valid jungle column.
            while (y < bottom && !WorldGen.SolidTile(x, y)) y++;
            if (y < bottom && Main.tile[x, y].TileType == TileID.JungleGrass) floors.Add(new(x, y));
        }
        Shuffle(floors);
        int attempted = 0, outside = 0, unsupportedCount = 0, noTree = 0, noGrass = 0, removedTrees = 0;
        using VanillaJungleTransaction transaction = new();
        foreach (Point floor in floors.Take(128))
        {
            attempted++;
            Rectangle area = new(floor.X - 24, floor.Y - 48, 48, 70);
            if (area.Top < 10 || area.Bottom >= Main.maxTilesY - 10) { outside++; continue; }
            bool unsupported = false;
            for (int x = area.Left; x < area.Right && !unsupported; x++)
            for (int y = area.Top; y < area.Bottom; y++)
            {
                Tile tile = Main.tile[x, y];
                if (tile.HasTile && Main.tileFrameImportant[tile.TileType]
                    && tile.TileType is not (TileID.Trees or TileID.JunglePlants or TileID.JunglePlants2
                        or TileID.Plants or TileID.Plants2)
                    && !(JungleLegacyObjects.TryGetBounds(x, y, tile, out Rectangle objectBounds)
                        && area.Contains(objectBounds))) { unsupported = true; break; }
            }
            if (unsupported) { unsupportedCount++; continue; }
            // Discard only clipped neighbouring tree components. A complete central tree is valid
            // even when an unrelated tree crosses the edge of this temporary source patch.
            removedTrees += RemoveCutTrees(area);
            bool rootedTree = HasRootedTree(area);
            if (!rootedTree)
            {
                bool previousNoLiquidCheck = WorldGen.noLiquidCheck;
                WorldGen.noLiquidCheck = true;
                try
                {
                    // GrowTree's native clearance check accepts saplings, not ordinary foliage.
                    for (int x = floor.X - 2; x <= floor.X + 2; x++)
                    for (int y = floor.Y - 22; y < floor.Y; y++)
                    {
                        Tile tile = Main.tile[x, y];
                        if (tile.HasTile && tile.TileType is TileID.JunglePlants or TileID.JunglePlants2
                            or TileID.Plants or TileID.Plants2)
                            WorldGen.KillTile(x, y, noItem: true);
                    }
                    WorldGen.GrowTree(floor.X, floor.Y);
                }
                finally { WorldGen.noLiquidCheck = previousNoLiquidCheck; }
                removedTrees += RemoveCutTrees(area);
                rootedTree = HasRootedTree(area);
            }
            bool exposedGrass = false;
            for (int x = area.Left; x < area.Right && !exposedGrass; x++)
            for (int y = area.Top; y < area.Bottom; y++)
                exposedGrass |= Main.tile[x, y].HasTile && Main.tile[x, y].TileType == TileID.JungleGrass
                    && Main.tile[x, y].WallType == WallID.None;
            if (rootedTree && exposedGrass)
            {
                Log.Debug($"[worldgen] Jungle extra SurfaceJungle: captured native surface after {attempted}/{floors.Count} solid jungle floors; removed {removedTrees} clipped neighbouring tree tiles.");
                return new SurfacePatch(area);
            }
            if (!rootedTree) noTree++;
            if (!exposedGrass) noGrass++;
            transaction.Restore();
        }
        Log.Debug($"[worldgen] Jungle extra SurfaceJungle: no native surface capture; solid jungle floors={floors.Count}, attempted={attempted}, outside={outside}, unsupported objects={unsupportedCount}, no complete rooted tree={noTree}, no exposed grass={noGrass}.");
        return null;

        static bool IsTree(int x, int y) => Main.tile[x, y].HasTile && Main.tile[x, y].TileType == TileID.Trees;

        static bool HasRootedTree(Rectangle area)
        {
            if (Count(area, TileID.Trees) < 5) return false;
            for (int x = area.Left + 1; x < area.Right - 1; x++)
            for (int y = area.Top + 1; y < area.Bottom - 1; y++)
                if (IsTree(x, y) && WorldGen.SolidTile(x, y + 1)
                    && Main.tile[x, y + 1].TileType == TileID.JungleGrass) return true;
            return false;
        }

        static int RemoveCutTrees(Rectangle area)
        {
            HashSet<Point> visited = [];
            int removed = 0;
            for (int x = area.Left; x < area.Right; x++)
            for (int y = area.Top; y < area.Bottom; y++)
            {
                Point start = new(x, y);
                if (!IsTree(x, y) || !visited.Add(start)) continue;
                List<Point> component = [];
                Queue<Point> queue = new();
                queue.Enqueue(start);
                bool cut = false;
                while (queue.TryDequeue(out Point point))
                {
                    component.Add(point);
                    cut |= point.X == area.Left || point.X == area.Right - 1
                        || point.Y == area.Top || point.Y == area.Bottom - 1;
                    for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        Point next = new(point.X + dx, point.Y + dy);
                        if (area.Contains(next) && IsTree(next.X, next.Y) && visited.Add(next)) queue.Enqueue(next);
                    }
                }
                if (!cut) continue;
                foreach (Point point in component) Main.tile[point.X, point.Y].ClearTile();
                removed += component.Count;
            }
            return removed;
        }
    }

    private sealed class SurfacePatch
    {
        internal readonly int Width, Height;
        private readonly TileTypeData[] tiles;
        private readonly WallTypeData[] walls;
        private readonly LiquidData[] liquid;
        private readonly TileWallBrightnessInvisibilityData[] brightness;
        private readonly TileWallWireStateData[] wires;

        internal SurfacePatch(Rectangle area)
        {
            Width = area.Width; Height = area.Height;
            tiles = Read<TileTypeData>(area); walls = Read<WallTypeData>(area); liquid = Read<LiquidData>(area);
            brightness = Read<TileWallBrightnessInvisibilityData>(area); wires = Read<TileWallWireStateData>(area);
        }

        internal void CopyTo(Point target)
        {
            Write(tiles, target); Write(walls, target); Write(liquid, target); Write(brightness, target); Write(wires, target);
        }

        private T[] Read<T>(Rectangle area) where T : unmanaged, ITileData
        {
            T[] source = Main.tile.GetData<T>(), copy = new T[Width * Height];
            for (int x = 0; x < Width; x++)
                source.AsSpan((area.Left + x) * Main.tile.Height + area.Top, Height).CopyTo(copy.AsSpan(x * Height, Height));
            return copy;
        }

        private void Write<T>(T[] source, Point target) where T : unmanaged, ITileData
        {
            T[] destination = Main.tile.GetData<T>();
            for (int x = 0; x < Width; x++)
                source.AsSpan(x * Height, Height).CopyTo(destination.AsSpan((target.X + x) * Main.tile.Height + target.Y, Height));
        }
    }
}
