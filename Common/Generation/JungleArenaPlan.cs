using PvPArenas.Common.Game;
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria.ID;
using Terraria.Utilities;

namespace PvPArenas.Common.Generation;

internal readonly record struct JungleChamber(Point Center, int RadiusX, int RadiusY);

/// <summary>Seed-only density field, scored landmark reservations, and a connected network of winding caves.</summary>
internal sealed class JungleArenaPlan
{
    private const byte Open = 1, Route = 2, Ground = 4, Grass = 8, Water = 16;
    private const int Grid = 5;
    private readonly Rectangle area, protectedArea;
    private readonly Rectangle[] landmarks;
    private readonly byte[] tiles;
    private readonly int width, seed;
    internal IReadOnlyList<JungleChamber> Chambers { get; }

    internal JungleArenaPlan(int seed, ArenaLayout layout, Rectangle protectedArea)
    {
        if (Main.netMode != NetmodeID.Server)
            throw new InvalidOperationException("Jungle planning must run on the server.");
        this.seed = seed;
        this.protectedArea = protectedArea;
        width = (Main.maxTilesX + 1) / 2;
        area = Rectangle.Intersect(layout.ArenaBounds, new Rectangle(0, 0, width, Main.maxTilesY));
        tiles = new byte[width * Main.maxTilesY];
        UnifiedRandom random = new(seed ^ 0x53A71);
        ShapeCaves();
        // Evaluate the actual density before reserving structures. Candidates are finite and cannot retry forever.
        List<JungleChamber> chambers = PlanLandmarks(layout);
        Chambers = chambers;
        landmarks = chambers.Select(room => new Rectangle(room.Center.X - room.RadiusX, room.Center.Y - room.RadiusY,
            room.RadiusX * 2 + 1, room.RadiusY * 2 + 1)).ToArray();

        List<Point> nodes = [];
        for (int row = 0; row < 3; row++)
        for (int column = 0; column < 3; column++)
        {
            Point site = new(area.Left + 40 + column * (area.Width - 80) / 2,
                area.Top + 35 + row * (area.Height - 70) / 2);
            if (FindPocket(site, random, out Point pocket)) nodes.Add(pocket);
        }
        for (int i = 0; i < chambers.Count; i++)
        {
            JungleChamber room = chambers[i];
            int doorY = room.Center.Y + (i == 0 ? room.RadiusY / 2 : i == 1 ? room.RadiusY - 8 : 5);
            nodes.Add(new(room.Center.X - room.RadiusX - 6, doorY));
            nodes.Add(new(room.Center.X + room.RadiusX + 6, doorY));
            // Join the route endpoints to the structure pass's seven-tile-high side entries.
            for (int offset = 1; offset <= 6; offset++)
            for (int dy = -3; dy <= 3; dy++)
            {
                Mark(room.Center.X - room.RadiusX - offset, doorY + dy, Open | Route);
                Mark(room.Center.X + room.RadiusX + offset, doorY + dy, Open | Route);
            }
        }
        Point spawn = layout.RedSpawn;
        nodes.Add(new(spawn.X, spawn.Y - 3));
        // Different-height crossings survive reflection as separate ways between teams.
        nodes.Add(new(area.Right - 1, area.Top + 55));
        nodes.Add(new(area.Right - 1, layout.BossSpawn.Y));
        nodes.Add(new(area.Right - 1, area.Bottom - 40));
        Connect(nodes.Distinct().ToList(), random);

        // A short irregular mound supports the anchor; other respawn floors come from the caves.
        for (int x = spawn.X - 5; x <= spawn.X + 5; x++)
        for (int y = spawn.Y - 8; y < spawn.Y; y++) Mark(x, y, Open | Route);
        for (int y = spawn.Y; y < spawn.Y + 7; y++)
        for (int x = spawn.X - 4; x <= spawn.X + 4; x++)
            if (Math.Abs(x - spawn.X) <= Math.Max(1, 4 - (y - spawn.Y) / 2)) Mark(x, y, Ground);
        // Neutral world-spawn ledge below the boss approach survives reflection across the center seam.
        for (int x = layout.BossSpawn.X - 5; x <= layout.BossSpawn.X; x++)
        {
            for (int y = layout.BossSpawn.Y - 5; y < layout.BossSpawn.Y + 10; y++) Mark(x, y, Open | Route);
            for (int y = layout.BossSpawn.Y + 10; y < layout.BossSpawn.Y + 14; y++) Mark(x, y, Ground);
        }

        RemoveIsolatedPockets(new Point(spawn.X, spawn.Y - 3));

        for (int y = area.Top; y < area.Bottom; y++)
        for (int x = area.Left; x < area.Right; x++)
            if (!protectedArea.Contains(x, y) && !IsOpen(x, y)
                && (IsOpen(x - 1, y) || IsOpen(x + 1, y) || IsOpen(x, y - 1) || IsOpen(x, y + 1)))
                tiles[Index(x, y)] |= Grass;
        AddPools();
    }

