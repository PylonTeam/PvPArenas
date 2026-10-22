using Newtonsoft.Json.Linq;
using PvPArenas.Common.Game;
using PvPArenas.Common.Generation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Terraria.Enums;
using Terraria.ID;
using Terraria.IO;
using Terraria.WorldBuilding;

namespace PvPArenas.Common.CultistArena;

/// <summary>The selected native forest/Dungeon/Underworld world, generated directly at its playable dimensions.</summary>
internal static class CultistArenaGenerator
{
    internal const int Width = 850, Height = 1200, DefaultSeed = 1234;
    private const int Margin = 20, ColumnsPerStep = 32;
    private static ArenaLayout generatedLayout;
    internal static string LastSourceFingerprint { get; private set; }
    internal static string LastPublishedFingerprint { get; private set; }
    internal static int LastConstructionIterations { get; private set; }
    internal static int LastRoomCount { get; private set; }
    internal static int LastWorkloadWidthLoads { get; private set; }

    // Installed Terraria pass order. Unrelated biomes and the Shimmer-dependent Final Cleanup
    // are omitted. Dungeon and Underworld themselves retain their native structures and loot.
    internal static readonly IReadOnlyList<string> Passes = Array.AsReadOnly(new[]
    {
        "Reset", "Terrain", "Dirt Wall Backgrounds", "Rocks In Dirt", "Dirt In Rocks", "Clay",
        "Small Holes", "Dirt Layer Caves", "Rock Layer Caves", "Surface Caves", "Wavy Caves", "Grass",
        "Silt", "Shinies", "Underworld", "Lakes", "Dungeon", "Mountain Caves", "Gems", "Clean Up Dirt",
        "Dirt Rock Wall Runner", "Living Trees", "Wood Tree Walls", "Settle Liquids", "Smooth World",
        "Wall Variety", "Life Crystals", "Statues", "Buried Chests", "Surface Chests", "Cave Walls",
        "Quick Cleanup", "Pots", "Hellforge", "Spreading Grass", "Surface Ore and Stone", "Place Fallen Log",
        "Piles", "Grass Wall", "Sunflowers", "Planting Trees", "Herbs", "Weeds", "Vines", "Flowers",
        "Mushrooms", "Random Gems", "Settle Liquids Again", "Tile Cleanup", "Water Plants", "Stalac",
        "Remove Broken Traps"
    });

    internal static ArenaLayout CreateLayout() => generatedLayout;

    internal static void ResetLayout()
    {
        generatedLayout = null;
        LastSourceFingerprint = LastPublishedFingerprint = null;
        LastConstructionIterations = LastRoomCount = LastWorkloadWidthLoads = 0;
    }

