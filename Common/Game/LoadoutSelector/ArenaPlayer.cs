using PvPArenas.Common.Game.TeamBalancing;
using PvPArenas.Common.Generation;
using ErkySSC.Common.RegionProtection;
using PvPFramework.Common.Scoreboard;
using PvPArenas.Common.Loadouts;
using ErkySSC.Core.Inventory;
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria.Enums;
using Terraria.ID;
using Terraria.ModLoader.Config;

namespace PvPArenas.Common.Game.LoadoutSelector;

/// <summary>Applies round loadouts, spawn rules, freezing, and arena bounds.</summary>
internal sealed class ArenaPlayer : ModPlayer
{
    private bool roundPrepared;
    private bool arenaSpawnActive;
    private bool generationStaged;
    private bool suppressArenaSpawn;
    private bool betweenRoundsBuffsCleared;

    internal int SelectedLoadoutIndex;
    private ArenaLoadoutDelivery delivery;
    internal ArenaLoadoutDelivery Delivery => delivery ??= new(Player);
    internal bool HasRoundLoadout => roundPrepared;
    internal bool AwaitingLoadoutDelivery => !roundPrepared || Delivery.AwaitingGrant;

    public override void Initialize() => delivery = new(Player);

    internal void MarkLoadoutReceived(int index)
    {
        SelectedLoadoutIndex = index;
        roundPrepared = true;
    }

    private readonly Dictionary<int, uint> bossDamageByItem = [];

    internal long BossDamage { get; private set; }

    /// <summary>Boss damage split by the item that dealt it, for the match report's item stats.</summary>
    internal IReadOnlyDictionary<int, uint> BossDamageByItem => bossDamageByItem;

    public override void Load()
    {
        On_Player.TrySwitchingLoadout += OnTrySwitchingLoadout;
        On_Player.Spawn += OnPlayerSpawn;
    }

    public override void Unload()
    {
        On_Player.TrySwitchingLoadout -= OnTrySwitchingLoadout;
        On_Player.Spawn -= OnPlayerSpawn;
    }

    public override void OnEnterWorld()
    {
        roundPrepared = false;
        arenaSpawnActive = false;
        betweenRoundsBuffsCleared = false;
        delivery = new(Player);
        if (!ModContent.GetInstance<RoundManager>().IsActive)
            return;
        ArenaRoundBuffs.Clear(Player);
        RoundManager.RoundPhase phase = ModContent.GetInstance<RoundManager>().CurrentPhase;
        if (phase is RoundManager.RoundPhase.WaitingForPlayers
            or RoundManager.RoundPhase.VotingOrEndScreen)
        {
            Player.SpawnX = -1;
            Player.SpawnY = -1;
        }
        TeamBalancer.AssignJoiningPlayer(Player);
    }

    // Max life/mana come from the boss fight, forced every tick here because
    // tModLoader resets statLifeMax to the character's vanilla value each update
    // (a one-time assignment would be wiped next frame).
    public override void ModifyMaxStats(out StatModifier health, out StatModifier mana)
    {
        health = StatModifier.Default;
        mana = StatModifier.Default;

        RoundManager manager = ModContent.GetInstance<RoundManager>();
        if (manager.CurrentPhase is not (RoundManager.RoundPhase.FreezeCountdown or RoundManager.RoundPhase.Playing)
            || (Team)Player.team is not (Team.Red or Team.Blue)
            || !manager.TryGetSelectedPreset(out BossFightPreset preset))
            return;

        // Additive 0 + Flat = value forces the stat to an absolute maximum.
        health = new StatModifier(0f, 1f, Math.Clamp(preset.MaxHealth, 1, 500));
        mana = new StatModifier(0f, 1f, Math.Clamp(preset.MaxMana, 0, 200));
    }

    public override void SetControls()
    {
        RoundManager manager = ModContent.GetInstance<RoundManager>();
        if (manager.CurrentPhase is not (RoundManager.RoundPhase.Generating or RoundManager.RoundPhase.FreezeCountdown))
            return;

        Player.controlLeft = Player.controlRight = Player.controlUp = Player.controlDown = false;
        Player.controlJump = Player.controlMount = Player.controlHook = false;
        Player.controlUseItem = Player.controlUseTile = Player.controlThrow = false;
    }

