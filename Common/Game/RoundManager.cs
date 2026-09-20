using PvPArenas.Common.Game.BossVoting;
using PvPArenas.Common.Game.LoadoutSelector;
using PvPArenas.Common.Game.Score;
using PvPArenas.Common.Game.TeamBalancing;
using PvPArenas.Common.Generation;
using PvPArenas.Core.Configs;
using PvPFramework.Common.EndScreen;
using System;
using System.IO;
using System.Linq;
using Terraria.Enums;
using Terraria.GameContent.Creative;
using Terraria.GameContent.NetModules;
using Terraria.ID;
using Terraria.Net;

namespace PvPArenas.Common.Game;

/// <summary>Server-authoritative Arenas round loop.</summary>
internal sealed class RoundManager : ModSystem
{
    private const int TicksPerSecond = 60;

    internal enum RoundPhase : byte
    {
        WaitingForPlayers,
        VotingOrEndScreen,
        Generating,
        FreezeCountdown,
        Playing
    }

    internal enum AdminAction : byte
    {
        StartRound,
        EndRound,
        StartVoting,
        EndVoting,
        SetIdle,
        AutoBalanceTeams
    }

    private enum RoundEndReason : byte
    {
        BossDefeated,
        TimeExpired,
        BossDespawned,
        SpawnFailed,
        ArenaUnavailable,
        NoPlayers,
        AdminEnded
    }

    private RoundPhase currentPhase = RoundPhase.WaitingForPlayers;
    private int remainingTicks;
    private bool timerPaused;
    private bool idleHeld;
    private int selectedPresetIndex = -1;
    private ArenaLayout currentLayout;

    /// <summary>UTC time the current fight (Playing phase) began; used for match reporting.</summary>
    internal System.DateTime RoundStartUtc { get; private set; } = System.DateTime.UtcNow;
    private Team pendingWinningTeam;
    private int pendingWinningPlayer = -1;

    internal RoundPhase CurrentPhase => currentPhase;
    internal int RemainingTicks => remainingTicks;
    internal bool IsTimerPaused => timerPaused;
    internal bool IsIdleHeld => idleHeld;
    internal int SelectedPresetIndex => selectedPresetIndex;
    internal ArenaLayout CurrentLayout => currentLayout;

    internal int SelectedBossType => TryGetSelectedPreset(out BossFightPreset preset)
        ? preset.Boss.Type
        : NPCID.None;

    public override void PostUpdateEverything()
    {
        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            TickClientTimer();
            return;
        }

        if (!Main.player.Any(player => player?.active == true))
        {
            if (currentPhase != RoundPhase.WaitingForPlayers)
                FinishRound(RoundEndReason.NoPlayers);
            return;
        }

        TeamBalancer.AssignUnassignedPlayers();

        if (currentPhase == RoundPhase.WaitingForPlayers)
        {
            if (!idleHeld)
                StartIntermission();
            return;
        }

        if (currentPhase == RoundPhase.Playing)
        {
            if (pendingWinningTeam != Team.None)
            {
                Team winner = pendingWinningTeam;
                int player = pendingWinningPlayer;
                pendingWinningTeam = Team.None;
                pendingWinningPlayer = -1;
                FinishRound(RoundEndReason.BossDefeated, winner, player);
                return;
            }

            // Sandbox rounds have no boss to track; they simply run until the timer ends.
            if (!IsSandboxRound
                && ModContent.GetInstance<BossManager>().Update() == BossState.Missing)
            {
                FinishRound(RoundEndReason.BossDespawned);
                return;
            }
        }

        if (timerPaused || remainingTicks <= 0)
            return;

        remainingTicks--;
        if (remainingTicks > 0)
            return;

