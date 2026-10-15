using System;
using System.Collections.Generic;
using System.Linq;
using Terraria.GameContent.Biomes;
using Terraria.GameContent.Biomes.CaveHouse;
using Terraria.ID;
using Terraria.WorldBuilding;

namespace PvPArenas.Common.Generation;

/// <summary>Adds real vanilla landmarks to a natural source crop without accepting clipped structures.</summary>
internal static class VanillaJungleLandmarks
{
    private static Rectangle[] existingHives = [], existingShrines = [];
    private static object discoveryWorld;
    private static int discoveryWidth, discoveryHeight;
    internal static string LastFailure { get; private set; } = "";
    internal static int ExistingHiveCount => existingHives.Length;
    internal static int ExistingShrineCount => existingShrines.Length;

    internal readonly record struct Args(Rectangle Bounds, IReadOnlyList<Rectangle> Reserved,
        Rectangle KnownHive, Rectangle KnownShrine)
    {
        internal Args(Rectangle bounds, IReadOnlyList<Rectangle> reserved) : this(bounds, reserved, default, default) { }
    }
    internal sealed record Inventory(Rectangle Hive, Rectangle Mahogany, Rectangle Shrine, Rectangle Cabin)
    {
        internal IReadOnlyList<Rectangle> ProtectedBounds => new[] { Hive, Mahogany, Shrine, Cabin };
    }

    internal static bool TryPopulate(Rectangle bounds, out Inventory inventory) =>
        TryPopulate(new Args(bounds, Array.Empty<Rectangle>()), out inventory);

    /// <summary>Build once before crop selection; queries retain rectangles, never source tile storage.</summary>
    internal static void PrepareExisting()
    {
        ClearExisting();
        Rectangle world = new(10, 10, Main.maxTilesX - 20, Main.maxTilesY - 20);
        existingHives = Components(world, IsHive).Where(CompleteHive).ToArray();
        existingShrines = Components(world, IsShrine).Where(CompleteShrine).ToArray();
        discoveryWorld = Main.ActiveWorldFileData;
        discoveryWidth = Main.maxTilesX; discoveryHeight = Main.maxTilesY;
    }

    internal static void ClearExisting()
    {
        existingHives = existingShrines = [];
        discoveryWorld = null;
        discoveryWidth = discoveryHeight = 0;
        LastFailure = "";
    }

    /// <summary>Cheap, non-mutating rejection before attempting the larger vanilla structures.</summary>
    internal static bool TryFindExisting(Rectangle bounds, out Rectangle hive, out Rectangle shrine)
    {
        LastFailure = "";
        hive = shrine = Rectangle.Empty;
        if (bounds.IsEmpty || !new Rectangle(10, 10, Main.maxTilesX - 20, Main.maxTilesY - 20).Contains(bounds))
            return Fail("Existing landmarks: crop is empty or outside the source world.");
        bool prepared = ReferenceEquals(discoveryWorld, Main.ActiveWorldFileData)
            && discoveryWidth == Main.maxTilesX && discoveryHeight == Main.maxTilesY;
        hive = prepared ? existingHives.FirstOrDefault(bounds.Contains)
            : FindContained(bounds, Array.Empty<Rectangle>(), IsHive, CompleteHive);
        if (hive.IsEmpty) return Fail("Existing landmarks: no whole hive with a complete larva fits this crop.");
        Rectangle selectedHive = hive;
        shrine = prepared ? existingShrines.FirstOrDefault(area => bounds.Contains(area) && !area.Intersects(selectedHive))
            : FindContained(bounds, new[] { hive }, IsShrine, CompleteShrine);
        return !shrine.IsEmpty || Fail("Existing landmarks: no whole chest shrine fits beside the hive.");
    }

