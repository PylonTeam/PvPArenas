using System;
using Terraria.ID;

namespace PvPArenas.Common.Game;

/// <summary>King Slime's authored-map bounds and collision-checked recovery; never changes terrain.</summary>
internal sealed class KingSlimeArena
{
    // Interior of Arenas_v10's enclosing shell. The wider world bounds include exterior void.
    internal static Rectangle TileBounds => new(84, 132, 635, 398);
    internal static Point FullSize => Main.getGoodWorld ? new(245, 230) : new(122, 115);

    private Vector2 progressPosition;
    private int stalledTicks, embeddedTicks, retryTicks;

    internal void Reset()
    {
        progressPosition = Vector2.Zero;
        stalledTicks = embeddedTicks = retryTicks = 0;
    }

    internal void Update(NPC boss, Rectangle bounds, Vector2 preferredBottom)
    {
        if (Main.netMode != NetmodeID.Server || boss?.active != true || boss.life <= 0
            || boss.type != NPCID.KingSlime || bounds.Width <= 0 || bounds.Height <= 0)
            return;

        // Vanilla arms an off-world teleport when its target is over 3,000 pixels away.
        // Restoring timeLeft alone neither cancels that destination nor clears the despawn flag.
        boss.DiscourageDespawn(3600);
        if (retryTicks > 0) { retryTicks--; return; }

        Point size = FullSize;
        bool outside = !bounds.Contains(boss.Hitbox);
        bool invalidTeleport = boss.ai[1] == 5f
            && !IsLanding(bounds, new Vector2(boss.localAI[1], boss.localAI[2]), size.X, size.Y);
        embeddedTicks = Collision.SolidCollision(boss.position, boss.width, boss.height) ? embeddedTicks + 1 : 0;
        if (boss.ai[1] >= 5f || Vector2.DistanceSquared(boss.Bottom, progressPosition) >= 32f * 32f)
        {
            progressPosition = boss.Bottom;
            stalledTicks = 0;
        }
        else
            stalledTicks++;

        if (!outside && !invalidTeleport && embeddedTicks < 60 && stalledTicks < 180)
            return;
        if (!TryFindLanding(bounds, preferredBottom, size.X, size.Y, out Vector2 landing))
        {
            retryTicks = 60;
            return;
        }

        boss.localAI[0] = 0f;
        boss.localAI[1] = landing.X;
        boss.localAI[2] = landing.Y;
        boss.localAI[3] = 1f; // Already initialized: do not restart the vanilla -100-tick opening delay.
        boss.ai[2] = 0f;
        if (outside || embeddedTicks >= 60)
        {
            boss.Bottom = landing;
            boss.velocity = Vector2.Zero;
            boss.ai[0] = 0f;
            boss.ai[1] = 6f; // Reappear using vanilla's grow animation.
            boss.hide = false;
            boss.dontTakeDamage = false;
        }
        else if (boss.ai[1] != 5f)
        {
            boss.ai[0] = 0f;
            boss.ai[1] = 5f; // A stalled slime keeps its normal shrink/teleport/grow sequence.
        }
        // Keep the same NPC, health, ai[3] summon threshold, and Framework team-health pools.
        boss.netUpdate = true;
        Reset();
    }

    internal static bool TryFindLanding(Rectangle bounds, Vector2 preferredBottom, int width, int height, out Vector2 bottom)
    {
        bottom = default;
        bounds = Rectangle.Intersect(bounds, new Rectangle(16, 16, (Main.maxTilesX - 2) * 16, (Main.maxTilesY - 2) * 16));
        if (width <= 0 || height <= 0 || bounds.Width < width || bounds.Height < height)
            return false;

        float bestDistance = float.MaxValue;
        int firstX = Math.Max(1, bounds.Left / 16);
        int lastX = Math.Min(Main.maxTilesX - 2, (bounds.Right - 1) / 16);
        int firstY = Math.Max(1, (bounds.Top + height + 15) / 16);
        int lastY = Math.Min(Main.maxTilesY - 2, bounds.Bottom / 16);
        for (int y = firstY; y <= lastY; y++)
        for (int x = firstX; x <= lastX; x++)
        {
            Vector2 candidate = new(x * 16 + 8, y * 16);
            float distance = Vector2.DistanceSquared(candidate, preferredBottom);
            if (distance >= bestDistance || !IsFloor(x, y)) continue;
            Rectangle body = new((int)candidate.X - width / 2, y * 16 - height, width, height);
            Vector2 landing = new(body.X + width / 2f, body.Bottom);
            if (!IsLanding(bounds, landing, width, height)) continue;

            bestDistance = distance;
            bottom = landing;
        }
        return bestDistance < float.MaxValue;
    }

    private static bool IsLanding(Rectangle bounds, Vector2 bottom, int width, int height)
    {
        Rectangle body = new((int)MathF.Floor(bottom.X - width / 2f), (int)MathF.Floor(bottom.Y - height), width, height);
        if (!bounds.Contains(body) || body.Left < 16 || body.Top < 16
            || body.Right >= (Main.maxTilesX - 1) * 16 || body.Bottom >= (Main.maxTilesY - 1) * 16
            || bottom.Y % 16f != 0f || !IsClear(body)) return false;
        for (int x = body.Left / 16; x <= (body.Right - 1) / 16; x++)
            if (!IsFloor(x, body.Bottom / 16)) return false;
        return true;
    }

    private static bool IsFloor(int x, int y)
    {
        Tile tile = Main.tile[x, y];
        return WorldGen.SolidTile(x, y) || tile.HasUnactuatedTile && Main.tileSolidTop[tile.TileType]
            && tile.TileFrameY == 0 && tile.Slope == SlopeType.Solid && !tile.IsHalfBlock;
    }

    private static bool IsClear(Rectangle body)
    {
        for (int x = body.Left / 16; x <= (body.Right - 1) / 16; x++)
        for (int y = body.Top / 16; y <= (body.Bottom - 1) / 16; y++)
        {
            Tile tile = Main.tile[x, y];
            if (tile.LiquidAmount > 0 || tile.HasUnactuatedTile
                && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType])
                return false;
        }
        return true;
    }
}
