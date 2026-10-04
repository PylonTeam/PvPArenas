using PvPArenas.Common.Game.BossVoting;
using PvPArenas.Common.Game.LoadoutSelector;
using PvPArenas.Common.Game.Score;
using PvPArenas.Common.Game.TeamBalancing;
using PvPArenas.Common.Generation;
using PvPArenas.Common.AdminTools.WorldGenManager;
using PvPArenas.Core.Configs;
using PvPFramework.Common.EndScreen;
using PvPFramework.Common.Game;
using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using Terraria.Chat;
using Terraria.Enums;
using Terraria.GameContent.Creative;
using Terraria.GameContent.NetModules;
using Terraria.ID;
using Terraria.Localization;
using Terraria.Net;

namespace PvPArenas.Common.Game;

/// <summary>Server-authoritative Arenas round loop.</summary>
internal sealed class RoundManager : GameEvent
{
    private const int TicksPerSecond = 60;

    internal enum RoundPhase : byte
    {
        WaitingForPlayers,
        VotingOrEndScreen,
        Generating,
        FreezeCountdown,
        Playing,
        Inactive = byte.MaxValue
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
        NoPlayers,
        AdminEnded
    }

    private RoundPhase currentPhase = RoundPhase.WaitingForPlayers;
    private bool idleHeld;
    private bool showingResults;
    private string preparationFailure = "";
    private int selectedPresetIndex = -1;
    private ArenaLayout currentLayout;
    private Point stagingSpawn;

    /// <summary>UTC time the current fight (Playing phase) began; used for match reporting.</summary>
    internal System.DateTime RoundStartUtc { get; private set; } = System.DateTime.UtcNow;
    private Team pendingWinningTeam;
    private int pendingWinningPlayer = -1;

    public override string Id => "arenas";
    public override string DisplayName => "Arenas";
    public override int Priority => 50;
    public override bool UsesStartSettings => false;
    public override bool UsesDefaultActions => false;
    public override string StartingRegionKey => ArenaSpawnBoxIntegration.RegionKey;
    public override bool ManagesStartingRegion => true;
    private bool ChooseTeam => CurrentPhase == RoundPhase.VotingOrEndScreen
        && (Team)Main.LocalPlayer.team is not (Team.Red or Team.Blue);
    public override int TimerSidePadding => ChooseTeam ? 56 : 16;
    public override string TimerTooltip => string.IsNullOrEmpty(PreparationFailure)
        ? "Arenas round status" : Language.GetTextValue("Mods.PvPArenas.Round.PreparationFailed", PreparationFailure);
    public override void DrawTimerDecoration(Rectangle panel, float opacity) => ScorelineUISystem.DrawTimerDecoration(this, panel, opacity);
    public override string TimerText => !IsActive ? null : ChooseTeam ? "Choose your team" : CurrentPhase switch
    {
        RoundPhase.WaitingForPlayers when !string.IsNullOrEmpty(PreparationFailure)
            => Language.GetTextValue("Mods.PvPArenas.Round.PreparationFailedStatus"),
        RoundPhase.WaitingForPlayers => IsIdleHeld ? "Waiting" : "Waiting for players",
        RoundPhase.VotingOrEndScreen when IsShowingResults => $"Results {GameTimerUISystem.FormatTime(RemainingTicks)}",
        RoundPhase.VotingOrEndScreen when IsVoting => $"Boss vote {GameTimerUISystem.FormatTime(RemainingTicks)}",
        RoundPhase.VotingOrEndScreen => $"Next round {GameTimerUISystem.FormatTime(RemainingTicks)}",
        RoundPhase.Generating => SelectedBossType == NPCID.Plantera ? "Generating jungle" : "Preparing arena",
        RoundPhase.FreezeCountdown => $"Starting {Math.Max(1, (int)Math.Ceiling(RemainingTicks / 60d))}",
        RoundPhase.Playing => GameTimerUISystem.FormatTime(RemainingTicks),
        _ => "Arenas"
    };
    public override bool CanAdvanceClock => ModContent.GetInstance<WorldGenPassRunner>()?.Busy != true;
    internal bool IsActive => GameSession.Instance.IsSelected(this) && ArenaWorldSystem.IsCompactWorld;
    internal RoundPhase CurrentPhase => IsActive ? currentPhase : RoundPhase.Inactive;
    internal int RemainingTicks => IsActive ? Math.Max(0, GameSession.Instance.RemainingTicks) : 0;
    internal bool IsTimerPaused => IsActive && GameSession.Instance.ClockPaused;

