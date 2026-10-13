using PvPArenas.Common.Game;
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria.ID;
using Terraria.ObjectData;

namespace PvPArenas.Common.Generation;

/// <summary>Crops a real vanilla underground Jungle and reflects it into the compact arena.</summary>
internal static class VanillaMirroredJungleGenerator
{
    private const int Surface = 55, Rock = 100, ColumnsPerStep = 16;
    private static ArenaLayout generatedLayout;

    internal static ArenaLayout CreateLayout() => generatedLayout ?? new ArenaLayout(
        new Rectangle(60, 75, ArenaWorldSystem.Width - 120, 310), new Point(628, 276), new Point(221, 276))
        { BossSpawn = new Point(425, 230) };

    internal static void ResetLayout() => generatedLayout = null;

    internal static IEnumerable<Rectangle> Generate(int seed, Rectangle protectedLobby)
    {
        if (Main.netMode != NetmodeID.Server) yield break;
        using WorldGenProgressLog trace = new("Vanilla Plantera Jungle", seed, Main.maxTilesX, Main.maxTilesY);
        foreach (Rectangle changed in GenerateWithProgress(seed, protectedLobby, trace)) yield return changed;
        trace.Complete();
    }

    internal static IEnumerable<Rectangle> GenerateWithProgress(int seed, Rectangle protectedLobby, WorldGenProgressLog trace)
    {
        VanillaJungleProfile profile = VanillaJungleProfile.Default;
#if DEBUG
        if (Main.netMode == NetmodeID.Server)
            profile = global::PvPArenas.Common.AdminTools.JungleWorldGen.JungleWorldGenSettings.CaptureProfile();
#endif
        // Capture the recipe when the request is made, before the iterator's first server tick.
        return GenerateCore(seed, protectedLobby, trace, profile);
    }