    internal static IEnumerable<Rectangle> GenerateWithProgress(int seed, WorldGenProgressLog trace)
    {
        if (Main.netMode != NetmodeID.Server) yield break;
        if (Main.maxTilesX != Width || Main.maxTilesY != Height)
            throw new InvalidOperationException("The Cultist arena requires the fixed 850x1200 arena session world.");
        ResetLayout();
        ClearActors();
        CultistWorldSnapshot snapshot;
        using (VanillaJungleSandbox sandbox = new(seed, Width, Height))
        {
            sandbox.Configure(() =>
            {
                // Reset uses these values to choose a legal Dungeon location before terrain exists.
                GenVars.beachSandRandomCenter = 35;
                GenVars.beachSandRandomWidthRange = 5;
                GenVars.beachSandDungeonExtraWidth = GenVars.beachSandJungleExtraWidth = 0;
            });
            foreach (string pass in Passes)
            {
                if (Main.netMode != NetmodeID.Server) yield break;
                if (pass == "Dungeon")
                {
                    // All hooks are gone before the source scope returns control to a server tick.
                    using CultistDungeonScale scale = new(Width, Height, 4200);
                    sandbox.Run(pass, trace);
                    LastConstructionIterations = scale.ConstructionIterations;
                    LastWorkloadWidthLoads = scale.WorkloadWidthLoads;
                }
                else sandbox.Run(pass, trace, pass == "Buried Chests" ? WithoutDesertHouses : null);
                yield return Rectangle.Empty;
            }
            trace?.Report("Validating the selected Cultist world", 0);
            sandbox.Read(() =>
            {
                LastSourceFingerprint = CultistWorldSnapshot.SemanticFingerprint();
                LastRoomCount = GenVars.numDRooms;
                // Final Cleanup also resets temporary solidity flags; restore only those flags,
                // without running its mandatory Shimmer biome cleanup or adding any terrain.
                Main.tileSolid[659] = Main.tileSolid[GenVars.crackedType] = Main.tileSolid[484] = true;
            });
            trace?.Report("Framing the native Cultist world", 0);
            for (int start = 0; start < Width - 1; start += ColumnsPerStep)
            {
                int first = start, end = Math.Min(start + ColumnsPerStep, Width - 1);
                sandbox.Read(() =>
                {
                    for (int x = first; x < end; x++)
                    for (int y = 0; y < Height - 1; y++)
                        WorldGen.Reframe(x, y, resetFrame: true);
                });
                trace?.Report("Framing the native Cultist world", end / (double)Width);
                yield return Rectangle.Empty;
            }
            snapshot = null;
            sandbox.Read(() => snapshot = new CultistWorldSnapshot());
        }

        // Publish complete value data into the existing session allocation. NPCs created by
        // world generation (including the Old Man) are deliberately absent from the snapshot.
        ArenaTemplate.ClearEntitiesOutside(Rectangle.Empty);
        snapshot.ApplyMetadata();
        for (int start = 0; start < Width; start += ColumnsPerStep)
        {
            if (Main.netMode != NetmodeID.Server) yield break;
            int end = Math.Min(start + ColumnsPerStep, Width);
            snapshot.CopyColumns(start, end);
            trace?.Report("Publishing the Cultist arena", end / (double)Width);
            yield return new Rectangle(start, 0, end - start, Height);
        }
        snapshot.ApplyEntities();
        generatedLayout = ResolveLayout();
        Main.spawnTileX = generatedLayout.RedSpawn.X;
        Main.spawnTileY = generatedLayout.RedSpawn.Y;
        Liquid.ReInit();
        LastPublishedFingerprint = CultistWorldSnapshot.SemanticFingerprint();
        Log.Info($"Cultist arena seed={seed}, source={LastSourceFingerprint}, published={LastPublishedFingerprint}, "
            + $"Dungeon iterations={LastConstructionIterations}, rooms={LastRoomCount}, workload patches={LastWorkloadWidthLoads}.");
    }

    private static GameConfiguration WithoutDesertHouses(GameConfiguration original)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        FieldInfo root = typeof(GameConfiguration).GetFields(flags).Single(field => typeof(JObject).IsAssignableFrom(field.FieldType));
        JObject json = (JObject)((JObject)root.GetValue(original)).DeepClone();
        JToken entry = json["AdditionalDesertHouseCount"]
            ?? throw new InvalidOperationException("The native chest pass no longer exposes its desert-house count.");
        Zero(entry);
        return new GameConfiguration(json);

