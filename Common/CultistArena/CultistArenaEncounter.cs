using System;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.ID;

namespace PvPArenas.Common.CultistArena;

/// <summary>Preserves the native encounter while Arenas owns its lifetime.</summary>
internal static class CultistArenaEncounter
{
    private static int nextEncounterId;
    private static int currentEncounterId;

    internal static void Begin(NPC boss, Rectangle bounds)
    {
        if (boss.type != NPCID.CultistBoss)
            return;
        currentEncounterId = nextEncounterId = nextEncounterId == int.MaxValue ? 1 : nextEncounterId + 1;
        CultistArenaNPC arena = boss.GetGlobalNPC<CultistArenaNPC>();
        arena.EncounterId = currentEncounterId;
        arena.RootIndex = boss.whoAmI;
        arena.Bounds = bounds;
        boss.netUpdate = true;
    }

    internal static bool TryGetParent(IEntitySource source, out int encounter, out int root, out Rectangle bounds)
    {
        encounter = 0;
        root = -1;
        bounds = Rectangle.Empty;
        if (source is not EntitySource_Parent parent)
            return false;
        if (parent.Entity is NPC npc && npc.TryGetGlobalNPC(out CultistArenaNPC arena) && arena.Managed)
        {
            encounter = arena.EncounterId;
            root = arena.RootIndex;
            bounds = arena.Bounds;
        }
        else if (parent.Entity is Projectile projectile
            && projectile.TryGetGlobalProjectile(out CultistArenaProjectile attack) && attack.EncounterId != 0)
        {
            encounter = attack.EncounterId;
            root = attack.RootIndex;
            bounds = attack.Bounds;
        }
        return encounter != 0;
    }

    internal static bool TryGetRoot(CultistArenaNPC arena, out NPC root)
    {
        root = (uint)arena.RootIndex < Main.maxNPCs ? Main.npc[arena.RootIndex] : null;
        return root?.active == true && root.type == NPCID.CultistBoss
            && root.TryGetGlobalNPC(out CultistArenaNPC owner) && owner.EncounterId == arena.EncounterId;
    }

    internal static int FindTarget(NPC root, Rectangle bounds)
    {
        int target = -1;
        float bestDistance = float.MaxValue;
        foreach (Player player in Main.ActivePlayers)
        {
            if (player.dead || player.ghost || (Team)player.team is not (Team.Red or Team.Blue)
                || !bounds.Intersects(player.Hitbox))
                continue;
            float distance = Vector2.DistanceSquared(root.Center, player.Center);
            if (distance >= bestDistance)
                continue;
            bestDistance = distance;
            target = player.whoAmI;
        }
        return target;
    }

    internal static bool MoveFormation(NPC root, CultistArenaNPC arena, Vector2 desiredCenter)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient || arena.Bounds.Width <= 0 || arena.Bounds.Height <= 0)
            return false;

        Rectangle formation = root.Hitbox;
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (IsFormationNPC(npc, arena))
                formation = Rectangle.Union(formation, npc.Hitbox);
        }
        for (int i = 0; i < Main.maxProjectiles; i++)
        {
            Projectile projectile = Main.projectile[i];
            if (IsRitual(projectile, arena))
                formation = Rectangle.Union(formation, projectile.Hitbox);
        }

        Rectangle bounds = arena.Bounds;
        Vector2 ClampOffset(Vector2 candidate) => new(
            MathHelper.Clamp(candidate.X, bounds.Left - formation.Left,
                Math.Max(bounds.Left - formation.Left, bounds.Right - formation.Right)),
            MathHelper.Clamp(candidate.Y, bounds.Top - formation.Top,
                Math.Max(bounds.Top - formation.Top, bounds.Bottom - formation.Bottom)));
        Vector2 offset = ClampOffset(desiredCenter - root.Center);
        if (desiredCenter != root.Center && !IsDryDestination(root, root.Center + offset))
        {
            bool found = false;
            // Flying Cultist may traverse blocks normally. A distance recovery,
            // however, must place its body in dry open space near the fighter.
            for (int radius = 64; radius <= 1024 && !found; radius += 64)
            for (int x = -radius; x <= radius && !found; x += 64)
            for (int y = -radius; y <= radius; y += 64)
            {
                if (Math.Abs(x) != radius && Math.Abs(y) != radius)
                    continue;
                Vector2 candidate = ClampOffset(desiredCenter + new Vector2(x, y) - root.Center);
                if (!IsDryDestination(root, root.Center + candidate))
                    continue;
                offset = candidate;
                found = true;
                break;
            }
            if (!found)
                return false;
        }
        if (offset == Vector2.Zero)
            return true;

        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (!IsFormationNPC(npc, arena))
                continue;
            npc.position += offset;
            npc.netUpdate = true;
        }
        for (int i = 0; i < Main.maxProjectiles; i++)
        {
            Projectile projectile = Main.projectile[i];
            if (!IsRitual(projectile, arena))
                continue;
            projectile.position += offset;
            projectile.netUpdate = true;
        }
        return true;
    }

    private static bool IsDryDestination(NPC root, Vector2 center)
    {
        Vector2 position = center - root.Size * .5f;
        if (Collision.SolidCollision(position, root.width, root.height))
            return false;
        int left = (int)Math.Floor(position.X / 16f);
        int top = (int)Math.Floor(position.Y / 16f);
        int right = (int)Math.Floor((position.X + root.width - 1) / 16f);
        int bottom = (int)Math.Floor((position.Y + root.height - 1) / 16f);
        for (int x = left; x <= right; x++)
        for (int y = top; y <= bottom; y++)
            if (!WorldGen.InWorld(x, y, 10) || Main.tile[x, y].LiquidAmount != 0)
                return false;
        return true;
    }

    private static bool IsFormationNPC(NPC npc, CultistArenaNPC arena) => npc?.active == true
        && npc.type is NPCID.CultistBoss or NPCID.CultistBossClone
        && npc.TryGetGlobalNPC(out CultistArenaNPC member) && member.EncounterId == arena.EncounterId;

    private static bool IsRitual(Projectile projectile, CultistArenaNPC arena) => projectile?.active == true
        && projectile.type == ProjectileID.CultistRitual
        && projectile.TryGetGlobalProjectile(out CultistArenaProjectile member) && member.EncounterId == arena.EncounterId;

    internal static void Cleanup()
    {
        if (Main.netMode == NetmodeID.MultiplayerClient || currentEncounterId == 0)
            return;
        // Clear attacks first without Kill callbacks that could spawn more descendants.
        // Ownership includes harmless ritual effects and entities outside the arena.
        for (int i = 0; i < Main.maxProjectiles; i++)
        {
            Projectile projectile = Main.projectile[i];
            if (projectile?.active != true || !projectile.TryGetGlobalProjectile(out CultistArenaProjectile arena)
                || arena.EncounterId != currentEncounterId)
                continue;
            projectile.active = false;
            if (Main.netMode == NetmodeID.Server)
                NetMessage.SendData(MessageID.KillProjectile, number: projectile.identity, number2: projectile.owner);
        }
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (npc?.active != true || !npc.TryGetGlobalNPC(out CultistArenaNPC arena)
                || arena.EncounterId != currentEncounterId)
                continue;
            npc.active = false;
            npc.netUpdate = true;
            if (Main.netMode == NetmodeID.Server)
                NetMessage.SendData(MessageID.SyncNPC, number: i);
        }
        currentEncounterId = 0;
    }
}