    private void ShapeCaves()
    {
        for (int y = area.Top; y < area.Bottom; y++)
        for (int x = area.Left; x < area.Right; x++)
        {
            if (protectedArea.Contains(x, y)) continue;
            float wx = x + Noise(x, y, 67, 3) * 17, wy = y + Noise(x, y, 61, 7) * 15;
            float cavity = Noise(wx, wy, 29, 11) * .60f + Noise(wx, wy, 12, 19) * .28f + Noise(wx, wy, 4, 31) * .12f;
            int border = Math.Min(y - area.Top, Math.Min(area.Bottom - 1 - y, x - area.Left));
            if (cavity > -.06f + Math.Max(0, 7 - border) * .08f) tiles[Index(x, y)] = Open;
        }
    }

    private List<JungleChamber> PlanLandmarks(ArenaLayout layout)
    {
        Point[] preferred = [new(145, 215), new(320, 145), new(120, 335), new(300, 340)];
        Point[] sizes = [new(29, 23), new(30, 40), new(19, 14), new(19, 15)];
        List<JungleChamber> result = [];
        List<Rectangle> reserved = [];
        Rectangle spawn = new(layout.RedSpawn.X - 18, layout.RedSpawn.Y - 18, 37, 37);
        for (int kind = 0; kind < sizes.Length; kind++)
        {
            Point size = sizes[kind], best = default;
            float bestScore = float.MinValue;
            for (int y = area.Top + size.Y + 7; y < area.Bottom - size.Y - 7; y += 4)
            for (int x = area.Left + size.X + 7; x < area.Right - size.X - 12; x += 4)
            {
                Rectangle envelope = new(x - size.X, y - size.Y, size.X * 2 + 1, size.Y * 2 + 1);
                Rectangle guard = envelope; guard.Inflate(12, 12);
                if (guard.Intersects(protectedArea) || guard.Intersects(spawn) || reserved.Any(guard.Intersects)) continue;
                int open = 0, samples = 0, foundation = 0;
                for (int sx = envelope.Left + 3; sx < envelope.Right - 3; sx += 5)
                for (int sy = envelope.Top + 3; sy < envelope.Bottom - 3; sy += 5)
                { samples++; if (IsOpen(sx, sy)) open++; }
                for (int sx = envelope.Left; sx < envelope.Right; sx += 3)
                    if (!IsOpen(sx, envelope.Bottom - 2)) foundation++;
                // Fit a landmark into an existing pocket, with rock under it and useful separation from the others.
                float score = open * 120f / Math.Max(1, samples) + foundation * 1.3f
                    - Vector2.Distance(new Vector2(x, y), preferred[kind].ToVector2()) * .75f
                    + Noise(x, y, 19, 97 + kind) * 8;
                if (score <= bestScore) continue;
                bestScore = score; best = new(x, y);
            }
            if (bestScore == float.MinValue)
                throw new InvalidOperationException("No safe Jungle landmark footprint fits in the available arena.");
            result.Add(new(best, size.X, size.Y));
            reserved.Add(new(best.X - size.X, best.Y - size.Y, size.X * 2 + 1, size.Y * 2 + 1));
        }
        return result;
    }

    private void RemoveIsolatedPockets(Point start)
    {
        bool[] reached = new bool[tiles.Length];
        Queue<Point> queue = new();
        queue.Enqueue(start); reached[Index(start.X, start.Y)] = true;
        Point[] directions = [new(-1, 0), new(1, 0), new(0, -1), new(0, 1)];
        while (queue.TryDequeue(out Point current))
        foreach (Point direction in directions)
        {
            Point next = current + direction;
            if (!IsOpen(next.X, next.Y) || reached[Index(next.X, next.Y)]) continue;
            reached[Index(next.X, next.Y)] = true; queue.Enqueue(next);
        }
        for (int y = area.Top; y < area.Bottom; y++)
        for (int x = area.Left; x < area.Right; x++)
            if (!reached[Index(x, y)] && !landmarks.Any(rect => rect.Contains(x, y)))
                tiles[Index(x, y)] &= unchecked((byte)~Open);
    }

