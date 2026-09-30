using PvPArenas.Common.Game.LoadoutSelector;
using PvPFramework.Common.Combat.TeamBoss;
using PvPFramework.Core.Configs.ConfigElements;
using System;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.ID;
using Terraria.ModLoader.Config;
using FrameworkServerConfig = PvPFramework.Core.Configs.ServerConfig;

namespace PvPArenas.Common.Game;

internal enum BossState : byte
{
    Alive,
    Missing
}

/// <summary>Owns the NPC spawned for the current Arenas round.</summary>
internal sealed class BossManager : ModSystem
{
    private const int MissingGraceTicks = 30;

    private int bossIndex = -1;
    private int bossType;
    private int missingTicks;
    private Rectangle roundArea;
    private Rectangle bossArea;
    private bool loggedGolemDistanceRecovery;

    internal int BossIndex => bossIndex;

    public override void Load()
    {
        TeamBossNPC.BossDefeatedByTeam += OnBossDefeatedByTeam;
        TeamBossNPC.BossDamageDealt += OnBossDamageDealt;
        On_NPC.TargetClosest += OnTargetClosest;
        On_NPC.AI_045_Golem += OnGolemAI;
    }

    public override void Unload()
    {
        TeamBossNPC.BossDefeatedByTeam -= OnBossDefeatedByTeam;
        TeamBossNPC.BossDamageDealt -= OnBossDamageDealt;
        On_NPC.TargetClosest -= OnTargetClosest;
        On_NPC.AI_045_Golem -= OnGolemAI;
    }

