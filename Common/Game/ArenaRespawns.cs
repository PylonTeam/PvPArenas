using PvPArenas.Common.Game.LoadoutSelector;
using System;
using System.Collections.Generic;
using Terraria.Enums;
using Terraria.ID;

namespace PvPArenas.Common.Game;

/// <summary>Server-owned spawn selection. Terrain is inspected, never carved or changed.</summary>
internal static class ArenaRespawns
{
    internal const int HostileRadius = 80; // Tiles, measured from the respawning player's center.

    // Floor coordinates on the authored Arenas_v10 map, not percentages or random offsets.
    internal static readonly Point[] RedPoints =
    [new(170, 310), new(332, 520), new(332, 170), new(139, 505), new(308, 305), new(197, 175)];
    internal static readonly Point[] BluePoints =
    [new(703, 196), new(527, 267), new(600, 229), new(690, 264), new(580, 299), new(707, 299)];

    internal static Rectangle TeamArea(ArenaLayout layout, Team team)
    {
        int sideWidth = Main.maxTilesX * 2 / 5;
        Rectangle side = team switch
        {
            Team.Red => new(0, 0, sideWidth, Main.maxTilesY),
            Team.Blue => new(Main.maxTilesX - sideWidth, 0, sideWidth, Main.maxTilesY),
            _ => Rectangle.Empty
        };
        return Rectangle.Intersect(layout.ArenaBounds, side);
    }

    internal static bool TrySelect(Player player, BossFightPreset preset, ArenaLayout layout, out Point spawn)
    {
        spawn = default;
        if (Main.netMode != NetmodeID.Server || player?.active != true || preset == null || layout == null
            || (Team)player.team is not (Team.Red or Team.Blue))
            return false;

        Rectangle side = TeamArea(layout, (Team)player.team);
        Selection selection = new(player);
        if (preset.ArenaKind == ArenaKind.ArenasV10)
        {
            foreach (Point point in (Team)player.team == Team.Red ? RedPoints : BluePoints)
                if (IsValid(point, side))
                    selection.Consider(point, 0);
            if (selection.Found)
            {
                spawn = selection.Point;
                return true;
            }
        }

        // Rebuild from live tiles at respawn time: edits, settled liquids and a new seed are all respected.
        OpenTerrain terrain = new(layout.ArenaBounds);
        bool[] reachable = terrain.ReachableFrom(layout.PlayerSpawn((Team)player.team));
        for (int y = side.Top + 3; y < side.Bottom; y++)
        for (int x = side.Left + 1; x < side.Right - 1; x++)
        {
            Point point = new(x, y);
            if (reachable[terrain.Index(x, y)] && IsValid(point, side))
                selection.Consider(point, terrain.RoomArea(x, y));
        }
        spawn = selection.Point;
        return selection.Found;
    }

    private static bool IsValid(Point point, Rectangle side)
    {
        if (point.X < 2 || point.X >= Main.maxTilesX - 2 || point.Y < 4 || point.Y >= Main.maxTilesY
            || !side.Contains(point.X - 1, point.Y - 3) || !side.Contains(point.X + 1, point.Y))
            return false;

        for (int x = point.X - 1; x <= point.X + 1; x++)
        {
            Tile floor = Main.tile[x, point.Y];
            bool platform = floor.HasUnactuatedTile && TileID.Sets.Platforms[floor.TileType]
                && floor.Slope == SlopeType.Solid && !floor.IsHalfBlock;
            if (!WorldGen.SolidTile(x, point.Y) && !platform) return false;
            for (int y = point.Y - 3; y < point.Y; y++)
            {
                Tile tile = Main.tile[x, y];
                if (tile.LiquidAmount > 0 || tile.HasUnactuatedTile
                    && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType])
                    return false;
            }
        }