    private bool FindPocket(Point site, UnifiedRandom random, out Point pocket)
    {
        pocket = default;
        int best = int.MinValue;
        site += new Point(random.Next(-9, 10), random.Next(-8, 9));
        for (int dx = -15; dx <= 15; dx += 5)
        for (int dy = -15; dy <= 15; dy += 5)
        {
            Point point = site + new Point(dx, dy);
            if (!CanRoute(point)) continue;
            int score = -Math.Abs(dx) - Math.Abs(dy);
            for (int xx = -8; xx <= 8; xx += 4)
            for (int yy = -8; yy <= 8; yy += 4)
                if (IsOpen(point.X + xx, point.Y + yy)) score += 4;
            if (score > best) { best = score; pocket = point; }
        }
        return best != int.MinValue;
    }

    private void Connect(List<Point> nodes, UnifiedRandom random)
    {
        List<(int A, int B, int Distance)> edges = [];
        for (int a = 0; a < nodes.Count; a++)
        for (int b = a + 1; b < nodes.Count; b++)
        {
            Point delta = nodes[a] - nodes[b];
            edges.Add((a, b, delta.X * delta.X + delta.Y * delta.Y));
        }
        edges.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        int[] groups = Enumerable.Range(0, nodes.Count).ToArray();
        List<(int A, int B)> loops = [];
        foreach (var edge in edges)
        {
            if (groups[edge.A] == groups[edge.B])
            {
                if (edge.Distance < 190 * 190) loops.Add((edge.A, edge.B));
                continue;
            }
            if (!CarveConnection(nodes[edge.A], nodes[edge.B], random)) continue;
            int old = groups[edge.B], replacement = groups[edge.A];
            for (int i = 0; i < groups.Length; i++) if (groups[i] == old) groups[i] = replacement;
        }
        for (int i = 0; i < 5 && loops.Count > 0; i++)
        {
            int index = random.Next(Math.Min(12, loops.Count));
            var edge = loops[index]; loops.RemoveAt(index);
            CarveConnection(nodes[edge.A], nodes[edge.B], random);
        }
        if (groups.Any(group => group != groups[0]))
            throw new InvalidOperationException("The occupied lobby leaves no connected route through this Jungle.");
    }