    internal bool TrySpawn(BossFightPreset preset, ArenaLayout layout)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient || preset?.Boss?.Type <= 0 || layout == null)
            return false;

        Cleanup();
        EnsureTeamBossConfigured(preset.Boss.Type);
        PrepareBossEnvironment(preset.Boss.Type);
        roundArea = new Rectangle(layout.ArenaBounds.X * 16, layout.ArenaBounds.Y * 16,
            layout.ArenaBounds.Width * 16, layout.ArenaBounds.Height * 16);
        // Plantera must be able to pursue players through both mirrored cave networks.
        Rectangle bossTiles = preset.Boss.Type == NPCID.Plantera ? layout.ArenaBounds : layout.BossBounds;
        bossArea = new Rectangle(bossTiles.X * 16, bossTiles.Y * 16,
            bossTiles.Width * 16, bossTiles.Height * 16);
        ClearRoundProjectiles();

        Point spawn = layout.BossSpawn;
        int index = NPC.NewNPC(new EntitySource_Misc("ArenasRound"), spawn.X * 16 + 8,
            spawn.Y * 16 + 8, preset.Boss.Type);
        if ((uint)index >= Main.maxNPCs || Main.npc[index]?.active != true)
            return false;

        bossIndex = index;
        bossType = preset.Boss.Type;
        missingTicks = 0;
        loggedGolemDistanceRecovery = false;
        NPC boss = Main.npc[index];
        int graceSeconds = Math.Clamp(preset.GracePeriodSeconds, 0, 300);
        boss.GetGlobalNPC<BossGraceNPC>().Begin(boss, graceSeconds);
        int target = FindManagedTarget(boss);
        boss.target = target >= 0 ? target : Main.maxPlayers;
        boss.timeLeft = Math.Max(boss.timeLeft, 3600);
        boss.netAlways = true;
        boss.netUpdate = true;
        if (Main.netMode == NetmodeID.Server)
            NetMessage.SendData(MessageID.SyncNPC, number: bossIndex);
        Log.Info($"Spawned round boss type={bossType}, index={bossIndex}, tile={spawn}, grace={graceSeconds}s.");
        return true;
    }

    internal BossState Update()
    {
        if (TryGetBoss(out NPC boss))
        {
            MaintainBossEnvironment();
            missingTicks = 0;
            int target = FindManagedTarget(boss);
            boss.target = target >= 0 ? target : Main.maxPlayers;
            ContainBoss(boss);
            boss.timeLeft = Math.Max(boss.timeLeft, 3600);
            return BossState.Alive;
        }

        missingTicks++;
        return missingTicks >= MissingGraceTicks ? BossState.Missing : BossState.Alive;
    }

    internal void Cleanup()
    {
        int ownedIndex = bossIndex;
        if (ownedIndex >= 0)
        {
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                bool ownedBoss = i == ownedIndex || npc?.realLife == ownedIndex;
                bool arenaHostile = npc?.active == true && !npc.friendly && !npc.townNPC
                    && !npc.isLikeATownNPC && npc.type != NPCID.TargetDummy
                    && roundArea.Intersects(npc.Hitbox);
                if (npc?.active != true || !ownedBoss && !arenaHostile)
                    continue;

                npc.active = false;
                npc.netUpdate = true;
                if (Main.netMode == NetmodeID.Server)
                    NetMessage.SendData(MessageID.SyncNPC, number: i);
            }
        }

        ClearRoundProjectiles();

        bossIndex = -1;
        bossType = 0;
        missingTicks = 0;
        loggedGolemDistanceRecovery = false;
        roundArea = Rectangle.Empty;
        bossArea = Rectangle.Empty;
    }

    private void ClearRoundProjectiles()
    {
        if (roundArea.Width <= 0 || roundArea.Height <= 0)
            return;

        for (int i = 0; i < Main.maxProjectiles; i++)
        {
            Projectile projectile = Main.projectile[i];
            if (projectile?.active != true || !projectile.friendly
                || projectile.owner < 0 || projectile.owner >= Main.maxPlayers
                || !roundArea.Intersects(projectile.Hitbox))
                continue;

            projectile.Kill();
            if (Main.netMode == NetmodeID.Server)
                NetMessage.SendData(MessageID.KillProjectile, -1, -1, null, i, projectile.owner);
        }
    }

    private void OnBossDefeatedByTeam(Player player, NPC npc, Team team)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient || team == Team.None || !IsOwnedBoss(npc))
            return;

        ModContent.GetInstance<RoundManager>().NotifyBossDefeated(player, team);
    }

    private void OnBossDamageDealt(Player player, uint damage, int itemType)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient || damage == 0 || player?.active != true
            || ModContent.GetInstance<RoundManager>().CurrentPhase != RoundManager.RoundPhase.Playing
            || (Team)player.team is not (Team.Red or Team.Blue) || !TryGetBoss(out NPC boss))
            return;

        Team team = (Team)player.team;
        TeamBossNPC teamBoss = boss.GetGlobalNPC<TeamBossNPC>();
        long remaining = teamBoss.TeamLife.TryGetValue(team, out int teamLife)
            ? Math.Max(0, teamLife)
            : boss.lifeMax;
        uint appliedDamage = (uint)Math.Min(damage, remaining);
        player.GetModPlayer<ArenaPlayer>().AddBossDamage(appliedDamage, itemType);
    }

    private bool TryGetBoss(out NPC boss)
    {
        if ((uint)bossIndex < Main.maxNPCs && Main.npc[bossIndex]?.active == true
            && Main.npc[bossIndex].type == bossType)
        {
            boss = Main.npc[bossIndex];
            return true;
        }

        boss = null;
        return false;
    }

    private bool IsOwnedBoss(NPC npc)
    {
        if (npc == null || bossIndex < 0)
            return false;
        return npc.whoAmI == bossIndex || npc.realLife == bossIndex;
    }

    private static int FindTarget(NPC npc, Rectangle allowedArea)
    {
        int target = -1;
        float bestDistanceSquared = float.MaxValue;
        foreach (Player player in Main.ActivePlayers)
        {
            if (player.dead || (Team)player.team is not (Team.Red or Team.Blue)
                || !allowedArea.Intersects(player.Hitbox))
                continue;

            float distanceSquared = Vector2.DistanceSquared(npc.Center, player.Center);
            if (distanceSquared >= bestDistanceSquared)
                continue;

            target = player.whoAmI;
            bestDistanceSquared = distanceSquared;
        }

        return target;
    }

    private int FindManagedTarget(NPC npc)
    {
        int target = FindTarget(npc, bossArea);

        // A 400-tile arena places Golem more than its hard-coded 3,000-pixel
        // despawn distance from both edge spawns. Give only Golem a full-arena
        // fallback target; its position is still clamped to bossArea below.
        if (target < 0 && bossType == NPCID.Golem)
            target = FindTarget(npc, roundArea);

        return target;
    }

    private void OnTargetClosest(On_NPC.orig_TargetClosest orig, NPC npc, bool faceTarget)
    {
        if (!IsOwnedBoss(npc) || bossArea.Width <= 0 || bossArea.Height <= 0)
        {
            orig(npc, faceTarget);
            return;
        }

        int target = FindManagedTarget(npc);
        npc.target = target >= 0 ? target : Main.maxPlayers;
        if (!faceTarget || target < 0)
            return;

        Player player = Main.player[target];
        npc.direction = npc.Center.X < player.Center.X ? 1 : -1;
        npc.directionY = npc.Center.Y < player.Center.Y ? 1 : -1;
    }

    private void OnGolemAI(On_NPC.orig_AI_045_Golem orig, NPC npc)
    {
        bool ownedRoundGolem = IsManagedRoundGolem(npc);

        orig(npc);

        // Vanilla Golem directly sets active=false when its Manhattan distance
        // from its target remains above 3,000 pixels. timeLeft cannot prevent it.
        // Arenas owns round completion, so keep a living managed Golem active.
        if (!ownedRoundGolem || npc.active || npc.life <= 0)
            return;

        npc.active = true;
        npc.timeLeft = Math.Max(npc.timeLeft, 3600);

        if (Main.netMode != NetmodeID.MultiplayerClient)
        {
            int target = FindManagedTarget(npc);
            npc.target = target >= 0 ? target : Main.maxPlayers;
        }

        if (loggedGolemDistanceRecovery)
            return;

        loggedGolemDistanceRecovery = true;
        if (Main.netMode == NetmodeID.MultiplayerClient)
            return;

        npc.netUpdate = true;
        Log.Info("Prevented the round Golem's vanilla 3,000-pixel target-distance despawn.");
    }

    private bool IsManagedRoundGolem(NPC npc)
    {
        if (npc?.type != NPCID.Golem || npc.life <= 0)
            return false;

        if (Main.netMode != NetmodeID.MultiplayerClient)
            return IsOwnedBoss(npc);

        // bossIndex is server-owned. Clients identify the synchronized round body
        // by the active preset and its contained position so they suppress the
        // same vanilla deactivation locally instead of flickering between syncs.
        RoundManager manager = ModContent.GetInstance<RoundManager>();
        ArenaLayout layout = manager.CurrentLayout;
        if (manager.CurrentPhase != RoundManager.RoundPhase.Playing
            || manager.SelectedBossType != NPCID.Golem || layout == null)
            return false;

        Rectangle bounds = layout.BossBounds;
        Rectangle worldBounds = new(bounds.X * 16, bounds.Y * 16,
            bounds.Width * 16, bounds.Height * 16);
        return worldBounds.Intersects(npc.Hitbox);
    }

    private void ContainBoss(NPC boss)
    {
        if (bossArea.Width <= 0 || bossArea.Height <= 0)
            return;

        float minX = bossArea.Left;
        float minY = bossArea.Top;
        float maxX = Math.Max(minX, bossArea.Right - boss.width);
        float maxY = Math.Max(minY, bossArea.Bottom - boss.height);
        Vector2 position = new(
            MathHelper.Clamp(boss.position.X, minX, maxX),
            MathHelper.Clamp(boss.position.Y, minY, maxY));
        if (position == boss.position)
            return;

        if (position.X != boss.position.X)
            boss.velocity.X = 0f;
        if (position.Y != boss.position.Y)
            boss.velocity.Y = 0f;
        boss.position = position;
        boss.netUpdate = true;
    }

    private static void PrepareBossEnvironment(int npcType)
    {
        if (npcType != NPCID.EyeofCthulhu)
            return;

        Main.dayTime = false;
        Main.time = 0;
        if (Main.netMode == NetmodeID.Server)
            NetMessage.SendData(MessageID.WorldData);
    }

    private void MaintainBossEnvironment()
    {
        if (bossType != NPCID.EyeofCthulhu || !Main.dayTime)
            return;

        Main.dayTime = false;
        Main.time = 0;
        if (Main.netMode == NetmodeID.Server)
            NetMessage.SendData(MessageID.WorldData);
    }

    private static void EnsureTeamBossConfigured(int npcType)
    {
        FrameworkServerConfig config = ModContent.GetInstance<FrameworkServerConfig>();
        config.BossBalance ??= [];
        NPCDefinition definition = new(npcType);
        if (config.BossBalance.TryGetValue(definition, out FrameworkServerConfig.BossBalanceEntry entry)
            && entry != null)
        {
            // M2 is an independent team race: only the team landing a strike may
            // reduce its own virtual life pool. Preserve configured HP/damage tuning.
            entry.TeamLifeShare = 0f;
            return;
        }

        config.BossBalance[definition] = new FrameworkServerConfig.BossBalanceEntry
        {
            LifeMaxMultiplier = 1f,
            DamageMultiplier = 1f,
            TeamLifeShare = 0f
        };
        Log.Info($"Registered NPC type {npcType} for PvP Framework team-life virtualization.");
    }
}
