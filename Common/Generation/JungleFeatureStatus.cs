using System;
using System.Collections.Generic;
using System.Linq;
using Terraria.ID;

namespace PvPArenas.Common.Generation;

internal sealed record JungleFeaturePlacement(string Feature, Rectangle Footprint)
{
    internal IReadOnlyDictionary<int, int> RequiredTiles { get; init; }

    internal JungleFeaturePlacement Measure()
    {
        Dictionary<int, int> required = new();
        bool exposedGrass = false;
        for (int x = Footprint.Left; x < Footprint.Right; x++)
        for (int y = Footprint.Top; y < Footprint.Bottom; y++)
        {
            Tile tile = Main.tile[x, y];
            if (tile.HasTile && IsStructureTile(tile.TileType))
                required[tile.TileType] = required.GetValueOrDefault(tile.TileType) + 1;
            exposedGrass |= tile.HasTile && tile.TileType == TileID.JungleGrass && tile.WallType == WallID.None;
        }
        bool completeSurface = Feature != "SurfaceJungle"
            || required.GetValueOrDefault(TileID.Trees) >= 5 && exposedGrass;
        return this with { RequiredTiles = completeSurface ? required : null };
    }

    internal bool Survives()
    {
        if (RequiredTiles == null || RequiredTiles.Count == 0) return false;
        Dictionary<int, int> retained = new();
        bool exposedGrass = false;
        for (int x = Footprint.Left; x < Footprint.Right; x++)
        for (int y = Footprint.Top; y < Footprint.Bottom; y++)
        {
            Tile tile = Main.tile[x, y];
            if (tile.HasTile && RequiredTiles.ContainsKey(tile.TileType))
                retained[tile.TileType] = retained.GetValueOrDefault(tile.TileType) + 1;
            exposedGrass |= tile.HasTile && tile.TileType == TileID.JungleGrass && tile.WallType == WallID.None;
        }
        bool complete = RequiredTiles.All(pair => retained.GetValueOrDefault(pair.Key) >= pair.Value)
            && (Feature != "SurfaceJungle" || retained.GetValueOrDefault(TileID.Trees) >= 5 && exposedGrass);
        if (!complete) Log.Debug($"[worldgen] {Feature} lost retained tiles in {Footprint}: "
            + string.Join(", ", RequiredTiles.Where(pair => retained.GetValueOrDefault(pair.Key) < pair.Value)
                .Select(pair => $"type {pair.Key}: {retained.GetValueOrDefault(pair.Key)}/{pair.Value}")));
        return complete;
    }

    private bool IsStructureTile(ushort type) => Feature switch
    {
        "Hive" => type is TileID.Hive or TileID.HoneyBlock or TileID.Larva,
        "Mahogany" => type is TileID.LivingMahogany or TileID.LivingMahoganyLeaves or TileID.Containers,
        "Shrine" => type is TileID.RichMahogany or 45 or 119 or 120 or 175 or TileID.Containers,
        "Cabin" => type is TileID.RichMahogany or TileID.RichMahoganyBeam or TileID.Containers or TileID.Tables or TileID.Chairs
            or TileID.WorkBenches or TileID.Bookcases or TileID.ClosedDoor or TileID.Platforms,
        "Temple" => type is TileID.LihzahrdBrick or TileID.LihzahrdAltar or TileID.Containers
            or TileID.ClosedDoor or TileID.Traps or TileID.PressurePlates or TileID.Statues,
        "SurfaceJungle" => type is TileID.Trees or TileID.JungleGrass,
        _ => false
    };
}
internal sealed record JungleFeatureCheck(string Feature, int Count, bool Passed);

/// <summary>Actual retained features, measured again after copying and final framing.</summary>
internal sealed record JungleFeatureStatus(IReadOnlyList<JungleFeatureCheck> Checks)
{
    internal bool Passed => Checks.All(check => check.Passed);
    internal string Failure => string.Join(", ", Checks.Where(check => !check.Passed).Select(check => check.Feature));

    internal void RequireComplete()
    {
        if (!Passed) throw new InvalidOperationException("The Jungle is missing: " + Failure + ".");
    }