    private bool CarveConnection(Point from, Point to, UnifiedRandom random)
    {
        int columns = (area.Width + Grid - 1) / Grid, rows = (area.Height + Grid - 1) / Grid;
        Point Position(int index) => new(Math.Min(area.Right - 1, area.Left + index % columns * Grid + Grid / 2),
            Math.Min(area.Bottom - 1, area.Top + index / columns * Grid + Grid / 2));
        int Cell(Point point) => Math.Clamp((point.Y - area.Top) / Grid, 0, rows - 1) * columns
            + Math.Clamp((point.X - area.Left) / Grid, 0, columns - 1);
        int start = Cell(from), end = Cell(to);
        int[] costs = Enumerable.Repeat(int.MaxValue, columns * rows).ToArray();
        int[] previous = Enumerable.Repeat(-1, costs.Length).ToArray();
        PriorityQueue<int, int> queue = new();
        costs[start] = 0; queue.Enqueue(start, 0);
        while (queue.TryDequeue(out int current, out _))
        {
            if (current == end) break;
            Point currentPoint = current == start ? from : Position(current);
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int x = current % columns + dx, y = current / columns + dy;
                if (x < 0 || x >= columns || y < 0 || y >= rows) continue;
                int next = y * columns + x;
                Point point = next == end ? to : Position(next);
                if (!CanRoute(point) || !CanRoute(new((point.X + currentPoint.X) / 2, (point.Y + currentPoint.Y) / 2))) continue;
                int cost = costs[current] + (dx == 0 || dy == 0 ? 10 : 14)
                    + (IsOpen(point.X, point.Y) ? 0 : 20) + (int)((Noise(point.X, point.Y, 23, 47) + 1) * 14);
                if (cost >= costs[next]) continue;
                costs[next] = cost; previous[next] = current;
                queue.Enqueue(next, cost + (Math.Abs(point.X - to.X) + Math.Abs(point.Y - to.Y)) * 7 / Grid);
            }
        }
        if (costs[end] == int.MaxValue) return false;
        List<Point> path = [to];
        for (int cursor = end; cursor >= 0; cursor = previous[cursor]) path.Add(cursor == start ? from : Position(cursor));
        for (int i = 1; i < path.Count; i++)
        {
            Point a = path[i - 1], b = path[i];
            int steps = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
            for (int j = 0; j <= steps; j++)
            {
                float t = j / (float)Math.Max(1, steps);
                float x = MathHelper.Lerp(a.X, b.X, t), y = MathHelper.Lerp(a.Y, b.Y, t);
                Stamp(x, y, 6.5f + Noise(x, y, 18, 53) * 3.5f, route: true);
            }
            if (i % 5 == 0 && random.Next(3) == 0) Branch(b, random);
        }
        return true;
    }

    private void Branch(Point start, UnifiedRandom random)
    {
        Vector2 position = start.ToVector2();
        double angle = random.NextDouble() * Math.PI * 2;
        int length = random.Next(14, 32);
        for (int step = 0; step < length; step++)
        {
            angle += (random.NextDouble() - .5) * .28;
            position += new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * 1.3f;
            if (!CanRoute(position.ToPoint())) break;
            float radius = MathHelper.Lerp(5.5f, 2.5f, step / (float)length) + Noise(position.X, position.Y, 9, 59);
            Stamp(position.X, position.Y, radius, route: false);
        }
    }

    private void Stamp(float cx, float cy, float radius, bool route)
    {
        int extent = (int)MathF.Ceiling(radius + 2);
        for (int x = (int)cx - extent; x <= (int)cx + extent; x++)
        for (int y = (int)cy - extent; y <= (int)cy + extent; y++)
        {
            float dx = x - cx, dy = y - cy;
            float distance = dx * dx + dy * dy;
            float edge = radius + Noise(x, y, 4, 61) * 1.6f;
            if (route && distance <= 10) Mark(x, y, Open | Route);
            else if (distance <= edge * edge) Mark(x, y, Open);
        }
    }

    private void AddPools()
    {
        // Small closed pockets get shallow water; nothing relies on unbounded liquid settling.
        List<Point> placed = [];
        for (int y = area.Bottom - 4; y > area.Top + 45 && placed.Count < 6; y--)
        for (int x = area.Left + 5; x < area.Right - 5 && placed.Count < 6; x++)
        {
            if (!IsOpen(x, y) || IsOpen(x - 1, y) || Noise(x, y, 8, 71) < -.1f) continue;
            int end = x;
            while (end < area.Right - 2 && IsOpen(end + 1, y)) end++;
            if (end - x < 4 || end - x > 17 || placed.Any(p => Math.Abs(p.X - x) + Math.Abs(p.Y - y) < 55)) continue;
            bool closed = true;
            for (int xx = x; xx <= end; xx++)
                if (Excluded(xx, y) || Excluded(xx, y + 1) || IsOpen(xx, y + 1)
                    || (tiles[Index(xx, y)] & Route) != 0) { closed = false; break; }
            if (!closed) continue;
            for (int xx = x; xx <= end; xx++) Mark(xx, y, Water);
            placed.Add(new(x, y));
            x = end;
        }
    }

    internal void ApplyTerrain(int x, int y)
    {
        if (Main.netMode != NetmodeID.Server || !area.Contains(x, y) || protectedArea.Contains(x, y)) return;
        Tile tile = Main.tile[x, y];
        byte flags = tiles[Index(x, y)];
        tile.ClearEverything();
        tile.WallType = WallID.JungleUnsafe;
        if ((flags & Open) == 0 || (flags & Ground) != 0)
        {
            tile.HasTile = true;
            tile.TileType = (flags & (Grass | Ground)) != 0 ? TileID.JungleGrass
                : Noise(x, y, 12, 79) > .45f ? TileID.Stone
                : Noise(x, y, 5, 83) > .66f && Noise(x, y, 19, 89) > .3f ? TileID.Iron : TileID.Mud;
        }
        else if ((flags & Water) != 0) { tile.LiquidType = LiquidID.Water; tile.LiquidAmount = 220; }
    }

    private bool IsOpen(int x, int y) => area.Contains(x, y) && (tiles[Index(x, y)] & (Open | Ground)) == Open;
    private bool Excluded(int x, int y) => !area.Contains(x, y) || protectedArea.Contains(x, y) || landmarks.Any(rect => rect.Contains(x, y));
    private bool CanRoute(Point point)
    {
        if (!area.Contains(point)) return false;
        Rectangle body = new(point.X - 4, point.Y - 4, 9, 9);
        return !body.Intersects(protectedArea) && !landmarks.Any(rect => rect.Intersects(body));
    }
    private int Index(int x, int y) => y * width + x;
    private void Mark(int x, int y, int flags) { if (!Excluded(x, y)) tiles[Index(x, y)] |= (byte)flags; }

    private float Noise(float x, float y, int scale, int salt)
    {
        x /= scale; y /= scale;
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        float fx = x - ix, fy = y - iy;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        return MathHelper.Lerp(MathHelper.Lerp(Hash(ix, iy), Hash(ix + 1, iy), fx),
            MathHelper.Lerp(Hash(ix, iy + 1), Hash(ix + 1, iy + 1), fx), fy);
        float Hash(int px, int py)
        {
            uint value = unchecked((uint)(seed ^ salt * 83492791 ^ px * 73856093 ^ py * 19349663));
            value ^= value >> 16; value *= 0x7feb352d; value ^= value >> 15; value *= 0x846ca68b; value ^= value >> 16;
            return (value & 0xffff) / 32767.5f - 1;
        }
    }
}