    internal static bool TryPopulate(Args args, out Inventory inventory)
    {
        LastFailure = "";
        inventory = null;
        Rectangle bounds = args.Bounds;
        if (bounds.Width < 240 || bounds.Height < 240
            || !new Rectangle(10, 10, Main.maxTilesX - 20, Main.maxTilesY - 20).Contains(bounds))
            return Fail("Core landmarks: crop is too small or outside the source world.");
        List<Rectangle> reserved = args.Reserved == null ? [] : new(args.Reserved);
        Rectangle hive = args.KnownHive.IsEmpty ? FindContained(bounds, reserved, IsHive, CompleteHive) : args.KnownHive;
        if (!bounds.Contains(hive) || !CompleteHive(hive))
            return Fail("Core landmarks: a whole hive with a complete larva is missing.");
        reserved.Add(hive);
        Rectangle shrine = args.KnownShrine.IsEmpty ? FindContained(bounds, reserved, IsShrine, CompleteShrine) : args.KnownShrine;
        if (!bounds.Contains(shrine) || !CompleteShrine(shrine))
            return Fail("Core landmarks: a whole shrine with an Ivy Chest is missing.");
        reserved.Add(shrine);

        using VanillaJungleTransaction population = new();
        if (!TryMahogany(bounds, reserved, out Rectangle mahogany)) return false;
        reserved.Add(mahogany);
        if (!TryCabin(bounds, reserved, out Rectangle cabin)) return false;
        if (!population.TryCommit(bounds, args.Reserved ?? Array.Empty<Rectangle>(), out _))
            return Fail("Core landmarks: " + population.LastFailure);
        inventory = new Inventory(hive, mahogany, shrine, cabin);
        return true;
    }

    private static bool TryMahogany(Rectangle bounds, IReadOnlyList<Rectangle> reserved, out Rectangle footprint)
    {
        footprint = Rectangle.Empty;
        MahoganyTreeBiome biome = GenVars.configuration.CreateBiome<MahoganyTreeBiome>();
        using VanillaJungleTransaction attempt = new();
        List<Point> origins = [];
        for (int y = bounds.Top + 65; y < bounds.Bottom - 45; y++)
        for (int x = bounds.Left + 50; x < bounds.Right - 50; x++)
        {
            Tile tile = Main.tile[x, y];
            if (!tile.HasTile && tile.LiquidAmount == 0 && !reserved.Any(area => area.Contains(x, y)))
                origins.Add(new Point(x, y));
        }
        VanillaJungleExtras.Shuffle(origins);
        HashSet<Point> floors = [];
        StructureMap localStructures = new();
        int attempts = 0, placed = 0, contained = 0;
        string last = "no native floor was found below an air position";
        bool Missing() => Fail($"Mahogany: air positions={origins.Count}, unique native floors={floors.Count}, attempts={attempts}, vanilla placements={placed}, "
            + $"contained={contained}; last rejection: {last}.");
        // Use the exact native floor search only to deduplicate origins. Let the
        // biome check its own ceiling, slopes and surrounding Jungle material.
        // Full-world protection boxes may belong to structures discarded by the
        // crop; only selected features are reserved in this placement's map.
        foreach (Point origin in origins)
        {
            if (!WorldUtils.Find(new Point(origin.X - 3, origin.Y), Searches.Chain(new Searches.Down(200),
                new Conditions.IsSolid().AreaAnd(6, 1)), out Point floor) || !floors.Add(floor)) continue;
            if (attempts >= 512) return Missing();
            attempts++;
            localStructures.Reset();
            foreach (Rectangle area in reserved) localStructures.AddProtectedStructure(area);
            if (!biome.Place(origin, localStructures))
            { last = "vanilla placement rejected terrain or a protected structure"; continue; }
            placed++;
            if (!attempt.TryCommit(bounds, reserved, out Rectangle changed))
            { last = attempt.LastFailure; continue; }
            contained++;
            int wood = Count(changed, TileID.LivingMahogany), leaves = Count(changed, TileID.LivingMahoganyLeaves);
            bool chest = HasChest(changed, 10);
            if (wood >= 50 && leaves >= 10 && chest)
            {
                footprint = changed;
                GenVars.structures.AddProtectedStructure(changed, 4);
                return true;
            }
            last = $"incomplete tree: wood={wood}, leaves={leaves}, chest={chest}";
            attempt.Restore();
        }
        return Missing();
    }

