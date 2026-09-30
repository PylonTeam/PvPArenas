using PvPArenas.Common.Game;
using PvPArenas.Common.Game.LoadoutSelector;
using System;
using Terraria.ID;

namespace PvPArenas.Common.Generation;

/// <summary>Resolves the selected arena after its server-side preparation has finished.</summary>
internal static class ArenaGeneration
{
    internal static bool TryResolve(BossFightPreset preset, out ArenaLayout layout, out string failure)
    {
        layout = null;
        failure = "";
        if (preset?.Boss == null || preset.Boss.Type <= NPCID.None)
        {
            failure = "The boss preset is missing a valid NPC.";
            return false;
        }
        failure = ArenaWorldSystem.MissingArena(preset.ArenaKind);
        if (failure.Length > 0)
            return false;
        if (Main.maxTilesX != ArenaWorldSystem.Width || Main.maxTilesY != ArenaWorldSystem.Height)
        {
            failure = "Arenas_v10 has not been loaded. Re-enter the world to start an arena session.";
            return false;
        }

        if (preset.ArenaKind == ArenaKind.UndergroundJungle)
        {
            layout = MirroredJungleGenerator.CreateLayout();
            if (IsSafeSpawn(layout.RedSpawn.X, layout.RedSpawn.Y)
                && IsSafeSpawn(layout.BlueSpawn.X, layout.BlueSpawn.Y)
                && IsOpenBossSpace(layout.BossSpawn.X, layout.BossSpawn.Y))
                return true;
            layout = null;
            failure = "The generated jungle did not provide clear player and boss spawns.";
            return false;
        }

        Rectangle bounds = ArenaWorldSystem.PlayBounds;
        Rectangle redArea = new(bounds.Left, bounds.Top, bounds.Width / 3, bounds.Height);
        Rectangle blueArea = new(bounds.Right - bounds.Width / 3, bounds.Top, bounds.Width / 3, bounds.Height);
        int preferredY = Math.Clamp(Main.spawnTileY, bounds.Top + 3, bounds.Bottom - 1);
        if (!TryFindSpawn(redArea, preferredY, out Point redSpawn)
            || !TryFindSpawn(blueArea, preferredY, out Point blueSpawn))
        {
            failure = "Grounded Red and Blue spawn positions could not be resolved inside Arenas_v10.";
            return false;
        }

        layout = new ArenaLayout(bounds, blueSpawn, redSpawn);
        Point preferredBoss = new(bounds.Center.X, Math.Clamp(preferredY - 8, bounds.Top + 5, bounds.Bottom - 5));
        if (!TryFindBossSpace(layout.BossBounds, preferredBoss, out Point bossSpawn))
        {
            layout = null;
            failure = "No clear boss spawn was found inside Arenas_v10.";
            return false;
        }
        layout = layout with { BossSpawn = bossSpawn };
        return true;
    }

    internal static bool TryFindSpawn(Rectangle area, int preferredY, out Point spawn)
    {
        spawn = default;
        int nearest = int.MaxValue;
        for (int x = area.Left + 1; x < area.Right - 1; x++)
        for (int y = area.Top + 3; y < area.Bottom; y++)
        {
            int distance = Math.Abs(x - area.Center.X) + Math.Abs(y - preferredY);
            if (distance >= nearest || !IsSafeSpawn(x, y))
                continue;
            spawn = new Point(x, y);
            nearest = distance;
        }
        return nearest != int.MaxValue;
    }

    internal static bool IsSafeSpawn(int x, int y)
    {
        if (x < 1 || x >= Main.maxTilesX - 1 || y < 3 || y >= Main.maxTilesY)
            return false;
        for (int dx = -1; dx <= 1; dx++)
        {
            if (!WorldGen.SolidTile(x + dx, y))
                return false;
            for (int dy = 1; dy <= 3; dy++)
            {
                Tile tile = Main.tile[x + dx, y - dy];
                if (tile.LiquidAmount > 0 || tile.HasUnactuatedTile
                    && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType])
                    return false;
            }
        }
        return true;
    }

    private static bool TryFindBossSpace(Rectangle area, Point preferred, out Point spawn)
    {
        spawn = default;
        int nearest = int.MaxValue;
        for (int x = area.Left + 4; x < area.Right - 4; x++)
        for (int y = area.Top + 5; y < area.Bottom - 5; y++)
        {
            int distance = Math.Abs(x - preferred.X) + Math.Abs(y - preferred.Y);
            if (distance >= nearest || !IsOpenBossSpace(x, y))
                continue;
            spawn = new Point(x, y);
            nearest = distance;
        }
        return nearest != int.MaxValue;
    }

    private static bool IsOpenBossSpace(int x, int y)
    {
        for (int dx = -3; dx <= 3; dx++)
        for (int dy = -4; dy <= 4; dy++)
        {
            Tile tile = Main.tile[x + dx, y + dy];
            if (tile.LiquidAmount > 0 || tile.HasUnactuatedTile
                && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType])
                return false;
        }
        return true;
    }
}