    internal static JungleFeatureStatus Capture(Rectangle bounds, IReadOnlyList<JungleFeaturePlacement> structures)
    {
        Dictionary<int, int> tiles = new(), walls = new();
        int water = 0, honey = 0, surface = 0, dryOpen = 0;
        for (int x = bounds.Left; x < bounds.Right; x++)
        for (int y = bounds.Top; y < bounds.Bottom; y++)
        {
            Tile tile = Main.tile[x, y];
            if (!tile.HasTile && tile.LiquidAmount == 0) dryOpen++;
            if (tile.HasTile) tiles[tile.TileType] = tiles.GetValueOrDefault(tile.TileType) + 1;
            if (tile.WallType != WallID.None) walls[tile.WallType] = walls.GetValueOrDefault(tile.WallType) + 1;
            if (tile.LiquidAmount > 0)
            {
                if (tile.LiquidType == LiquidID.Water) water++;
                if (tile.LiquidType == LiquidID.Honey) honey++;
            }
            if (tile.HasTile && tile.TileType == TileID.JungleGrass && tile.WallType == WallID.None && y < bounds.Top + 100)
                surface++;
        }
        int Count(params int[] types) => types.Sum(type => tiles.GetValueOrDefault(type));
        List<JungleFeatureCheck> checks = [];
        void Add(string feature, int count) => checks.Add(new(feature, count, count > 0));
        void Landmark(string feature, int count)
        {
            JungleFeaturePlacement[] matches = structures?.Where(item => item.Feature == feature).ToArray() ?? [];
            bool complete = matches.Length > 0 && matches.All(item => bounds.Contains(item.Footprint) && item.Survives());
            checks.Add(new(feature, count, count > 0 && complete));
        }
        Add("Jungle terrain", Count(TileID.Mud, TileID.JungleGrass));
        Add("Jungle grass", Count(TileID.JungleGrass));
        Add("Natural caves", dryOpen);
        Add("Clay", Count(TileID.ClayBlock));
        Add("Dirt", Count(TileID.Dirt));
        Add("Stone", Count(TileID.Stone));
        Add("Cave walls", walls.Values.Sum());
        Add("Water", water);
        Add("Honey", honey);
        Add("Water plants", Count(TileID.LilyPad, TileID.Cattail, TileID.Bamboo));
        Landmark("Hive", Count(TileID.Hive));
        Add("Larvae", Count(TileID.Larva));
        Landmark("Mahogany", Count(TileID.LivingMahogany));
        Add("Living mahogany leaves", Count(TileID.LivingMahoganyLeaves));
        Landmark("Cabin", Count(TileID.RichMahogany));
        Landmark("Shrine", Count(TileID.Containers));
        Add("Chests", Count(TileID.Containers, TileID.Containers2));
        Landmark("Temple", Count(TileID.LihzahrdBrick));
        Add("Lihzahrd altar", Count(TileID.LihzahrdAltar));
        Add("Ordinary trees", Count(TileID.Trees));
        Add("Small jungle plants", Count(TileID.JunglePlants, TileID.JunglePlants2));
        Add("Large jungle plants", Count(TileID.PlantDetritus));
        Add("Vines", Count(TileID.JungleVines));
        Add("Pots", Count(TileID.Pots));
        Add("Rubble", Count(TileID.SmallPiles, TileID.LargePiles, TileID.LargePiles2));
        Add("Stalactites", Count(TileID.Stalactite));
        Add("Life Crystals", Count(TileID.Heart));
        Add("Ores", Count(TileID.Copper, TileID.Tin, TileID.Iron, TileID.Lead, TileID.Silver, TileID.Tungsten, TileID.Gold, TileID.Platinum));
        Add("Gems", Count(TileID.Amethyst, TileID.Topaz, TileID.Sapphire, TileID.Emerald, TileID.Ruby, TileID.Diamond));
        Add("Cobwebs", Count(TileID.Cobweb));
        Add("Spider", walls.GetValueOrDefault(WallID.SpiderUnsafe));
        Add("Mushroom", Count(TileID.MushroomGrass));
        Add("Marble", Count(TileID.Marble));
        Add("Granite", Count(TileID.Granite));
        Add("Tracks", Count(TileID.MinecartTrack));
        Add("Traps", Count(TileID.Traps, TileID.Explosives));
        Add("Statues", Count(TileID.Statues));
        Add("Cabin furniture", Count(TileID.Tables, TileID.Chairs, TileID.WorkBenches, TileID.Bookcases));
        Add("LifeFruit", Count(TileID.LifeFruit));
        Add("PlanteraBulb", Count(TileID.PlanteraBulb));
        Add("Chlorophyte", Count(TileID.Chlorophyte));
        Landmark("SurfaceJungle", surface);
        if (ArenaWorldSystem.IsCompactWorld && bounds == ArenaWorldSystem.PlayBounds)
            checks.Add(new("Mirrored terrain", bounds.Width * bounds.Height / 2, IsMirrored(bounds)));
        return new(checks);
    }

    private static bool IsMirrored(Rectangle bounds)
    {
        for (int x = bounds.Left; x < bounds.Center.X; x++)
        for (int y = bounds.Top; y < bounds.Bottom; y++)
        {
            Tile left = Main.tile[x, y], right = Main.tile[Main.maxTilesX - 1 - x, y];
            if (left.HasTile != right.HasTile || left.WallType != right.WallType
                || left.LiquidAmount != right.LiquidAmount || left.LiquidAmount > 0 && left.LiquidType != right.LiquidType
                || left.RedWire != right.RedWire || left.BlueWire != right.BlueWire || left.GreenWire != right.GreenWire
                || left.YellowWire != right.YellowWire || left.HasActuator != right.HasActuator || left.IsActuated != right.IsActuated)
                return Mismatch(x, y, left, right);
            SlopeType reflected = right.Slope switch
            {
                SlopeType.SlopeDownLeft => SlopeType.SlopeDownRight, SlopeType.SlopeDownRight => SlopeType.SlopeDownLeft,
                SlopeType.SlopeUpLeft => SlopeType.SlopeUpRight, SlopeType.SlopeUpRight => SlopeType.SlopeUpLeft, _ => right.Slope
            };
            if (left.HasTile && (left.TileType != right.TileType || left.IsHalfBlock != right.IsHalfBlock || left.Slope != reflected))
                return Mismatch(x, y, left, right);
        }
        return true;

        static bool Mismatch(int x, int y, Tile left, Tile right)
        {
            Log.Debug($"[worldgen] Jungle mirror mismatch at {x},{y}: active={left.HasTile}/{right.HasTile}, "
                + $"type={left.TileType}/{right.TileType}, frame={left.TileFrameX},{left.TileFrameY}/{right.TileFrameX},{right.TileFrameY}, "
                + $"wall={left.WallType}/{right.WallType}, liquid={left.LiquidAmount}/{right.LiquidAmount}, slope={left.Slope}/{right.Slope}.");
            return false;
        }
    }
}