        // Include adjacent tiles so spikes, thorns and hot floors cannot touch the player's hitbox.
        for (int x = point.X - 2; x <= point.X + 2; x++)
        for (int y = point.Y - 4; y <= point.Y; y++)
        {
            Tile tile = Main.tile[x, y];
            if (tile.HasUnactuatedTile && (TileID.Sets.TouchDamageImmediate[tile.TileType] > 0
                || TileID.Sets.TouchDamageHot[tile.TileType] || tile.TileType == TileID.LandMine))
                return false;
        }
        return true;
    }

    private sealed class Selection
    {
        private readonly List<Vector2> enemies = [];
        private readonly List<Vector2> occupants = [];
        private int bestNearby = int.MaxValue, bestOccupied, bestArea, bestDistanceBand, ties;
        private float bestDistance;
        internal bool Found { get; private set; }
        internal Point Point { get; private set; }

        internal Selection(Player player)
        {
            foreach (Player other in Main.player)
            {
                if (other?.active != true || other.dead || other.ghost || ReferenceEquals(other, player))
                    continue;
                Vector2 center = other.Center / 16f;
                occupants.Add(center);
                if (other.hostile && other.team != player.team)
                    enemies.Add(center);
            }
        }

        internal void Consider(Point point, int area)
        {
            Vector2 center = new(point.X + .5f, point.Y - 1.5f);
            int nearby = 0, occupied = 0;
            float distance = float.MaxValue;
            foreach (Vector2 enemy in enemies)
            {
                float squared = Vector2.DistanceSquared(center, enemy);
                distance = Math.Min(distance, squared);
                if (squared < HostileRadius * HostileRadius)
                    nearby++;
            }
            foreach (Vector2 other in occupants)
                if (Vector2.DistanceSquared(center, other) < 4 * 4)
                    occupied++;

            // Outside the danger radius: favor open rooms, then distance in 16-tile bands.
            // Past two radii all distances are equivalent, allowing varied uncontested spawns.
            int band = (int)(MathF.Sqrt(Math.Min(distance, 4 * HostileRadius * HostileRadius)) / 16);
            int comparison = bestNearby.CompareTo(nearby);
            if (comparison == 0 && nearby > 0) comparison = distance.CompareTo(bestDistance);
            if (comparison == 0) comparison = bestOccupied.CompareTo(occupied);
            if (comparison == 0) comparison = area.CompareTo(bestArea);
            if (comparison == 0) comparison = band.CompareTo(bestDistanceBand);
            if (Found && comparison < 0) return;
            if (Found && comparison == 0 && Main.rand.Next(++ties) != 0) return;
            if (!Found || comparison > 0) ties = 1;
            Found = true;
            Point = point;
            bestNearby = nearby;
            bestOccupied = occupied;
            bestArea = area;
            bestDistance = distance;
            bestDistanceBand = band;
        }
    }

    /// <summary>Horizontal empty runs support exact room-area queries and player-sized flood filling.</summary>
    private sealed class OpenTerrain
    {
        private readonly Rectangle bounds;
        private readonly int[] halfWidths;
        private readonly byte[] bodyHeights;

        internal OpenTerrain(Rectangle bounds)
        {
            this.bounds = bounds;
            halfWidths = new int[bounds.Width * bounds.Height];
            bodyHeights = new byte[halfWidths.Length];
            for (int y = bounds.Top; y < bounds.Bottom; y++)
            {
                int run = 0;
                for (int x = bounds.Left; x < bounds.Right; x++)
                {
                    Tile tile = Main.tile[x, y];
                    int index = Index(x, y);
                    // Platforms and decoration do not seal a room, but still count as
                    // placed tiles when measuring its completely empty floor-to-ceiling area.
                    bool passable = tile.LiquidAmount == 0 && !(tile.HasUnactuatedTile
                        && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType]);
                    bodyHeights[index] = passable
                        ? (byte)Math.Min(3, y == bounds.Top ? 1 : bodyHeights[index - bounds.Width] + 1) : (byte)0;
                    run = !tile.HasTile && tile.LiquidAmount == 0 ? run + 1 : 0;
                    halfWidths[index] = run;
                }
                run = 0;
                for (int x = bounds.Right - 1; x >= bounds.Left; x--)
                {
                    int index = Index(x, y);
                    run = halfWidths[index] > 0 ? run + 1 : 0;
                    halfWidths[index] = Math.Min(halfWidths[index], run);
                }
            }
        }

        internal int Index(int x, int y) => (y - bounds.Top) * bounds.Width + x - bounds.Left;

        internal int RoomArea(int x, int floor)
        {
            int halfWidth = int.MaxValue, largest = 0;
            for (int y = floor - 1; y >= bounds.Top; y--)
            {
                halfWidth = Math.Min(halfWidth, halfWidths[Index(x, y)]);
                if (halfWidth < 2) break;
                int height = floor - y;
                if (height >= 3) largest = Math.Max(largest, (halfWidth * 2 - 1) * height);
            }
            return largest;
        }

        internal bool[] ReachableFrom(Point start)
        {
            bool[] visited = new bool[halfWidths.Length];
            Queue<Point> queue = new();
            Visit(start.X, start.Y);
            // A small edit at the original anchor must not invalidate the entire connected arena.
            for (int radius = 1; radius <= 8 && queue.Count == 0; radius++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                int dy = radius - Math.Abs(dx);
                if (Visit(start.X + dx, start.Y + dy) || Visit(start.X + dx, start.Y - dy)) break;
            }
            while (queue.TryDequeue(out Point point))
            {
                Visit(point.X - 1, point.Y);
                Visit(point.X + 1, point.Y);
                Visit(point.X, point.Y - 1);
                Visit(point.X, point.Y + 1);
            }
            return visited;

            bool Visit(int x, int y)
            {
                if (x < bounds.Left + 1 || x >= bounds.Right - 1 || y < bounds.Top + 3 || y >= bounds.Bottom)
                    return false;
                int index = Index(x, y);
                if (visited[index]) return false;
                for (int column = x - 1; column <= x + 1; column++)
                    if (bodyHeights[Index(column, y - 1)] < 3) return false;
                visited[index] = true;
                queue.Enqueue(new Point(x, y));
                return true;
            }
        }
    }
}
