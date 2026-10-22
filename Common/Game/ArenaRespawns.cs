using PvPArenas.Common.Game.LoadoutSelector;
using ErkySSC.Core.Compat;
using System;
using System.Collections.Generic;
using System.Linq;
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
        return Rectangle.Intersect(Rectangle.Intersect(layout.ArenaBounds, layout.RespawnBounds ?? layout.ArenaBounds), side);
    }

    // Unassigned late joins may stage before receiving a team. Dead fighters remain eligible.
    internal static bool CanStage(Player player) => player?.active == true
        && !player.ghost && !ReeseCompat.IsGhostSpectator(player.whoAmI);

    internal static bool IsInitialParticipant(Player player) => CanStage(player)
        && (Team)player.team is Team.Red or Team.Blue;

    /// <summary>Reserve the entire round's starts from live terrain before any player is moved.</summary>
    internal static bool TryPlanInitialSpawns(IReadOnlyList<Player> players, BossFightPreset preset,
        ArenaLayout layout, out Dictionary<int, Point> spawns, out string failure)
    {
        spawns = [];
        failure = "";
        if (Main.netMode != NetmodeID.Server)
        {
            failure = "Initial arena spawns must be planned on the server.";
            return false;
        }
        if (players == null || preset == null || layout == null || layout.ArenaBounds.Width <= 0
            || layout.ArenaBounds.Height <= 0
            || !new Rectangle(0, 0, Main.maxTilesX, Main.maxTilesY).Contains(layout.ArenaBounds))
        {
            failure = "The arena layout or initial player list is unavailable.";
            return false;
        }

        Player[] participants = players.Where(IsInitialParticipant).OrderBy(player => player.whoAmI)
            .GroupBy(player => player.whoAmI).Select(group => group.First()).ToArray();
        if (participants.Length == 0)
        {
            failure = "No active Red or Blue players are available for initial spawns.";
            return false;
        }

        OpenTerrain terrain = new(layout.ArenaBounds);
        foreach (Team team in new[] { Team.Red, Team.Blue })
        {
            Player[] teammates = participants.Where(player => (Team)player.team == team).ToArray();
            if (teammates.Length == 0) continue;
            Rectangle side = TeamArea(layout, team);
            bool[] reachable = terrain.ReachableFrom(layout.PlayerSpawn(team));
            int width = teammates.Max(player => Math.Max(1, player.width));
            int height = teammates.Max(player => Math.Max(1, player.height));
            HashSet<Point> authored = preset.ArenaKind == ArenaKind.ArenasV10
                ? new(team == Team.Red ? RedPoints : BluePoints) : [];
            List<InitialCandidate> candidates = [];
            AddCandidates(connected: true);

            if (!TryReserveInitial(candidates, teammates.Length, side, out List<InitialCandidate> chosen))
            {
                // A blocked or cramped anchor cave must not hide safe starts elsewhere on this side.
                AddCandidates(connected: false);
                if (!TryReserveInitial(candidates, teammates.Length, side, out chosen))
                {
                    spawns.Clear();
                    failure = $"Not enough non-overlapping safe {team} starting positions for {teammates.Length} players "
                        + $"({PackInitialCandidates(candidates).Count} positions could be reserved).";
                    return false;
                }
            }
            for (int i = 0; i < teammates.Length; i++)
                spawns.Add(teammates[i].whoAmI, chosen[i].Point);

            void AddCandidates(bool connected)
            {
                for (int y = side.Top + 3; y < side.Bottom; y++)
                for (int x = side.Left + 1; x < side.Right - 1; x++)
                {
                    Point point = new(x, y);
                    // Authored points retain their teleport semantics even in separate chambers.
                    bool isAuthored = authored.Contains(point);
                    bool primary = isAuthored || reachable[terrain.Index(x, y)];
                    if (primary != connected || !IsValid(point, side)) continue;
                    Rectangle body = InitialBody(point, width, height);
                    if (IsInitialBodySafe(body, y, side))
                        candidates.Add(new(point, body, terrain.RoomArea(x, y), isAuthored));
                }
            }
        }
        return true;
    }

    private sealed record InitialCandidate(Point Point, Rectangle Body, int Area, bool Authored);

    private static Rectangle InitialBody(Point point, int width, int height) =>
        new(point.X * 16 + 8 - (width + 1) / 2, point.Y * 16 - height, width + width % 2, height);

    private static bool IsInitialBodySafe(Rectangle body, int floor, Rectangle side)
    {
        if (!ArenaSpawnBoxes.TileToWorld(side).Contains(body)) return false;
        int left = body.Left / 16, right = (body.Right - 1) / 16, top = body.Top / 16;
        for (int x = left; x <= right; x++)
        {
            Tile support = Main.tile[x, floor];
            bool platform = support.HasUnactuatedTile && TileID.Sets.Platforms[support.TileType]
                && support.Slope == SlopeType.Solid && !support.IsHalfBlock;
            if (!WorldGen.SolidTile(x, floor) && !platform) return false;
            for (int y = top; y < floor; y++)
            {
                Tile tile = Main.tile[x, y];
                if (tile.LiquidAmount > 0 || tile.HasUnactuatedTile
                    && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType]) return false;
            }
        }
        for (int x = Math.Max(0, left - 1); x <= Math.Min(Main.maxTilesX - 1, right + 1); x++)
        for (int y = Math.Max(0, top - 1); y <= Math.Min(Main.maxTilesY - 1, floor); y++)
        {
            Tile tile = Main.tile[x, y];
            if (tile.HasUnactuatedTile && (TileID.Sets.TouchDamageImmediate[tile.TileType] > 0
                || TileID.Sets.TouchDamageHot[tile.TileType] || tile.TileType == TileID.LandMine)) return false;
        }
        return true;
    }

    private static bool TryReserveInitial(List<InitialCandidate> candidates, int count, Rectangle side,
        out List<InitialCandidate> chosen)
    {
        if (TrySpreadInitial(candidates, count, side, out chosen)) return true;
        // Spread-first choices can strand narrow gaps. Pack from ledge edges before broadening the search.
        List<InitialCandidate> packed = PackInitialCandidates(candidates);
        return packed.Count >= count && TrySpreadInitial(packed, count, side, out chosen);
    }

    private static bool TrySpreadInitial(List<InitialCandidate> candidates, int count, Rectangle side,
        out List<InitialCandidate> chosen)
    {
        chosen = [];
        bool[] available = Enumerable.Repeat(true, candidates.Count).ToArray();
        float[] nearest = new float[candidates.Count];
        Vector2 center = side.Center.ToVector2();
        for (int i = 0; i < candidates.Count; i++)
            nearest[i] = Vector2.DistanceSquared(candidates[i].Point.ToVector2(), center);
        while (chosen.Count < count)
        {
            int best = -1, ties = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!available[i]) continue;
                int comparison = best < 0 ? 1 : candidates[i].Authored.CompareTo(candidates[best].Authored);
                if (comparison == 0) comparison = nearest[i].CompareTo(nearest[best]);
                if (comparison == 0) comparison = candidates[i].Area.CompareTo(candidates[best].Area);
                if (comparison < 0) continue;
                if (comparison == 0 && Main.rand.Next(++ties) != 0) continue;
                if (comparison > 0) ties = 1;
                best = i;
            }
            if (best < 0) return false;
            InitialCandidate selected = candidates[best];
            chosen.Add(selected);
            for (int i = 0; i < candidates.Count; i++)
            {
                if (selected.Body.Intersects(candidates[i].Body)) available[i] = false;
                float distance = Vector2.DistanceSquared(selected.Point.ToVector2(), candidates[i].Point.ToVector2());
                nearest[i] = chosen.Count == 1 ? distance : Math.Min(nearest[i], distance);
            }
        }
        return true;
    }

    private static List<InitialCandidate> PackInitialCandidates(List<InitialCandidate> candidates)
    {
        List<InitialCandidate> best = [];
        // Edge-first interval packing recovers capacity that spread-first choices may strand.
        // Try both axes and directions because platforms can provide vertically close floors.
        for (int order = 0; order < 8; order++)
        {
            IEnumerable<InitialCandidate> sorted = (order & 1) == 0
                ? candidates.OrderBy(candidate => (order & 2) == 0 ? candidate.Point.Y : -candidate.Point.Y)
                    .ThenBy(candidate => (order & 4) == 0 ? candidate.Point.X : -candidate.Point.X)
                : candidates.OrderBy(candidate => (order & 2) == 0 ? candidate.Point.X : -candidate.Point.X)
                    .ThenBy(candidate => (order & 4) == 0 ? candidate.Point.Y : -candidate.Point.Y);
            List<InitialCandidate> packed = [];
            InitialReservationGrid reservations = new();
            foreach (InitialCandidate candidate in sorted)
                if (reservations.CanAdd(candidate.Body))
                {
                    packed.Add(candidate);
                    reservations.Add(candidate.Body);
                }
            if (packed.Count > best.Count) best = packed;
        }
        return best;
    }

    private sealed class InitialReservationGrid
    {
        private const int CellSize = 64;
        private readonly Dictionary<Point, List<Rectangle>> cells = [];

        internal bool CanAdd(Rectangle body)
        {
            for (int y = body.Top / CellSize; y <= (body.Bottom - 1) / CellSize; y++)
            for (int x = body.Left / CellSize; x <= (body.Right - 1) / CellSize; x++)
                if (cells.TryGetValue(new(x, y), out List<Rectangle> occupants)
                    && occupants.Any(body.Intersects)) return false;
            return true;
        }

        internal void Add(Rectangle body)
        {
            for (int y = body.Top / CellSize; y <= (body.Bottom - 1) / CellSize; y++)
            for (int x = body.Left / CellSize; x <= (body.Right - 1) / CellSize; x++)
            {
                Point cell = new(x, y);
                if (!cells.TryGetValue(cell, out List<Rectangle> occupants)) cells[cell] = occupants = [];
                occupants.Add(body);
            }
        }
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
        ConsiderTerrain(connected: true);
        // If the anchor is blocked or its cave has no safe floor, search the rest of the team's side.
        if (!selection.Found) ConsiderTerrain(connected: false);
        spawn = selection.Point;
        return selection.Found;

        void ConsiderTerrain(bool connected)
        {
            for (int y = side.Top + 3; y < side.Bottom; y++)
            for (int x = side.Left + 1; x < side.Right - 1; x++)
            {
                Point point = new(x, y);
                if (reachable[terrain.Index(x, y)] == connected && IsValid(point, side))
                    selection.Consider(point, terrain.RoomArea(x, y));
            }
        }
    }

    internal static bool IsValid(Point point, Rectangle side)
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