    private static IEnumerable<Rectangle> GenerateCore(int seed, Rectangle protectedLobby, WorldGenProgressLog trace, VanillaJungleProfile profile)
    {
        if (Main.netMode != NetmodeID.Server) yield break;
        if (!ArenaWorldSystem.IsCompactWorld) throw new InvalidOperationException("The Jungle requires the compact arena world.");
        if (!protectedLobby.IsEmpty) throw new InvalidOperationException("Vanilla Jungle generation replaces the complete arena.");
        generatedLayout = null;
        ArenaLayout layout = CreateLayout();
        // TeamArea uses Main's dimensions. Capture its compact-world bounds before
        // entering the 4200-wide source scope, then translate them into each crop.
        Rectangle relativeRedArea = ArenaRespawns.TeamArea(layout, Terraria.Enums.Team.Red);
        relativeRedArea.Offset(-layout.ArenaBounds.X, -layout.ArenaBounds.Y);
        Point relativePreferredSpawn = layout.RedSpawn - layout.ArenaBounds.Location;
        Rectangle source;
        Crop crop = null;
        using (VanillaJungleSandbox sandbox = new(seed))
        {
            foreach (string pass in profile.Passes)
            {
                if (Main.netMode != NetmodeID.Server) yield break;
                sandbox.Run(pass, trace);
                // Scratch globals have already been restored; no staging state escapes into a server tick.
                yield return Rectangle.Empty;
            }
            if (Main.netMode != NetmodeID.Server) yield break;
            trace.Report("Selecting an underground Jungle section", 0);
            sandbox.Read(() => crop = CaptureCrop(layout.ArenaBounds.Width / 2, layout.ArenaBounds.Height,
                relativeRedArea, relativePreferredSpawn));
            source = crop.Source;
        }

        trace.Report("Copying and mirroring vanilla Jungle", 0);
        ArenaTemplate.ClearEntitiesOutside(Rectangle.Empty);
        Main.worldSurface = Surface;
        Main.rockLayer = Rock;
        Liquid.ReInit();
        int half = Main.maxTilesX / 2;
        for (int start = 0; start < half; start += ColumnsPerStep)
        {
            if (Main.netMode != NetmodeID.Server) yield break;
            int end = Math.Min(half, start + ColumnsPerStep);
            for (int x = start; x < end; x++)
            for (int y = 0; y < Main.maxTilesY; y++)
            {
                Tile tile = Main.tile[x, y];
                tile.ClearEverything();
                tile.HasTile = true;
                tile.TileType = TileID.Mud;
                tile.WallType = WallID.JungleUnsafe;
                if (x >= layout.ArenaBounds.Left && y >= layout.ArenaBounds.Top && y < layout.ArenaBounds.Bottom)
                    crop.CopyTo(x - layout.ArenaBounds.Left, y - layout.ArenaBounds.Top, x, y);
                Tile reflected = Main.tile[Main.maxTilesX - 1 - x, y];
                reflected.CopyFrom(tile);
                reflected.Slope = Reflect(tile.Slope);
            }
            trace.Report("Copying and mirroring vanilla Jungle", end / (double)half);
            yield return new Rectangle(start, 0, end - start, Main.maxTilesY);
            yield return new Rectangle(Main.maxTilesX - end, 0, end - start, Main.maxTilesY);
        }
        // Multi-tile objects retain normal frame order on each side, rather than reversed chest/plant frames.
        crop.PlaceObjects(layout.ArenaBounds.Location);
        trace.Report("Resolving natural cave spawns", 0);
        layout = ResolveAnchors(layout);
        generatedLayout = layout;
        Main.spawnTileX = layout.BossSpawn.X;
        Main.spawnTileY = layout.BossSpawn.Y + 10;

        trace.Report("Framing completed vanilla Jungle", 0);
        for (int start = 6; start < Main.maxTilesX - 6; start += ColumnsPerStep)
        {
            if (Main.netMode != NetmodeID.Server) yield break;
            int end = Math.Min(Main.maxTilesX - 6, start + ColumnsPerStep);
            bool previousLiquidCheck = WorldGen.noLiquidCheck;
            Terraria.Utilities.UnifiedRandom previousGenRandom = WorldGen._genRand, previousRandom = Main.rand;
            int previousSeed = WorldGen._lastSeed, previousRandomSeed = WorldGen._genRandSeed;
            try
            {
                int frameSeed = unchecked(seed ^ start * 7919);
                WorldGen._lastSeed = WorldGen._genRandSeed = frameSeed;
                WorldGen._genRand = new Terraria.Utilities.UnifiedRandom(frameSeed);
                Main.rand = new Terraria.Utilities.UnifiedRandom(frameSeed);
                WorldGen.noLiquidCheck = true;
                for (int x = start; x < end; x++)
                for (int y = 6; y < Main.maxTilesY - 6; y++)
                {
                    WorldGen.TileFrame(x, y, noBreak: true);
                    Framing.WallFrame(x, y);
                }
            }
            finally
            {
                WorldGen.noLiquidCheck = previousLiquidCheck;
                WorldGen._genRand = previousGenRandom;
                WorldGen._lastSeed = previousSeed;
                WorldGen._genRandSeed = previousRandomSeed;
                Main.rand = previousRandom;
            }
            trace.Report("Framing completed vanilla Jungle", end / (double)Main.maxTilesX);
            yield return new Rectangle(start, 0, end - start, Main.maxTilesY);
        }
        Liquid.ReInit();
        JungleMapExport.TryExport(seed, profile.Passes, layout.ArenaBounds, source, "Terraria vanilla C# GenPasses; underground crop mirrored horizontally");
    }