        switch (currentPhase)
        {
            case RoundPhase.VotingOrEndScreen:
                PrepareRound();
                break;
            case RoundPhase.FreezeCountdown:
                StartPlaying();
                break;
            case RoundPhase.Playing:
                FinishRound(RoundEndReason.TimeExpired);
                break;
        }
    }

    private bool IsSandboxRound =>
        TryGetSelectedPreset(out BossFightPreset preset) && preset.IsSandbox();

    internal bool TryGetSelectedPreset(out BossFightPreset preset)
    {
        var presets = ModContent.GetInstance<ServerConfig>().FightPresets;
        if (presets != null && selectedPresetIndex >= 0 && selectedPresetIndex < presets.Count)
        {
            preset = presets[selectedPresetIndex];
            return BossVoteSystem.IsVotable(preset);
        }

        preset = null;
        return false;
    }

    internal void NotifyBossDefeated(Player player, Team team)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient || currentPhase != RoundPhase.Playing
            || team is not (Team.Red or Team.Blue) || pendingWinningTeam != Team.None)
            return;

        pendingWinningTeam = team;
        pendingWinningPlayer = player?.whoAmI ?? -1;
        Log.Info($"[M2-BossVictory] team={team}, player={pendingWinningPlayer}.");
    }

    internal void SetRemainingSeconds(int seconds)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient || !IsTimedPhase(currentPhase))
            return;

        remainingTicks = SecondsToTicks(seconds);
        SyncState();
    }

    internal void ToggleTimerPaused()
    {
        if (Main.netMode == NetmodeID.MultiplayerClient || !IsTimedPhase(currentPhase))
            return;

        timerPaused = !timerPaused;
        SyncState();
    }

    internal static void RequestAdminAction(AdminAction action)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            ModPacket packet = ModContent.GetInstance<PvPArenas>().GetPacket();
            packet.Write((byte)PvPArenas.PacketType.AdminRoundAction);
            packet.Write((byte)action);
            packet.Send();
            return;
        }

        ModContent.GetInstance<RoundManager>().ExecuteAdminAction(action, Main.myPlayer);
    }

    internal void ExecuteAdminAction(AdminAction action, int playerId)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient)
            return;

        Log.Info($"[M2-Admin] player={playerId}, action={action}, phase={currentPhase}.");
        switch (action)
        {
            case AdminAction.StartRound:
                idleHeld = false;
                if (currentPhase == RoundPhase.FreezeCountdown)
                    StartPlaying();
                else if (currentPhase is RoundPhase.WaitingForPlayers or RoundPhase.VotingOrEndScreen)
                    PrepareRound();
                break;

            case AdminAction.EndRound:
                if (currentPhase == RoundPhase.Playing)
                    FinishRound(RoundEndReason.AdminEnded);
                break;

            case AdminAction.StartVoting:
                idleHeld = false;
                if (currentPhase == RoundPhase.Playing)
                    FinishRound(RoundEndReason.AdminEnded);
                else if (currentPhase is not (RoundPhase.VotingOrEndScreen or RoundPhase.Generating))
                {
                    ModContent.GetInstance<BossManager>().Cleanup();
                    ArenaPlayer.ReleaseAll();
                    StartIntermission();
                }
                break;

            case AdminAction.EndVoting:
                if (currentPhase == RoundPhase.VotingOrEndScreen)
                    PrepareRound();
                break;

            case AdminAction.SetIdle:
                if (currentPhase == RoundPhase.Generating)
                    break;
                bool wasAlreadyWaiting = currentPhase == RoundPhase.WaitingForPlayers;
                bool wasAlreadyIdle = idleHeld;
                ModContent.GetInstance<BossManager>().Cleanup();
                ArenaPlayer.ReleaseAll();
                EndScreenService.Hide();
                pendingWinningTeam = Team.None;
                pendingWinningPlayer = -1;
                idleHeld = true;
                selectedPresetIndex = -1;
                currentLayout = null;
                SetPhase(RoundPhase.WaitingForPlayers, 0);
                if (Main.netMode == NetmodeID.SinglePlayer && wasAlreadyWaiting && !wasAlreadyIdle)
                    Main.NewText("Idle", Color.Lerp(Color.LightGray, Color.LightSteelBlue, .45f));
                break;

            case AdminAction.AutoBalanceTeams:
                if (currentPhase is RoundPhase.WaitingForPlayers or RoundPhase.VotingOrEndScreen)
                    TeamBalancer.AutoBalanceTeams();
                break;
        }
    }

    private void StartIntermission()
    {
        ArenaPlayer.ReleaseAll();
        TeamBalancer.AssignUnassignedPlayers();
        if (!TeamBalancer.AllActivePlayersAssigned())
        {
            SetPhase(RoundPhase.WaitingForPlayers, 0);
            return;
        }

        ModContent.GetInstance<BossVoteSystem>().Reset();
        selectedPresetIndex = FindFirstPlayablePreset();
        int seconds = Math.Max(1, ModContent.GetInstance<ServerConfig>().VotingDurationSeconds);
        SetPhase(RoundPhase.VotingOrEndScreen, SecondsToTicks(seconds));
    }

    private void PrepareRound()
    {
        EndScreenService.Hide();

        int votedPreset = ModContent.GetInstance<BossVoteSystem>().ResolveWinner();
        if (votedPreset >= 0)
            selectedPresetIndex = votedPreset;

        if (!TryGetSelectedPreset(out BossFightPreset preset))
        {
            Log.Warn("[M2-Prepare] No playable boss preset is configured; retrying after the intermission.");
            StartIntermission();
            return;
        }

        Player[] participants = Main.player
            .Where(player => player?.active == true && (Team)player.team is Team.Red or Team.Blue)
            .ToArray();
        if (participants.Length == 0)
        {
            Log.Warn("[M2-Prepare] No active Red or Blue players were available after automatic assignment.");
            StartIntermission();
            return;
        }

        SetPhase(RoundPhase.Generating, 0);
        if (!ArenaGeneration.TryResolve(preset, out ArenaLayout layout, out string failure))
        {
            Log.Warn($"[M2-Prepare] Existing-terrain arena resolution failed: {failure}");
            FinishRound(RoundEndReason.ArenaUnavailable);
            return;
        }

        currentLayout = layout;
        ArenaBorder.ClearSpawnBoxes(currentLayout);
        foreach (Player player in participants)
            ArenaPlayer.Prepare(player, preset, currentLayout);

        int countdownSeconds = Math.Max(0, ModContent.GetInstance<ServerConfig>().FreezeCountdownSeconds);
        if (countdownSeconds == 0)
        {
            StartPlaying();
            return;
        }

        SetPhase(RoundPhase.FreezeCountdown, SecondsToTicks(countdownSeconds));
    }

    internal static void SendArenaSections(Player player, ArenaLayout layout)
    {
        if (Main.netMode != NetmodeID.Server || player?.active != true || layout == null
            || player.whoAmI < 0 || player.whoAmI >= Main.maxPlayers)
            return;

        Rectangle bounds = layout.ArenaBounds;
        int minSectionX = Math.Max(0, bounds.Left / ArenaMapSystem.SectionWidth);
        int maxSectionX = Math.Min((Main.maxTilesX - 1) / ArenaMapSystem.SectionWidth,
            Math.Max(bounds.Left, bounds.Right - 1) / ArenaMapSystem.SectionWidth);
        int minSectionY = Math.Max(0, bounds.Top / ArenaMapSystem.SectionHeight);
        int maxSectionY = Math.Min((Main.maxTilesY - 1) / ArenaMapSystem.SectionHeight,
            Math.Max(bounds.Top, bounds.Bottom - 1) / ArenaMapSystem.SectionHeight);
        int sectionsPerPlayer = (maxSectionX - minSectionX + 1)
            * (maxSectionY - minSectionY + 1);

        for (int sectionY = minSectionY; sectionY <= maxSectionY; sectionY++)
            for (int sectionX = minSectionX; sectionX <= maxSectionX; sectionX++)
                NetMessage.SendSection(player.whoAmI, sectionX, sectionY);

        Log.Chat($"Sent {sectionsPerPlayer} arena map sections to {player.name} ({player.whoAmI}); bounds={bounds}.");
    }

    private void StartPlaying()
    {
        if (!TryGetSelectedPreset(out BossFightPreset preset) || currentLayout == null)
        {
            FinishRound(RoundEndReason.SpawnFailed);
            return;
        }

        // Sandbox arenas run bossless; every other arena must spawn its NPC.
        if (!preset.IsSandbox()
            && !ModContent.GetInstance<BossManager>().TrySpawn(preset, currentLayout))
        {
            FinishRound(RoundEndReason.SpawnFailed);
            return;
        }

        RoundStartUtc = System.DateTime.UtcNow;
        int seconds = Math.Max(1, ModContent.GetInstance<ServerConfig>().RoundDurationSeconds);
        SetPhase(RoundPhase.Playing, SecondsToTicks(seconds));
    }

    private void FinishRound(RoundEndReason reason, Team winningTeam = Team.None, int winningPlayer = -1)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient)
            return;

        ModContent.GetInstance<BossManager>().Cleanup();
        ArenaPlayer.ReleaseAll();
        pendingWinningTeam = Team.None;
        pendingWinningPlayer = -1;
        Log.Info($"[M2-RoundEnd] reason={reason}, winner={winningTeam}, player={winningPlayer}.");

        if (reason == RoundEndReason.NoPlayers)
        {
            EndScreenService.Hide();
            selectedPresetIndex = -1;
            currentLayout = null;
            SetPhase(RoundPhase.WaitingForPlayers, 0);
            return;
        }

        if (reason is RoundEndReason.BossDefeated or RoundEndReason.TimeExpired
            or RoundEndReason.BossDespawned or RoundEndReason.AdminEnded)
            ArenaEndScreen.Present(winningTeam, winningPlayer);

        StartIntermission();
    }

    private int FindFirstPlayablePreset()
    {
        var presets = ModContent.GetInstance<ServerConfig>().FightPresets;
        if (presets == null)
            return -1;

        for (int i = 0; i < presets.Count; i++)
            if (BossVoteSystem.IsVotable(presets[i]))
                return i;
        return -1;
    }

    private void SetPhase(RoundPhase newPhase, int durationTicks)
    {
        RoundPhase oldPhase = currentPhase;
        bool wasIdleHeld = idleHeld;
        currentPhase = newPhase;
        remainingTicks = Math.Max(0, durationTicks);
        timerPaused = false;
        ArenaSpawnBoxIntegration.UpdateMatchState();

        int players = Main.player.Count(player => player?.active == true);
        Log.Info($"[M2-Phase] {oldPhase} -> {newPhase}; ticks={remainingTicks}, preset={selectedPresetIndex}, players={players}.");
        UpdateFreezeTime(newPhase != RoundPhase.Playing);
        if (Main.netMode == NetmodeID.SinglePlayer)
            AnnouncePhaseChange(oldPhase, newPhase, wasIdleHeld, idleHeld, SelectedBossType);
        SyncState();
    }

    private static void AnnouncePhaseChange(
        RoundPhase oldPhase,
        RoundPhase newPhase,
        bool wasIdleHeld,
        bool isIdleHeld,
        int bossType)
    {
        if (Main.dedServ)
            return;

        if (oldPhase == RoundPhase.Playing && newPhase != RoundPhase.Playing)
            Main.NewText("Game has ended!", Color.Lerp(Color.Gold, Color.LightGoldenrodYellow, .35f));

        if (newPhase == RoundPhase.FreezeCountdown && oldPhase != RoundPhase.FreezeCountdown)
            Main.NewText(BossGradient(ArenaAnnouncement(bossType)), new Color(175, 75, 255));

        if (newPhase == RoundPhase.WaitingForPlayers
            && (oldPhase != RoundPhase.WaitingForPlayers || isIdleHeld != wasIdleHeld))
            Main.NewText("Idle", Color.Lerp(Color.LightGray, Color.LightSteelBlue, .45f));
    }

    /// <summary>
    /// Wraps each character in a chat color tag running light purple -> vanilla boss purple -> dark purple.
    /// The edges follow a light-to-dark lerp while a sine weight pulls the center to the vanilla boss color.
    /// </summary>
    private static string BossGradient(string text)
    {
        Color light = new(225, 180, 255), bossPurple = new(175, 75, 255), dark = new(94, 27, 158);
        System.Text.StringBuilder builder = new(text.Length * 12);
        float lastIndex = Math.Max(1, text.Length - 1);
        for (int i = 0; i < text.Length; i++)
        {
            char letter = text[i];
            if (char.IsWhiteSpace(letter))
            {
                builder.Append(letter);
                continue;
            }

            float progress = i / lastIndex;
            Color edge = Color.Lerp(light, dark, progress);
            Color color = Color.Lerp(edge, bossPurple, MathF.Sin(MathF.PI * progress));
            builder.Append($"[c/{color.R:X2}{color.G:X2}{color.B:X2}:{letter}]");
        }

        return builder.ToString();
    }

    private static string ArenaAnnouncement(int bossType) => bossType switch
    {
        <= NPCID.None => "The Sandbox arena opens... build your loadout and have at it!",
        NPCID.KingSlime => "The ground squelches... the King Slime Arena is awakening!",
        NPCID.EyeofCthulhu => "You feel an evil presence watching the arena...",
        NPCID.EaterofWorldsHead => "A horrible chill crawls through the corrupted arena...",
        NPCID.BrainofCthulhu => "Screams echo across the crimson arena...",
        NPCID.QueenBee => "The hive stirs... the Queen Bee Arena is buzzing to life!",
        NPCID.SkeletronHead => "The cursed bones of the dungeon rattle around the arena...",
        NPCID.Deerclops => "A howling blizzard sweeps over the Deerclops Arena...",
        NPCID.WallofFlesh => "The underworld trembles... the Wall of Flesh Arena is rising!",
        NPCID.QueenSlimeBoss => "The hallowed gel crystallizes... the Queen Slime Arena shimmers awake!",
        NPCID.Retinazer or NPCID.Spazmatism => "This is going to be a terrible night in the arena...",
        NPCID.TheDestroyer => "You feel vibrations from deep below the arena...",
        NPCID.SkeletronPrime => "The air is getting colder around the arena...",
        NPCID.Plantera => "The jungle grows restless... the Plantera Arena blooms!",
        NPCID.Golem => "The altar glows... the Golem Arena rumbles to life!",
        NPCID.DukeFishron => "The tide churns... Duke Fishron circles the arena!",
        NPCID.HallowBoss => "A shimmering radiance descends upon the arena...",
        NPCID.CultistBoss => "Fanatics gather at the edge of the arena...",
        NPCID.MoonLordCore => "Impending doom approaches the arena...",
        _ => $"The {Lang.GetNPCNameValue(bossType)} Arena is awakening..."
    };

    private void TickClientTimer()
    {
        if (!timerPaused && IsTimedPhase(currentPhase) && remainingTicks > 0)
            remainingTicks--;
    }

    private static bool IsTimedPhase(RoundPhase phase) =>
        phase is RoundPhase.VotingOrEndScreen or RoundPhase.FreezeCountdown or RoundPhase.Playing;

    private static int SecondsToTicks(int seconds) => Math.Max(0, seconds) * TicksPerSecond;

    private static void UpdateFreezeTime(bool frozen)
    {
        CreativePowers.FreezeTime freezeTime = CreativePowerManager.Instance.GetPower<CreativePowers.FreezeTime>();
        freezeTime.SetPowerInfo(frozen);

        if (Main.netMode == NetmodeID.Server)
        {
            NetPacket packet = NetCreativePowersModule.PreparePacket(freezeTime.PowerId, 1);
            packet.Writer.Write(freezeTime.Enabled);
            NetManager.Instance.Broadcast(packet);
        }
    }

    private static void SyncState()
    {
        if (Main.netMode == NetmodeID.Server)
            NetMessage.SendData(MessageID.WorldData);
    }

    public override void OnWorldLoad()
    {
        currentPhase = RoundPhase.WaitingForPlayers;
        remainingTicks = 0;
        timerPaused = false;
        idleHeld = false;
        selectedPresetIndex = -1;
        currentLayout = null;
        pendingWinningTeam = Team.None;
        pendingWinningPlayer = -1;

        if (Main.netMode != NetmodeID.MultiplayerClient)
            UpdateFreezeTime(true);
    }

    public override void ClearWorld()
    {
        ModContent.GetInstance<BossManager>().Cleanup();
        currentPhase = RoundPhase.WaitingForPlayers;
        remainingTicks = 0;
        timerPaused = false;
        idleHeld = false;
        selectedPresetIndex = -1;
        currentLayout = null;
    }

    public override void NetSend(BinaryWriter writer)
    {
        writer.Write((byte)currentPhase);
        writer.Write(remainingTicks);
        writer.Write(timerPaused);
        writer.Write(idleHeld);
        writer.Write(selectedPresetIndex);
        writer.Write(currentLayout != null);
        currentLayout?.Write(writer);
    }

    public override void NetReceive(BinaryReader reader)
    {
        RoundPhase oldPhase = currentPhase;
        bool wasIdleHeld = idleHeld;
        currentPhase = (RoundPhase)reader.ReadByte();
        remainingTicks = Math.Max(0, reader.ReadInt32());
        timerPaused = reader.ReadBoolean();
        idleHeld = reader.ReadBoolean();
        selectedPresetIndex = reader.ReadInt32();
        currentLayout = reader.ReadBoolean() ? ArenaLayout.Read(reader) : null;
        AnnouncePhaseChange(oldPhase, currentPhase, wasIdleHeld, idleHeld, SelectedBossType);
    }
}