    public override IReadOnlyList<GameEventAction> Actions => new GameEventAction[]
    {
        new("start_round", currentPhase == RoundPhase.Playing ? "End round" : "Start round",
            () => ExecuteAdminAction(currentPhase == RoundPhase.Playing ? AdminAction.EndRound : AdminAction.StartRound, -1),
            () => IsActive && currentPhase != RoundPhase.Generating),
        new("voting", currentPhase == RoundPhase.VotingOrEndScreen ? "End voting" : "Start voting",
            () => ExecuteAdminAction(currentPhase == RoundPhase.VotingOrEndScreen ? AdminAction.EndVoting : AdminAction.StartVoting, -1),
            () => IsActive && currentPhase != RoundPhase.Generating),
        new("waiting", "Set waiting", () => ExecuteAdminAction(AdminAction.SetIdle, -1),
            () => IsActive && currentPhase != RoundPhase.Generating && !(currentPhase == RoundPhase.WaitingForPlayers && idleHeld)),
        new("balance_teams", "Auto balance teams", () => ExecuteAdminAction(AdminAction.AutoBalanceTeams, -1),
            () => IsActive && currentPhase is RoundPhase.WaitingForPlayers or RoundPhase.VotingOrEndScreen)
    };

    public override void Start(int durationTicks, int countdownSeconds) => ExecuteAdminAction(AdminAction.StartRound, -1);
    public override void End() => ExecuteAdminAction(
        currentPhase == RoundPhase.Playing ? AdminAction.EndRound : AdminAction.SetIdle, -1);

    public override void OnSelected()
    {
        ResetRoundState();
        if (!ArenaWorldSystem.IsCompactWorld)
        {
            idleHeld = true;
            preparationFailure = "The authored arena template is unavailable. Arenas requires its arena world; restart with Arenas as the default event.";
        }
        SetPhase(RoundPhase.WaitingForPlayers, 0);
    }

    public override void OnDeselected()
    {
        ModContent.GetInstance<ArenaPreparation>()?.Cancel();
        ModContent.GetInstance<BossManager>().Cleanup();
        if (IsActive)
        {
            ArenaPlayer.ReleaseAll();
            EndScreenService.Hide();
            UpdateFreezeTime(false);
        }
        ModContent.GetInstance<BossVoteSystem>().Reset();
        ResetRoundState();
        ArenaSpawnBoxIntegration.HideLobby();
    }
    public override void OnAborted()
    {
        OnDeselected();
        idleHeld = true;
        preparationFailure = "The event stopped after an unexpected error. Use Start round to retry.";
        ArenaSpawnBoxIntegration.UpdateMatchState();
        if (IsActive)
            UpdateFreezeTime(true);
    }

    internal bool IsIdleHeld => idleHeld;
    internal string PreparationFailure => preparationFailure;
    internal bool IsShowingResults => IsActive && currentPhase == RoundPhase.VotingOrEndScreen && showingResults;
    internal bool IsVoting => IsActive && currentPhase == RoundPhase.VotingOrEndScreen && !showingResults
        && ModContent.GetInstance<BossVoteSystem>().Active;
    internal int SelectedPresetIndex => selectedPresetIndex;
    internal ArenaLayout CurrentLayout => currentLayout;
    internal Point StagingSpawn => stagingSpawn;

    internal int SelectedBossType => TryGetSelectedPreset(out BossFightPreset preset)
        ? preset.Boss.Type
        : NPCID.None;

    public override void Tick()
    {
        if (ModContent.GetInstance<WorldGenPassRunner>()?.Busy == true)
            return;

        if (!IsActive || Main.netMode != NetmodeID.Server)
            return;

        if (!Main.player.Any(player => player?.active == true))
        {
            if (currentPhase != RoundPhase.WaitingForPlayers)
                FinishRound(RoundEndReason.NoPlayers);
            return;
        }

        TeamBalancer.AssignUnassignedPlayers();

        if (currentPhase == RoundPhase.Generating)
        {
            AdvancePreparation();
            return;
        }

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
    }