    private static Crop CaptureCrop(int width, int height, Rectangle relativeRedArea, Point relativePreferredSpawn)
    {
        // Integral counts make the search finite and cheap. The source is entirely below the deepest surface.
        int worldWidth = Main.maxTilesX, worldHeight = Main.maxTilesY;
        int top = Math.Max((int)Math.Ceiling(Main.worldSurface) + 40, (int)Math.Ceiling(Main.rockLayer));
        int bottom = worldHeight - 210 - height;
        int stride = worldWidth + 1;
        int[] jungle = new int[stride * (worldHeight + 1)], open = new int[jungle.Length];
        for (int y = 0; y < worldHeight; y++)
        {
            int rowJungle = 0, rowOpen = 0;
            for (int x = 0; x < worldWidth; x++)
            {
                Tile tile = Main.tile[x, y];
                if (tile.HasTile && tile.TileType is TileID.Mud or TileID.JungleGrass or TileID.Hive) rowJungle++;
                if (!Solid(x, y) && tile.LiquidAmount == 0) rowOpen++;
                jungle[(y + 1) * stride + x + 1] = jungle[y * stride + x + 1] + rowJungle;
                open[(y + 1) * stride + x + 1] = open[y * stride + x + 1] + rowOpen;
            }
        }
        int Count(int[] sums, int x, int y) => sums[(y + height) * stride + x + width] - sums[y * stride + x + width]
            - sums[(y + height) * stride + x] + sums[y * stride + x];
        List<(Rectangle Bounds, double Score)> candidates = [];
        for (int y = top; y <= bottom; y += 12)
        for (int x = 250; x + width < worldWidth - 250; x += 12)
        {
            double dry = Count(open, x, y) / (double)(width * height);
            if (dry < .12 || dry > .55) continue;
            double score = Count(jungle, x, y) - Math.Abs(dry - .30) * width * height * .35;
            candidates.Add((new Rectangle(x, y, width, height), score));
        }
        foreach (var candidate in candidates.OrderByDescending(candidate => candidate.Score).Take(256))
            if (HasCaveApproach(candidate.Bounds, relativeRedArea, relativePreferredSpawn)) return new Crop(candidate.Bounds);
        throw new InvalidOperationException("The selected vanilla passes produced no connected underground Jungle section with safe team respawn floors near a mirror seam.");
    }