    private static bool TryCabin(Rectangle bounds, IReadOnlyList<Rectangle> reserved, out Rectangle footprint)
    {
        footprint = Rectangle.Empty;
        using VanillaJungleTransaction attempt = new();
        int available = 0, caves = 0, attempts = 0, contained = 0;
        string last = "no unprotected room site";
        bool Missing() => Fail($"Cabin: available rooms={available}, cave rooms={caves}, attempts={attempts}, "
            + $"contained={contained}; last rejection: {last}.");
        // Use Terraria's Jungle builder directly: CaveHouseBiome chooses room
        // rectangles itself and could select a different biome or leave this crop.
        for (int y = bounds.Top + 24; y < bounds.Bottom - 55; y += 8)
        for (int x = bounds.Left + 26; x < bounds.Right - 60; x += 12)
        {
            Rectangle room = new(x, y, 24, 14);
            if (!GenVars.structures.CanPlace(room, 8)) continue;
            available++;
            int open = 0;
            for (int xx = room.Left; xx < room.Right; xx++)
            for (int yy = room.Top; yy < room.Bottom; yy++)
                if (!WorldGen.SolidTile(xx, yy) && Main.tile[xx, yy].LiquidAmount == 0) open++;
            if (open < room.Width * room.Height / 5 || open > room.Width * room.Height * 4 / 5) continue;
            caves++;
            if (attempts >= 48) return Missing();
            attempts++;
            JungleHouseBuilder builder = new([room]) { ChestChance = 1.0 };
            builder.Place(new HouseBuilderContext(), GenVars.structures);
            if (!attempt.TryCommit(bounds, reserved, out Rectangle changed))
            { last = attempt.LastFailure; continue; }
            contained++;
            bool chest = HasChest(room, 8);
            int wood = Count(changed, TileID.RichMahogany);
            if (chest && wood >= 12)
            {
                footprint = changed;
                GenVars.structures.AddProtectedStructure(changed, 4);
                return true;
            }
            last = $"incomplete cabin: wood={wood}, interior chest={chest}";
            attempt.Restore();
        }
        return Missing();
    }

    private static Rectangle FindContained(Rectangle bounds, IReadOnlyList<Rectangle> reserved,
        Func<Tile, bool> includes, Func<Rectangle, bool> complete)
        => Components(bounds, includes).FirstOrDefault(component => !reserved.Any(area => area.Intersects(component)) && complete(component));

    private static IEnumerable<Rectangle> Components(Rectangle bounds, Func<Tile, bool> includes)
    {
        bool[] visited = new bool[bounds.Width * bounds.Height];
        int Index(int x, int y) => (x - bounds.X) * bounds.Height + y - bounds.Y;
        Point[] directions = [new(-1, 0), new(1, 0), new(0, -1), new(0, 1)];
        for (int x = bounds.Left; x < bounds.Right; x++)
        for (int y = bounds.Top; y < bounds.Bottom; y++)
        {
            if (visited[Index(x, y)] || !includes(Main.tile[x, y])) continue;
            Queue<Point> pending = new(); pending.Enqueue(new Point(x, y)); visited[Index(x, y)] = true;
            int left = x, right = x, top = y, bottom = y;
            bool clipped = false;
            while (pending.TryDequeue(out Point point))
            {
                left = Math.Min(left, point.X); right = Math.Max(right, point.X);
                top = Math.Min(top, point.Y); bottom = Math.Max(bottom, point.Y);
                foreach (Point direction in directions)
                {
                    Point next = point + direction;
                    if (!bounds.Contains(next)) { clipped = true; continue; }
                    if (visited[Index(next.X, next.Y)] || !includes(Main.tile[next.X, next.Y])) continue;
                    visited[Index(next.X, next.Y)] = true; pending.Enqueue(next);
                }
            }
            Rectangle component = new(left, top, right - left + 1, bottom - top + 1);
            if (!clipped) yield return component;
        }
    }

    private static bool Fail(string reason) { LastFailure = reason; return false; }

    private static bool IsHive(Tile tile) => tile.WallType == WallID.HiveUnsafe
        || tile.HasTile && tile.TileType is TileID.Hive or TileID.HoneyBlock or TileID.Larva;

    private static bool IsShrine(Tile tile) => tile.HasTile && tile.TileType == GenVars.jungleHut
        || tile.WallType is 23 or 24 or 42 or 45 or 10;

    private static bool CompleteHive(Rectangle component) => !component.IsEmpty
        && Count(component, TileID.Hive) >= 100 && Count(component, TileID.Larva) >= 9;

    private static bool CompleteShrine(Rectangle component) => !component.IsEmpty
        && HasChest(component, 10) && Count(component, GenVars.jungleHut) >= 12;

    private static int Count(Rectangle bounds, ushort type)
    {
        int count = 0;
        for (int x = bounds.Left; x < bounds.Right; x++)
        for (int y = bounds.Top; y < bounds.Bottom; y++)
            if (Main.tile[x, y].HasTile && Main.tile[x, y].TileType == type) count++;
        return count;
    }

    private static bool HasChest(Rectangle bounds, int style) => Main.chest.Any(chest => chest != null
        && bounds.Contains(new Rectangle(chest.x, chest.y, 2, 2))
        && Main.tile[chest.x, chest.y].HasTile && Main.tile[chest.x, chest.y].TileType == TileID.Containers
        && Main.tile[chest.x, chest.y].TileFrameX / 36 == style);
}