    public override void OnClockExpired()
    {
        if (!IsActive || Main.netMode != NetmodeID.Server || !CanAdvanceClock)
            return;

        switch (currentPhase)
        {
            case RoundPhase.VotingOrEndScreen:
                if (showingResults)
                    BeginVoting();
                else if (ModContent.GetInstance<BossVoteSystem>().Active)
                    FinishVoting();
                else
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
        preset = ModContent.GetInstance<ServerConfig>().GetFightPreset(selectedPresetIndex);
        return preset != null;
    }

    internal void NotifyBossDefeated(Player player, Team team)
    {
        if (!IsActive || Main.netMode == NetmodeID.MultiplayerClient || currentPhase != RoundPhase.Playing
            || team is not (Team.Red or Team.Blue) || pendingWinningTeam != Team.None)
            return;

        pendingWinningTeam = team;
        pendingWinningPlayer = player?.whoAmI ?? -1;
        Log.Info($"[M2-BossVictory] team={team}, player={pendingWinningPlayer}.");
    }

    internal void SetRemainingSeconds(int seconds)
    {
        if (!IsActive || Main.netMode == NetmodeID.MultiplayerClient || !IsTimedPhase(currentPhase))
            return;

        GameSession.Instance.SetClock(this, SecondsToTicks(seconds));
    }

    internal void ToggleTimerPaused()
    {
        if (!IsActive || Main.netMode == NetmodeID.MultiplayerClient || !IsTimedPhase(currentPhase))
            return;

        GameSession.Instance.SetClockPaused(this, !IsTimerPaused);
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
        if (!IsActive || Main.netMode == NetmodeID.MultiplayerClient
            || ModContent.GetInstance<WorldGenPassRunner>()?.Busy == true)
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
                showingResults = false;
                preparationFailure = "";
                ModContent.GetInstance<BossVoteSystem>().Reset();
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

    private void StartIntermission(bool presentResults = false)
    {
        preparationFailure = "";
        currentLayout = null;
        ArenaPlayer.ReleaseAll();
        TeamBalancer.AssignUnassignedPlayers();
        if (!TeamBalancer.AllActivePlayersAssigned())
        {
            SetPhase(RoundPhase.WaitingForPlayers, 0);
            return;
        }

        ModContent.GetInstance<BossVoteSystem>().Reset();
        selectedPresetIndex = 0;
        int resultSeconds = ModContent.GetInstance<ServerConfig>().ResultsDurationSeconds;
        showingResults = presentResults && resultSeconds > 0;
        if (showingResults)
            SetPhase(RoundPhase.VotingOrEndScreen, SecondsToTicks(resultSeconds));
        else
            BeginVoting();
    }

    private void BeginVoting()
    {
        EndScreenService.Hide();
        showingResults = false;
        int ticks = SecondsToTicks(Math.Clamp(ModContent.GetInstance<ServerConfig>().VotingDurationSeconds, 5, 300));
        ModContent.GetInstance<BossVoteSystem>().Start(ticks);
        SetPhase(RoundPhase.VotingOrEndScreen, ticks);
    }

    private void FinishVoting()
    {
        selectedPresetIndex = ModContent.GetInstance<BossVoteSystem>().Complete();
        SetPhase(RoundPhase.VotingOrEndScreen, BossVotePresentation.ResultDurationTicks);
    }

    private void PrepareRound()
    {
        if (Main.netMode != NetmodeID.Server)
            return;
        EndScreenService.Hide();

        preparationFailure = "";
        showingResults = false;
        if (!ArenaWorldSystem.IsCompactWorld || !ArenaTemplate.Available)
        {
            HoldPreparationFailure("The authored arena template is unavailable. Arenas requires its arena world; restart with Arenas as the default event.");
            return;
        }
        int votedPreset = ModContent.GetInstance<BossVoteSystem>().Complete();
        if (votedPreset >= 0)
            selectedPresetIndex = votedPreset;

        if (!TryGetSelectedPreset(out BossFightPreset preset))
        {
            HoldPreparationFailure("No playable boss preset is configured.");
            return;
        }

        if (!Main.player.Any(player => player?.active == true && (Team)player.team is Team.Red or Team.Blue))
        {
            HoldPreparationFailure("No active Red or Blue players were available after automatic assignment.");
            return;
        }

        currentLayout = null;
        try
        {
            ArenaPreparation preparation = ModContent.GetInstance<ArenaPreparation>();
            if (!preparation.TryBegin(preset, out string failure))
            {
                HoldPreparationFailure(failure);
                return;
            }
            ModContent.GetInstance<BossManager>().Cleanup();
            ArenaPlayer.ReleaseAll();
            stagingSpawn = preparation.StagingSpawn;
            SetPhase(RoundPhase.Generating, 0);
            foreach (Player player in Main.player)
                if (player?.active == true)
                    ArenaPlayer.Stage(player, stagingSpawn);
        }
        catch (Exception exception)
        {
            Log.Error(exception);
            HoldPreparationFailure(exception.Message);
        }
    }

    private void AdvancePreparation()
    {
        if (!ModContent.GetInstance<ArenaPreparation>().Advance(out string failure))
            return;
        if (failure.Length > 0)
        {
            HoldPreparationFailure(failure);
            return;
        }
        if (!TryGetSelectedPreset(out BossFightPreset preset)
            || !ArenaGeneration.TryResolve(preset, out ArenaLayout layout, out failure))
        {
            HoldPreparationFailure(failure.Length > 0 ? failure : "The selected boss is unavailable.");
            return;
        }

        currentLayout = layout;
        foreach (Player player in Main.player)
            if (player?.active == true && (Team)player.team is Team.Red or Team.Blue)
                if (!ArenaPlayer.Prepare(player, preset, currentLayout))
                    return;

        int countdownSeconds = Math.Max(0, ModContent.GetInstance<ServerConfig>().FreezeCountdownSeconds);
        if (countdownSeconds == 0)
        {
            StartPlaying();
            return;
        }

        SetPhase(RoundPhase.FreezeCountdown, SecondsToTicks(countdownSeconds));
    }

    internal void ReportSpawnFailure(string failure)
    {
        if (Main.netMode != NetmodeID.Server)
            return;
        ModContent.GetInstance<BossManager>().Cleanup();
        ArenaPlayer.ReleaseAll();
        HoldPreparationFailure(failure);
    }

    private void HoldPreparationFailure(string failure)
    {
        ModContent.GetInstance<ArenaPreparation>()?.Cancel();
        // Preparation recovers the authored world before reporting a failed job.
        // Its old staging position may have been inside the jungle, so return players to world spawn.
        if (ArenaWorldSystem.IsCompactWorld && ArenaTemplate.Available)
            foreach (Player player in Main.player)
                if (player?.active == true)
                    ArenaPlayer.Stage(player, new Point(Main.spawnTileX, Main.spawnTileY));
        // Keep the completed ballot so Start Round retries its winner instead of rolling another boss.
        preparationFailure = failure;
        currentLayout = null;
        showingResults = false;
        idleHeld = true;
        Log.Warn($"[M2-Prepare] {failure} Holding the selected boss until setup is retried.");
        SetPhase(RoundPhase.WaitingForPlayers, 0);

        const string key = "Mods.PvPArenas.Round.PreparationFailed";
        if (Main.netMode == NetmodeID.Server)
            ChatHelper.BroadcastChatMessage(NetworkText.FromKey(key, failure), Color.OrangeRed);
        else if (!Main.dedServ)
            Main.NewText(Language.GetTextValue(key, failure), Color.OrangeRed);
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

        ModContent.GetInstance<ArenaPreparation>()?.Cancel();
        ModContent.GetInstance<BossManager>().Cleanup();
        ArenaPlayer.ReleaseAll();
        pendingWinningTeam = Team.None;
        pendingWinningPlayer = -1;
        Log.Info($"[M2-RoundEnd] reason={reason}, winner={winningTeam}, player={winningPlayer}.");

        if (reason == RoundEndReason.NoPlayers)
        {
            EndScreenService.Hide();
            showingResults = false;
            preparationFailure = "";
            ModContent.GetInstance<BossVoteSystem>().Reset();
            selectedPresetIndex = -1;
            currentLayout = null;
            SetPhase(RoundPhase.WaitingForPlayers, 0);
            return;
        }

        if (reason == RoundEndReason.SpawnFailed)
        {
            HoldPreparationFailure("The selected boss could not be spawned.");
            return;
        }

        bool presentResults = reason is RoundEndReason.BossDefeated or RoundEndReason.TimeExpired
            or RoundEndReason.BossDespawned or RoundEndReason.AdminEnded;
        if (presentResults)
            ArenaEndScreen.Present(winningTeam, winningPlayer);

        StartIntermission(presentResults);
    }

    private void SetPhase(RoundPhase newPhase, int durationTicks)
    {
        RoundPhase oldPhase = currentPhase;
        bool wasIdleHeld = idleHeld;
        currentPhase = newPhase;
        (string id, string label) = Stage(newPhase);
        bool lobby = newPhase is RoundPhase.WaitingForPlayers or RoundPhase.VotingOrEndScreen;
        GameSession.Instance.SetStage(this, id, label, IsTimedPhase(newPhase) ? durationTicks : -1,
            playing: newPhase == RoundPhase.Playing, lobby: lobby, running: newPhase != RoundPhase.WaitingForPlayers);
        ArenaSpawnBoxIntegration.UpdateMatchState();

        int players = Main.player.Count(player => player?.active == true);
        Log.Info($"[M2-Phase] {oldPhase} -> {newPhase}; ticks={RemainingTicks}, preset={selectedPresetIndex}, players={players}.");
        if (IsActive)
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

    private (string Id, string Label) Stage(RoundPhase phase) => phase switch
    {
        RoundPhase.WaitingForPlayers when preparationFailure.Contains("authored arena template is unavailable", StringComparison.Ordinal)
            => ("world_required", "Arena world required"),
        RoundPhase.WaitingForPlayers when preparationFailure.Length > 0 => ("preparation_failed", "Preparation failed"),
        RoundPhase.WaitingForPlayers => ("waiting", idleHeld ? "Waiting" : "Waiting for players"),
        RoundPhase.VotingOrEndScreen when showingResults => ("results", "Results"),
        RoundPhase.VotingOrEndScreen when ModContent.GetInstance<BossVoteSystem>().Active => ("voting", "Boss voting"),
        RoundPhase.VotingOrEndScreen => ("vote_result", "Vote result"),
        RoundPhase.Generating => ("generating", "Preparing arena"),
        RoundPhase.FreezeCountdown => ("countdown", "Starting round"),
        RoundPhase.Playing => ("playing", "Round in progress"),
        _ => ("waiting", "Waiting")
    };

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
        if (!IsActive)
            ResetRoundState();
    }

    private void ResetRoundState()
    {
        currentPhase = RoundPhase.WaitingForPlayers;
        idleHeld = false;
        showingResults = false;
        preparationFailure = "";
        selectedPresetIndex = -1;
        currentLayout = null;
        pendingWinningTeam = Team.None;
        pendingWinningPlayer = -1;
    }

    public override void ClearWorld()
    {
        ModContent.GetInstance<ArenaPreparation>()?.Cancel();
        ModContent.GetInstance<BossManager>().Cleanup();
        currentPhase = RoundPhase.WaitingForPlayers;
        idleHeld = false;
        showingResults = false;
        preparationFailure = "";
        selectedPresetIndex = -1;
        currentLayout = null;
    }

    public override void NetSend(BinaryWriter writer)
    {
        writer.Write((byte)currentPhase);
        // Keep the mode snapshot shape; Framework is authoritative for these clock values.
        writer.Write(RemainingTicks);
        writer.Write(IsTimerPaused);
        writer.Write(idleHeld);
        writer.Write(showingResults);
        writer.Write(preparationFailure);
        writer.Write(selectedPresetIndex);
        writer.Write(stagingSpawn.X);
        writer.Write(stagingSpawn.Y);
        writer.Write(currentLayout != null);
        currentLayout?.Write(writer);
    }

    public override void NetReceive(BinaryReader reader)
    {
        RoundPhase oldPhase = currentPhase;
        bool wasIdleHeld = idleHeld;
        currentPhase = (RoundPhase)reader.ReadByte();
        _ = reader.ReadInt32();
        _ = reader.ReadBoolean();
        idleHeld = reader.ReadBoolean();
        showingResults = reader.ReadBoolean();
        preparationFailure = reader.ReadString();
        selectedPresetIndex = reader.ReadInt32();
        stagingSpawn = new Point(reader.ReadInt32(), reader.ReadInt32());
        currentLayout = reader.ReadBoolean() ? ArenaLayout.Read(reader) : null;
        if (IsActive)
            AnnouncePhaseChange(oldPhase, currentPhase, wasIdleHeld, idleHeld, SelectedBossType);
    }
}