        static void Zero(JToken token)
        {
            if (token is JValue value && value.Type is JTokenType.Integer or JTokenType.Float) value.Value = 0;
            else foreach (JToken child in token.Children()) Zero(child);
        }
    }

    private static ArenaLayout ResolveLayout()
    {
        Rectangle bounds = new(Margin, Margin, Width - Margin * 2, Height - Margin * 2);
        ArenaLayout search = new(bounds, default, default);
        List<Point> redFloors = SurfaceFloors(ArenaRespawns.TeamArea(search, Team.Red));
        List<Point> blueFloors = SurfaceFloors(ArenaRespawns.TeamArea(search, Team.Blue));
        if (redFloors.Count == 0 || blueFloors.Count == 0)
            throw new InvalidOperationException("The native Cultist world has no safe exterior surface floor for both teams.");
        Point Pick(List<Point> floors, Team team)
        {
            int center = ArenaRespawns.TeamArea(search, team).Center.X;
            return floors.OrderBy(point => Math.Abs(point.X - center)).ThenBy(point => point.Y).First();
        }
        int top = Math.Max(bounds.Top, redFloors.Concat(blueFloors).Min(point => point.Y) - 12);
        int bottom = Math.Min(bounds.Bottom, redFloors.Concat(blueFloors).Max(point => point.Y) + 1);
        Rectangle respawns = new(bounds.Left, top, bounds.Width, bottom - top);
        Rectangle bossArea = bounds;
        Point preferred = new(Main.dungeonX, Math.Max(bossArea.Top + 5, Main.dungeonY - 20));
        Point boss = default;
        int bestDistance = int.MaxValue;
        for (int x = bossArea.Left + 4; x < bossArea.Right - 4; x++)
        for (int y = bossArea.Top + 5; y < bossArea.Bottom - 5; y++)
        {
            int distance = Math.Abs(x - preferred.X) + Math.Abs(y - preferred.Y);
            if (distance >= bestDistance || !ClearBossSpace(x, y)) continue;
            bestDistance = distance; boss = new(x, y);
        }
        if (bestDistance == int.MaxValue)
            throw new InvalidOperationException("No dry Cultist arrival space remains near the native Dungeon entrance.");
        return new(bounds, Pick(blueFloors, Team.Blue), Pick(redFloors, Team.Red))
        { BossSpawn = boss, RespawnBounds = respawns, BossAreaBounds = bossArea };
    }

    private static List<Point> SurfaceFloors(Rectangle side)
    {
        List<Point> floors = [];
        int bottom = Math.Min(side.Bottom, Math.Min((int)Main.rockLayer, (int)Main.worldSurface + 100));
        for (int x = side.Left + 1; x < side.Right - 1; x++)
        for (int y = side.Top + 3; y < bottom; y++)
        {
            Point point = new(x, y);
            Tile floor = Main.tile[x, y];
            if (Main.tileDungeon[floor.TileType] || floor.TileType is TileID.CrackedBlueDungeonBrick
                    or TileID.CrackedGreenDungeonBrick or TileID.CrackedPinkDungeonBrick
                || !ArenaRespawns.IsValid(point, side)) continue;
            bool exposed = true;
            for (int above = side.Top; above < y - 3; above++)
                if (WorldGen.SolidTile(x, above)) { exposed = false; break; }
            if (exposed) floors.Add(point);
        }
        return floors;
    }

    private static bool ClearBossSpace(int x, int y)
    {
        for (int dx = -3; dx <= 3; dx++)
        for (int dy = -4; dy <= 4; dy++)
        {
            Tile tile = Main.tile[x + dx, y + dy];
            if (tile.LiquidAmount > 0 || tile.HasUnactuatedTile && Main.tileSolid[tile.TileType]
                && !Main.tileSolidTop[tile.TileType]) return false;
        }
        return true;
    }

    private static void ClearActors()
    {
        foreach (NPC npc in Main.npc)
            if (npc?.active == true)
            {
                npc.active = false;
                NetMessage.SendData(MessageID.SyncNPC, number: npc.whoAmI);
            }
        foreach (Projectile projectile in Main.projectile)
            if (projectile?.active == true)
            {
                projectile.active = false;
                NetMessage.SendData(MessageID.KillProjectile, number: projectile.identity, number2: projectile.owner);
            }
        for (int i = 0; i < Main.item.Length; i++)
            if (Main.item[i]?.active == true)
            {
                Main.item[i].TurnToAir();
                NetMessage.SendData(MessageID.SyncItem, number: i);
            }
    }
}