    public override bool CanUseItem(Item item) =>
        ModContent.GetInstance<RoundManager>().CurrentPhase != RoundManager.RoundPhase.Generating;

    public override void PreUpdate()
    {
        RoundManager manager = ModContent.GetInstance<RoundManager>();
        bool betweenRounds = manager.IsActive
            && manager.CurrentPhase is RoundManager.RoundPhase.WaitingForPlayers or RoundManager.RoundPhase.VotingOrEndScreen;
        if (betweenRounds && !betweenRoundsBuffsCleared)
            ArenaRoundBuffs.Clear(Player);
        betweenRoundsBuffsCleared = betweenRounds;

        // Vanilla ghosts return before PostUpdate, so release their transient handoff here.
        if (!manager.IsActive || !ArenaRespawns.IsInitialParticipant(Player)
            || manager.CurrentPhase is RoundManager.RoundPhase.WaitingForPlayers or RoundManager.RoundPhase.VotingOrEndScreen)
        {
            roundPrepared = false;
            Delivery.Reset();
        }
    }

    public override void PostUpdate()
    {
        RoundManager manager = ModContent.GetInstance<RoundManager>();
        if (!manager.IsActive)
        {
            // Release only transient Arenas spawn state when a client changes events.
            roundPrepared = generationStaged = false;
            Delivery.Reset();
            ResetArenaSpawn();
            return;
        }

        if (manager.CurrentPhase != RoundManager.RoundPhase.Generating)
            generationStaged = false;

        if (manager.CurrentPhase is RoundManager.RoundPhase.WaitingForPlayers
            or RoundManager.RoundPhase.VotingOrEndScreen)
        {
            roundPrepared = false;
            SelectedLoadoutIndex = 0;
            Delivery.Reset();

            ResetArenaSpawn();

            if (HasCarriedItems(Player))
                ClearCarriedItems(
                    Player,
                    sync: Main.netMode == NetmodeID.Server);

            if (Player.whoAmI == Main.myPlayer &&
                !Main.mouseItem.IsAir)
            {
                Main.mouseItem.TurnToAir();
            }
        }

        if (manager.CurrentPhase is RoundManager.RoundPhase.Generating or RoundManager.RoundPhase.FreezeCountdown)
        {
            Player.AddBuff(BuffID.Frozen, 2);
            Player.immune = true;
            Player.immuneTime = Math.Max(Player.immuneTime, 2);
            Player.immuneNoBlink = true;
            Player.velocity = Vector2.Zero;
        }

        if (manager.CurrentPhase == RoundManager.RoundPhase.Generating
            && ArenaRespawns.CanStage(Player))
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
                roundPrepared = false;
            Player.breath = Player.breathMax;
            Player.noFallDmg = true;
            // Clients may still see Generating after receiving their fight teleport.
            // Only the server owns staging placement; clients follow its teleports.
            if (Main.netMode == NetmodeID.Server)
            {
                if (!generationStaged)
                    Stage(Player, manager.StagingSpawn);
                Vector2 position = SpawnPosition(Player, manager.StagingSpawn);
                if (!Player.dead && Player.position != position)
                {
                    Player.position = position;
                    Player.fallStart = (int)(position.Y / 16f);
                }
            }
        }