    private static bool HasCaveApproach(Rectangle area, Rectangle relativeRedArea, Point relativePreferredSpawn)
    {
        Rectangle teamArea = relativeRedArea;
        teamArea.Offset(area.Location);
        Point preferred = area.Location + relativePreferredSpawn;
        bool[] reached = new bool[area.Width * area.Height];
        int Index(Point p) => (p.Y - area.Y) * area.Width + p.X - area.X;
        bool Body(Point p)
        {
            if (p.X <= area.Left + 1 || p.X >= area.Right || p.Y <= area.Top + 4 || p.Y >= area.Bottom - 14) return false;
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -3; dy < 0; dy++)
                if (Solid(p.X + dx, p.Y + dy) || Main.tile[p.X + dx, p.Y + dy].LiquidAmount != 0) return false;
            return true;
        }
        Point[] directions = [new(-1, 0), new(1, 0), new(0, -1), new(0, 1)];
        for (int y = area.Top + 5; y < area.Bottom - 14; y++)
        for (int x = area.Left + 2; x < area.Right; x++)
        {
            Point start = new(x, y);
            if (reached[Index(start)] || !Body(start)) continue;
            Queue<Point> queue = new(); queue.Enqueue(start); reached[Index(start)] = true;
            List<Point> cave = [];
            while (queue.TryDequeue(out Point p))
            {
                cave.Add(p);
                foreach (Point direction in directions)
                {
                    Point next = p + direction;
                    if (!area.Contains(next) || reached[Index(next)] || !Body(next)) continue;
                    reached[Index(next)] = true; queue.Enqueue(next);
                }
            }
            if (cave.Count < 500) continue;
            Point approach = FindApproach(cave, area, area);
            if (approach != default && TryChooseTeamSpawn(cave, teamArea, preferred, area, approach, area.Right, out _))
                return true;
        }
        return false;
    }

    private static ArenaLayout ResolveAnchors(ArenaLayout layout)
    {
        Rectangle left = new(layout.ArenaBounds.Left, layout.ArenaBounds.Top, layout.ArenaBounds.Width / 2, layout.ArenaBounds.Height);
        // Find the largest natural player-sized cave, then extend only its closest approach to the mirror seam.
        bool[] visited = new bool[left.Width * left.Height];
        List<Point> largest = [];
        Rectangle teamArea = ArenaRespawns.TeamArea(layout, Terraria.Enums.Team.Red);
        Point approach = default;
        int Index(Point p) => (p.Y - left.Top) * left.Width + p.X - left.Left;
        bool Body(Point p) => p.X > left.Left + 1 && p.X < left.Right && p.Y > left.Top + 4 && p.Y < left.Bottom - 14
            && Enumerable.Range(-1, 3).All(dx => Enumerable.Range(-3, 3).All(dy => !Solid(p.X + dx, p.Y + dy)
                && Main.tile[p.X + dx, p.Y + dy].LiquidAmount == 0));
        Point[] directions = [new(-1, 0), new(1, 0), new(0, -1), new(0, 1)];
        for (int y = left.Top + 5; y < left.Bottom - 14; y++)
        for (int x = left.Left + 2; x < left.Right; x++)
        {
            Point start = new(x, y);
            if (visited[Index(start)] || !Body(start)) continue;
            Queue<Point> queue = new();
            List<Point> cave = [];
            visited[Index(start)] = true; queue.Enqueue(start);
            while (queue.TryDequeue(out Point p))
            {
                cave.Add(p);
                foreach (Point d in directions)
                {
                    Point next = p + d;
                    if (!left.Contains(next) || visited[Index(next)] || !Body(next)) continue;
                    visited[Index(next)] = true; queue.Enqueue(next);
                }
            }
            if (cave.Count < 500 || cave.Count <= largest.Count) continue;
            Point candidateApproach = FindApproach(cave, left);
            if (candidateApproach == default || !TryChooseTeamSpawn(cave, teamArea, layout.RedSpawn,
                null, candidateApproach, left.Right, out _)) continue;
            largest = cave;
            approach = candidateApproach;
        }
        if (largest.Count < 500)
            throw new InvalidOperationException("The natural Jungle has no connected cave with two safe team respawn floors and a short mirror-seam connection.");
        for (int x = approach.X - 1; x <= left.Right; x++)
        for (int y = approach.Y - 5; y <= approach.Y + 2; y++) ClearPair(x, y);
        int bossY = approach.Y - 2;
        for (int x = left.Right - 5; x < left.Right; x++)
        for (int y = bossY - 5; y < bossY + 14; y++)
        {
            ClearPair(x, y);
            if (y >= bossY + 10)
            {
                foreach (int px in new[] { x, Main.maxTilesX - 1 - x })
                {
                    Tile tile = Main.tile[px, y]; tile.HasTile = true; tile.TileType = TileID.JungleGrass;
                }
            }
        }
        Point boss = new(left.Right, bossY);
        if (JungleCount(boss) < SceneMetrics.JungleTileThreshold)
            throw new InvalidOperationException("The crop's central cave does not retain the Jungle biome.");
        // Validate the finished terrain. A join near the edge of the team area must
        // not remove its original anchor or the alternate floor used when occupied.
        if (!TryChooseTeamSpawn(largest, teamArea, layout.RedSpawn, null, null, left.Right, out Point red))
            throw new InvalidOperationException("The completed Jungle cave does not retain two safe team respawn floors.");
        return new ArenaLayout(layout.ArenaBounds, new Point(Main.maxTilesX - 1 - red.X, red.Y), red) { BossSpawn = boss };
    }

    private static Point FindApproach(List<Point> cave, Rectangle area, Rectangle? sampleBounds = null) =>
        cave.Where(p => area.Right - p.X <= 90).OrderByDescending(p => p.X)
            .ThenBy(p => Math.Abs(p.Y - area.Center.Y))
            .FirstOrDefault(p => ClearableJoin(p, area.Right, sampleBounds));

    private static bool TryChooseTeamSpawn(List<Point> cave, Rectangle teamArea, Point preferred,
        Rectangle? jungleBounds, Point? plannedApproach, int seam, out Point spawn)
    {
        spawn = default;
        List<Point> floors = cave.Where(p => ArenaRespawns.IsValid(p, teamArea)
                && ArenaGeneration.IsSafeSpawn(p.X, p.Y)
                && (!plannedApproach.HasValue || !ClearsSpawnFloor(p, plannedApproach.Value, seam)))
            .OrderBy(p => Math.Abs(p.X - preferred.X) + Math.Abs(p.Y - preferred.Y)).ToList();
        Dictionary<Point, bool> jungle = [];
        bool HasJungle(Point point)
        {
            if (!jungle.TryGetValue(point, out bool valid))
                jungle[point] = valid = JungleCount(point, jungleBounds, plannedApproach, seam,
                    SceneMetrics.JungleTileThreshold) >= SceneMetrics.JungleTileThreshold;
            return valid;
        }
        foreach (Point floor in floors)
        {
            if (!HasJungle(floor)) continue;
            // Occupancy avoidance treats positions within four tiles as occupied.
            // Leave enough separation for a second usable location on the same cave.
            if (!floors.Any(other => (other.X - floor.X) * (other.X - floor.X)
                    + (other.Y - floor.Y) * (other.Y - floor.Y) >= 36 && HasJungle(other))) continue;
            spawn = floor;
            return true;
        }
        return false;
    }

    private static bool ClearsSpawnFloor(Point floor, Point approach, int seam) =>
        floor.X + 1 >= approach.X - 1 && floor.X - 1 <= seam
        && floor.Y >= approach.Y - 5 && floor.Y <= approach.Y + 2;

    private static void ClearPair(int x, int y)
    {
        foreach (int px in new[] { x, Main.maxTilesX - 1 - x })
        {
            Tile tile = Main.tile[px, y];
            if (ProtectedObject(tile)) continue;
            tile.ClearTile(); tile.LiquidAmount = 0;
        }
    }

    private static bool ClearableJoin(Point approach, int seam, Rectangle? sampleBounds = null)
    {
        for (int x = Math.Min(approach.X - 1, seam - 5); x <= seam; x++)
        for (int y = approach.Y - 7; y < approach.Y + 12; y++)
            if ((!sampleBounds.HasValue || sampleBounds.Value.Contains(x, y)) && ProtectedObject(Main.tile[x, y])) return false;
        return true;
    }

    private static bool ProtectedObject(Tile tile)
    {
        if (!tile.HasTile || !Main.tileFrameImportant[tile.TileType]) return false;
        if (tile.TileType == TileID.JunglePlants) return false;
        TileObjectData shape = TileObjectData.GetTileData(tile);
        return shape == null || shape.Width * shape.Height > 1;
    }

    private static int JungleCount(Point point, Rectangle? bounds = null, Point? plannedApproach = null,
        int seam = 0, int stopAt = int.MaxValue)
    {
        int count = 0;
        for (int x = Math.Max(0, point.X - 84); x <= Math.Min(Main.maxTilesX - 1, point.X + 84); x++)
        for (int y = Math.Max(0, point.Y - 62); y <= Math.Min(Main.maxTilesY - 1, point.Y + 61); y++)
        {
            if (bounds.HasValue && !bounds.Value.Contains(x, y)) continue;
            if (plannedApproach.HasValue)
            {
                Point approach = plannedApproach.Value;
                if (x >= seam - 5 && x < seam && y >= approach.Y - 7 && y < approach.Y + 12)
                {
                    if (y >= approach.Y + 8) count += TileID.Sets.JungleBiome[TileID.JungleGrass];
                    if (count >= stopAt) return count;
                    continue;
                }
                if (x >= approach.X - 1 && x <= seam && y >= approach.Y - 5 && y <= approach.Y + 2) continue;
            }
            Tile tile = Main.tile[x, y];
            if (tile.HasTile) count += TileID.Sets.JungleBiome[tile.TileType];
            if (count >= stopAt) return count;
        }
        return count;
    }

    private static bool Solid(int x, int y) => Main.tile[x, y].HasUnactuatedTile
        && Main.tileSolid[Main.tile[x, y].TileType] && !Main.tileSolidTop[Main.tile[x, y].TileType];

    private static SlopeType Reflect(SlopeType slope) => slope switch
    {
        SlopeType.SlopeDownLeft => SlopeType.SlopeDownRight, SlopeType.SlopeDownRight => SlopeType.SlopeDownLeft,
        SlopeType.SlopeUpLeft => SlopeType.SlopeUpRight, SlopeType.SlopeUpRight => SlopeType.SlopeUpLeft, _ => slope
    };

    private sealed class Crop
    {
        internal Rectangle Source { get; }
        private readonly Array[] data;
        private readonly List<Rectangle> objects = [];
        private readonly List<Chest> chests = [];
        private static Array[] Arrays() => [Main.tile.GetData<TileTypeData>(), Main.tile.GetData<WallTypeData>(),
            Main.tile.GetData<LiquidData>(), Main.tile.GetData<TileWallBrightnessInvisibilityData>(), Main.tile.GetData<TileWallWireStateData>()];

        internal Crop(Rectangle source)
        {
            Source = source;
            HashSet<Point> origins = [];
            for (int x = source.Left; x < source.Right; x++)
            for (int y = source.Top; y < source.Bottom; y++)
            {
                Tile tile = Main.tile[x, y];
                if (!tile.HasTile || !Main.tileFrameImportant[tile.TileType]) continue;
                TileObjectData shape = TileObjectData.GetTileData(tile);
                if (shape == null)
                {
                    // Vanilla's one-tile Jungle plants/spores do not register TileObjectData.
                    if (tile.TileType != TileID.JunglePlants) tile.ClearTile();
                    continue;
                }
                int localX = tile.TileFrameX % shape.CoordinateFullWidth / (shape.CoordinateWidth + shape.CoordinatePadding);
                int frameY = tile.TileFrameY % shape.CoordinateFullHeight, localY = 0;
                while (localY < shape.Height - 1 && frameY >= shape.CoordinateHeights[localY] + shape.CoordinatePadding)
                    frameY -= shape.CoordinateHeights[localY++] + shape.CoordinatePadding;
                Point origin = new(x - localX, y - localY);
                Rectangle bounds = new(origin.X, origin.Y, shape.Width, shape.Height);
                if (!source.Contains(bounds)) { tile.ClearTile(); continue; }
                if (origins.Add(origin)) objects.Add(new Rectangle(origin.X - source.X, origin.Y - source.Y, shape.Width, shape.Height));
            }
            foreach (Chest chest in Main.chest)
                if (chest != null && source.Contains(new Rectangle(chest.x, chest.y, 2, 2)))
                    chests.Add(new Chest { x = chest.x - source.X, y = chest.y - source.Y, name = chest.name,
                        item = chest.item.Select(item => item?.Clone() ?? new Item()).ToArray() });
            data = Arrays().Select(array => Array.CreateInstance(array.GetType().GetElementType(), source.Width * source.Height)).ToArray();
            Array[] current = Arrays();
            for (int x = 0; x < source.Width; x++)
            for (int component = 0; component < data.Length; component++)
                Array.Copy(current[component], (source.X + x) * Main.tile.Height + source.Y,
                    data[component], x * source.Height, source.Height);
        }

        internal void CopyTo(int x, int y, int targetX, int targetY)
        {
            int index = x * Source.Height + y, target = targetX * Main.tile.Height + targetY;
            Main.tile.GetData<TileTypeData>()[target] = ((TileTypeData[])data[0])[index];
            Main.tile.GetData<WallTypeData>()[target] = ((WallTypeData[])data[1])[index];
            Main.tile.GetData<LiquidData>()[target] = ((LiquidData[])data[2])[index];
            Main.tile.GetData<TileWallBrightnessInvisibilityData>()[target] = ((TileWallBrightnessInvisibilityData[])data[3])[index];
            Main.tile.GetData<TileWallWireStateData>()[target] = ((TileWallWireStateData[])data[4])[index];
        }

        internal void PlaceObjects(Point offset)
        {
            foreach (Rectangle obj in objects)
            {
                int right = Main.maxTilesX - offset.X - obj.Right;
                for (int dx = 0; dx < obj.Width; dx++)
                for (int dy = 0; dy < obj.Height; dy++)
                {
                    Tile original = Main.tile[offset.X + obj.X + dx, offset.Y + obj.Y + dy];
                    Tile target = Main.tile[right + dx, offset.Y + obj.Y + dy];
                    target.TileFrameX = original.TileFrameX;
                    target.TileFrameY = original.TileFrameY;
                }
            }
            int slot = 0;
            foreach (Chest chest in chests)
            foreach (int x in new[] { offset.X + chest.x, Main.maxTilesX - offset.X - chest.x - 2 })
                Main.chest[slot++] = new Chest { x = x, y = offset.Y + chest.y, name = chest.name,
                    item = chest.item.Select(item => item.Clone()).ToArray() };
        }
    }
}