        if ((manager.CurrentPhase is RoundManager.RoundPhase.FreezeCountdown or RoundManager.RoundPhase.Playing)
            && ArenaRespawns.IsInitialParticipant(Player))
        {
            if (!roundPrepared
                && manager.CurrentLayout != null
                && manager.TryGetSelectedPreset(out BossFightPreset preset))
            {
                if (Main.netMode == NetmodeID.MultiplayerClient)
                {
                    if (Player.whoAmI == Main.myPlayer && preset.IsSandbox())
                    {
                        ArenaRoundBuffs.Clear(Player);
                        ApplyLoadout(Player, preset);
                        roundPrepared = true;
                    }
                }
                else
                {
                    if (!Prepare(Player, preset, manager.CurrentLayout))
                        return;
                }
            }

            if (!roundPrepared && (Main.netMode != NetmodeID.MultiplayerClient || Player.whoAmI == Main.myPlayer))
            {
                Player.hostile = false;
                Player.immune = Player.immuneNoBlink = true;
                Player.immuneTime = Math.Max(Player.immuneTime, 2);
                Player.noFallDmg = true;
                Player.breath = Player.breathMax;
                return;
            }
            Player.hostile = true;
            KeepInsideArena(manager.CurrentLayout);
        }
    }

    internal static bool Prepare(Player player, BossFightPreset preset, ArenaLayout layout)
    {
        if (Main.netMode != NetmodeID.Server || !ArenaRespawns.IsInitialParticipant(player)
            || preset == null || layout == null)
            return false;

        if (!ArenaLoadoutDelivery.RequestPreferences(player, preset))
            return true; // Pending late join; PostUpdate retries without placing or granting twice.

        if (!ArenaRespawns.TrySelect(player, preset, layout, out Point spawn))
        {
            ModContent.GetInstance<RoundManager>().ReportSpawnFailure(
                $"No safe {(Team)player.team} spawn remains in this arena.");
            return false;
        }

        // Transition participants already acknowledged the entire world. A late join still
        // receives the fight area here before teleporting out of its initially loaded sections.
        if (ModContent.GetInstance<RoundManager>().CurrentPhase != RoundManager.RoundPhase.Generating)
            ArenaTileSync.Send(layout.ArenaBounds, player.whoAmI);
        return PrepareAtSpawn(player, preset, layout, spawn);
    }

    // Initial participants already verified the world. Use the reserved position without
    // selecting again or resending the whole arena after publishing countdown.
    internal static bool PrepareAtSpawn(Player player, BossFightPreset preset, ArenaLayout layout, Point spawn)
    {
        if (Main.netMode != NetmodeID.Server || !ArenaRespawns.IsInitialParticipant(player)
            || preset == null || layout == null)
            return false;
        if (!ArenaRespawns.IsValid(spawn, ArenaRespawns.TeamArea(layout, (Team)player.team)))
        {
            ModContent.GetInstance<RoundManager>().ReportSpawnFailure(
                $"The reserved {(Team)player.team} starting position is no longer safe.");
            return false;
        }

        ArenaPlayer arenaPlayer = player.GetModPlayer<ArenaPlayer>();
        arenaPlayer.SetArenaSpawn(spawn);
        arenaPlayer.Revive();
        arenaPlayer.ResetBossDamage();
        ScoreboardService.ResetPlayer(player);
        arenaPlayer.Delivery.BeginRound();
        ApplyLoadout(player, preset);
        Teleport(player, spawn);
        arenaPlayer.roundPrepared = true;
        player.hostile = true;

        if (Main.netMode == NetmodeID.Server)
            NetMessage.SendData(MessageID.TogglePVP, -1, -1, null, player.whoAmI);
        return true;
    }

    internal static void ReleaseAll()
    {
        if (Main.netMode == NetmodeID.MultiplayerClient)
            return;

        foreach (Player player in Main.player)
        {
            if (player?.active != true)
                continue;

            ArenaPlayer arenaPlayer = player.GetModPlayer<ArenaPlayer>();
            arenaPlayer.roundPrepared = false;
            arenaPlayer.generationStaged = false;
            arenaPlayer.Delivery.Reset();
            arenaPlayer.SelectedLoadoutIndex = 0;
            arenaPlayer.ResetArenaSpawn();
            ClearCarriedItems(player, sync: Main.netMode == NetmodeID.Server);
            ArenaRoundBuffs.Clear(player);
            if (player.hostile && (Team)player.team is Team.Red or Team.Blue)
            {
                player.hostile = false;
                if (Main.netMode == NetmodeID.Server)
                    NetMessage.SendData(MessageID.TogglePVP, -1, -1, null, player.whoAmI);
            }
        }
    }

    internal static void Stage(Player player, Point spawn)
    {
        if (Main.netMode != NetmodeID.Server || !ArenaRespawns.CanStage(player))
            return;
        ArenaPlayer arenaPlayer = player.GetModPlayer<ArenaPlayer>();
        arenaPlayer.generationStaged = true;
        arenaPlayer.roundPrepared = false;
        arenaPlayer.Delivery.Reset();
        arenaPlayer.ResetArenaSpawn();
        player.chest = -1;
        player.sign = -1;
        player.tileEntityAnchor.Clear();
        NetMessage.SendData(MessageID.SyncPlayerChest, player.whoAmI, -1, null, -1);
        ClearCarriedItems(player, sync: true);
        arenaPlayer.Revive();
        player.hostile = false;
        player.mount.Dismount(player);
        player.RemoveAllGrapplingHooks();
        Teleport(player, spawn);
        NetMessage.SendData(MessageID.TogglePVP, -1, -1, null, player.whoAmI);
        if (ArenaRespawns.IsInitialParticipant(player)
            && ModContent.GetInstance<RoundManager>().TryGetSelectedPreset(out BossFightPreset preset))
            ArenaLoadoutDelivery.RequestPreferences(player, preset);
    }

    private static void OnTrySwitchingLoadout(On_Player.orig_TrySwitchingLoadout orig,
        Player player, int loadoutIndex)
    {
        RoundManager.RoundPhase phase = ModContent.GetInstance<RoundManager>().CurrentPhase;
        if (phase is RoundManager.RoundPhase.Generating or RoundManager.RoundPhase.FreezeCountdown
            or RoundManager.RoundPhase.Playing)
            return;

        orig(player, loadoutIndex);
    }

    private static void OnPlayerSpawn(On_Player.orig_Spawn orig, Player player,
        PlayerSpawnContext context)
    {
        ArenaPlayer arenaPlayer = player.GetModPlayer<ArenaPlayer>();
        if (Main.netMode != NetmodeID.Server || arenaPlayer.suppressArenaSpawn)
        {
            orig(player, context);
            return;
        }
        RoundManager manager = ModContent.GetInstance<RoundManager>();
        if (manager.CurrentPhase == RoundManager.RoundPhase.Generating)
        {
            orig(player, context);
            Stage(player, manager.StagingSpawn);
            return;
        }
        Team team = (Team)player.team;
        if (!player.active || team is not (Team.Red or Team.Blue)
            || manager.CurrentPhase is not (RoundManager.RoundPhase.FreezeCountdown or RoundManager.RoundPhase.Playing)
            || manager.CurrentLayout == null || !manager.TryGetSelectedPreset(out BossFightPreset preset))
        {
            orig(player, context);
            return;
        }

        if (!ArenaRespawns.TrySelect(player, preset, manager.CurrentLayout, out Point spawn))
        {
            manager.ReportSpawnFailure($"No safe {team} spawn remains in this arena.");
            player.SpawnX = player.SpawnY = -1;
            orig(player, context);
            return;
        }

        // Packet 12 rebroadcasts these coordinates after Spawn returns. Other clients
        // therefore see the server's choice, and the owner receives the teleport below.
        arenaPlayer.SetArenaSpawn(spawn);
        orig(player, context);
        ArenaTileSync.Send(new Rectangle(spawn.X - 2, spawn.Y - 4, 5, 6), player.whoAmI);
        Teleport(player, spawn);
    }

    private void Revive()
    {
        if (!Player.dead)
            return;
        suppressArenaSpawn = true;
        try { Player.Spawn(PlayerSpawnContext.ReviveFromDeath); }
        finally { suppressArenaSpawn = false; }
    }

    private static bool HasCarriedItems(Player player) => PlayerLoadouts.HasItems(player);

    private static void ClearCarriedItems(Player player, bool sync) => PlayerLoadouts.Clear(player, sync);

    internal void AddBossDamage(uint damage, int itemType)
    {
        // A zero delta would create a zero-valued item stat, which Tavernkeep rejects.
        if (damage == 0)
            return;

        BossDamage = BossDamage > long.MaxValue - damage ? long.MaxValue : BossDamage + damage;

        if (itemType <= ItemID.None)
            return;

        uint previous = bossDamageByItem.GetValueOrDefault(itemType);
        bossDamageByItem[itemType] = previous > uint.MaxValue - damage ? uint.MaxValue : previous + damage;
    }

    private void KeepInsideArena(ArenaLayout layout)
    {
        if (layout == null || Player.dead || Player.ghost)
            return;

        Rectangle area = ArenaSpawnBoxes.TileToWorld(layout.ArenaBounds);
        float maxX = Math.Max(area.Left, area.Right - Player.width);
        float maxY = Math.Max(area.Top, area.Bottom - Player.height);
        Vector2 position = new(
            MathHelper.Clamp(Player.position.X, area.Left, maxX),
            MathHelper.Clamp(Player.position.Y, area.Top, maxY));
        if (position == Player.position)
            return;

        if (position.X != Player.position.X)
            Player.velocity.X = 0f;
        if (position.Y != Player.position.Y)
            Player.velocity.Y = 0f;
        Player.position = position;

        if (Main.netMode == NetmodeID.Server)
            NetMessage.SendData(MessageID.PlayerControls, -1, -1, null, Player.whoAmI);
    }

    private void ResetBossDamage()
    {
        BossDamage = 0;
        bossDamageByItem.Clear();
    }

    private void SetArenaSpawn(Point spawn)
    {
        arenaSpawnActive = true;
        Player.SpawnX = spawn.X;
        Player.SpawnY = spawn.Y;
    }

    private void ResetArenaSpawn()
    {
        if (!arenaSpawnActive)
            return;

        arenaSpawnActive = false;
        Player.SpawnX = -1;
        Player.SpawnY = -1;
    }

    internal static void RequestLoadoutSelect(int loadoutIndex)
    {
        RoundManager manager = ModContent.GetInstance<RoundManager>();

        if (manager.CurrentPhase != RoundManager.RoundPhase.FreezeCountdown)
        {
            return;
        }

        if (!manager.TryGetSelectedPreset(out BossFightPreset preset) ||
            !IsValidLoadoutIndex(preset, loadoutIndex))
        {
            return;
        }

        Player player = Main.LocalPlayer;
        ArenaPlayer arenaPlayer = player.GetModPlayer<ArenaPlayer>();

        LocalInventoryPositions.Stop();

        arenaPlayer.SelectedLoadoutIndex = loadoutIndex;

        // Sandbox kits are local. Fixed kits include preferences in the selection request.
        if (preset.IsSandbox()) ApplyLoadout(player, preset);

        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            ModPacket packet =
                ModContent.GetInstance<PvPArenas>().GetPacket();

            packet.Write((byte)PvPArenas.PacketType.SelectLoadout);
            packet.Write(loadoutIndex);
            packet.Write(preset.Boss.Type);
            ArenaLoadoutDelivery.WritePreferences(packet, preset, loadoutIndex);
            packet.Send();
            return;
        }

        HandleLoadoutSelect(Main.myPlayer, loadoutIndex, preset.Boss.Type, null);
    }

    internal static void HandleLoadoutSelect(
        int playerId,
        int loadoutIndex, int boss, int[] positions)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient)
            return;

        RoundManager manager = ModContent.GetInstance<RoundManager>();

        if (manager.CurrentPhase != RoundManager.RoundPhase.FreezeCountdown)
        {
            Log.Chat(
                $"[LoadoutSelect] Rejected for player {playerId}: " +
                $"phase is {manager.CurrentPhase}.");

            return;
        }

        if (playerId < 0 ||
            playerId >= Main.maxPlayers ||
            Main.player[playerId]?.active != true)
        {
            Log.Chat(
                $"[LoadoutSelect] Rejected: player {playerId} is inactive.");

            return;
        }

        if (!manager.TryGetSelectedPreset(out BossFightPreset preset) || preset.Boss.Type != boss
            || !ArenaRespawns.IsInitialParticipant(Main.player[playerId])
            || !IsValidLoadoutIndex(preset, loadoutIndex))
        {
            Log.Chat(
                $"[LoadoutSelect] Rejected: loadout {loadoutIndex} is invalid.");

            return;
        }

        Player player = Main.player[playerId];

        ArenaPlayer owner = player.GetModPlayer<ArenaPlayer>();
        if (!owner.roundPrepared) return;
        if (!preset.IsSandbox()) owner.Delivery.SetPreferences(preset, loadoutIndex, positions);
        owner.SelectedLoadoutIndex = loadoutIndex;

        ApplyLoadout(player, preset);

        Log.Chat(
            $"[Loadout] {player.name} selected " +
            $"'{GetLoadoutName(preset, loadoutIndex)}'.");
    }

    internal static bool IsValidLoadoutIndex(
        BossFightPreset preset,
        int index)
    {
        return preset?.Loadouts != null &&
            index >= 0 &&
            index < preset.Loadouts.Count &&
            preset.Loadouts[index]?.Loadout != null;
    }

    private static string GetLoadoutName(
        BossFightPreset preset,
        int index)
    {
        if (!IsValidLoadoutIndex(preset, index))
            return $"Loadout {index + 1}";

        string name = preset.Loadouts[index].Name;

        return string.IsNullOrWhiteSpace(name)
            ? $"Loadout {index + 1}"
            : name;
    }

    private static void ApplyLoadout(Player player, BossFightPreset preset)
    {
        Loadout loadout = Main.netMode == NetmodeID.Server && !preset.IsSandbox()
            ? player.GetModPlayer<ArenaPlayer>().Delivery.Arrange(preset, player.GetModPlayer<ArenaPlayer>().SelectedLoadoutIndex)
            : ResolveLoadout(preset, player);
        IReadOnlyList<LoadoutItem> equipped = LoadoutSlotLayout.ArmorItems(loadout);
        List<LoadoutSlot> slots = [];
        for (int i = 0; i < equipped.Count; i++)
            slots.Add(new(PlayerItemSlotID.Armor0 + i,
                equipped[i]?.Item?.Type ?? ItemID.None, equipped[i]?.Stack ?? 1));
        for (int i = 0; i < (loadout.Inventory?.Count ?? 0) && i < LoadoutSlotLayout.SlotCount; i++)
            if (loadout.Inventory[i] is LoadoutItem item)
                slots.Add(new(PlayerItemSlotID.Inventory0 + i, item.Item?.Type ?? ItemID.None, item.Stack));
        slots.Add(new(PlayerItemSlotID.Misc0 + 4, loadout.Equipment?.GrapplingHook?.Type ?? ItemID.None));
        slots.Add(new(PlayerItemSlotID.Misc0 + 3, loadout.Equipment?.Mount?.Type ?? ItemID.None));
        PlayerLoadouts.Apply(new(player, slots, Synchronize: false));

        player.dead = false;
        player.ghost = false;
        player.respawnTimer = 0;
        // Max life/mana are enforced each tick in ModifyMaxStats; fill up to them here.
        player.statLife = Math.Clamp(preset.MaxHealth, 1, 500);
        player.statMana = Math.Clamp(preset.MaxMana, 0, 200);

        Log.Chat($"[Loadout] Applied to {player.name}: {CountNonAir(player.inventory)} inventory items, "
            + $"armor '{player.armor[0].Name}'/'{player.armor[1].Name}'/'{player.armor[2].Name}', "
            + $"hook '{player.miscEquips[4].Name}'.");

        if (Main.netMode != NetmodeID.Server)
            return;

        if (!preset.IsSandbox())
            player.GetModPlayer<ArenaPlayer>().Delivery.SendGrant(player, preset);
        else
        {
            PlayerLoadouts.Synchronize(player);
            NetMessage.SendData(MessageID.PlayerLifeMana, number: player.whoAmI);
            NetMessage.SendData(MessageID.PlayerMana, number: player.whoAmI);
        }
    }

    internal static Loadout ResolveLoadout(
    BossFightPreset preset,
    Player player)
    {
        int index = player?
            .GetModPlayer<ArenaPlayer>()
            .SelectedLoadoutIndex ?? 0;

        return ResolveLoadout(preset, index);
    }

    internal static Loadout ResolveLoadout(
        BossFightPreset preset,
        int loadoutIndex = 0)
    {
        Loadout loadout =
            ResolveBaseLoadout(preset, loadoutIndex);

        // Sandbox loadouts are already the player's exact slot layout; the reorder
        // pass only applies to fixed preset loadouts.
        if (preset?.IsSandbox() == true || Main.dedServ || Main.netMode == NetmodeID.Server)
            return loadout;

        return LocalLoadoutPositions.Apply(
            preset,
            loadoutIndex,
            loadout);
    }

    internal static Loadout ResolveBaseLoadout(
        BossFightPreset preset,
        int loadoutIndex = 0)
    {
        List<ArenaLoadoutOption> options =
            preset?.Loadouts;

        if (options == null || options.Count == 0)
            return new Loadout();

        // Sandbox loadouts live only in the local per-player store, never the shared
        // config, and must not fall back to a boss's built-in default kit.
        if (preset.IsSandbox())
        {
            int sandboxIndex = Math.Clamp(loadoutIndex, 0, options.Count - 1);
            return Main.dedServ
                ? new Loadout()
                : LocalSandboxLoadouts.Get(preset, sandboxIndex);
        }

        loadoutIndex =
            Math.Clamp(loadoutIndex, 0, options.Count - 1);

        Loadout loadout =
            options[loadoutIndex]?.Loadout;

        if (!IsLoadoutEmpty(loadout))
            return loadout;

        BossFightPreset defaults =
            FightPresets.CreateFightPresets()
                .FirstOrDefault(entry =>
                    entry?.Boss?.Type == preset?.Boss?.Type);

        if (defaults?.Loadouts?.Count > 0)
        {
            loadoutIndex =
                Math.Clamp(
                    loadoutIndex,
                    0,
                    defaults.Loadouts.Count - 1);

            loadout =
                defaults.Loadouts[loadoutIndex]?.Loadout
                ?? defaults.Loadouts[0]?.Loadout;
        }

        return loadout ?? new Loadout();
    }

    private static bool IsLoadoutEmpty(Loadout loadout)
    {
        if (loadout == null)
            return true;

        bool anyEquipped = (loadout.Armor?.Head?.Type ?? 0) > 0
            || (loadout.Armor?.Body?.Type ?? 0) > 0
            || (loadout.Armor?.Legs?.Type ?? 0) > 0
            || (loadout.Accessories?.Accessory1?.Type ?? 0) > 0
            || (loadout.Accessories?.Accessory2?.Type ?? 0) > 0
            || (loadout.Accessories?.Accessory3?.Type ?? 0) > 0
            || (loadout.Accessories?.Accessory4?.Type ?? 0) > 0
            || (loadout.Accessories?.Accessory5?.Type ?? 0) > 0
            || (loadout.Equipment?.GrapplingHook?.Type ?? 0) > 0
            || (loadout.Equipment?.Mount?.Type ?? 0) > 0;
        bool anyItems = loadout.Inventory?.Any(entry => entry?.Item?.Type > 0) == true;
        return !anyEquipped && !anyItems;
    }

    private static int CountNonAir(Item[] items)
    {
        int count = 0;
        foreach (Item item in items)
            if (item?.IsAir == false)
                count++;
        return count;
    }

    private static void Teleport(Player player, Point tile)
    {
        Vector2 position = SpawnPosition(player, tile);
        player.Teleport(position, TeleportationStyleID.RodOfDiscord);
        player.velocity = Vector2.Zero;

        if (Main.netMode == NetmodeID.Server)
            RegionTeleportSystem.Synchronize(player, TeleportationStyleID.RodOfDiscord);
    }

    private static Vector2 SpawnPosition(Player player, Point tile) =>
        new(tile.X * 16f + 8f - player.width / 2f, tile.Y * 16f - player.height);
}
