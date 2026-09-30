using PvPArenas.Common.AdminTools.WorldGenManager;
using PvPArenas.Common.Game;
using PvPArenas.Common.Game.LoadoutSelector;
using System;
using System.Collections.Generic;
using Terraria.Chat;
using Terraria.ID;
using Terraria.Localization;

namespace PvPArenas.Common.Generation;

/// <summary>Runs one bounded terrain step per server tick while the round holds players in the lobby.</summary>
internal sealed class ArenaPreparation : ModSystem
{
    private IEnumerator<Rectangle> steps;
    private Rectangle pendingSync;
    private Rectangle protectedLobby;
    private TileWallWireStateData[] lobbyFramingState;
    private int stepsSinceSync;
    private bool authoredWorldChanged;
    private bool restoringAuthored;
    private WorldGenProgressLog progressLog;
    private int generationEpoch;

    internal Point StagingSpawn { get; private set; }

    public override void Load() => On_WorldGen.UpdateWorld += UpdateWorld;
    public override void Unload()
    {
        Cancel();
        On_WorldGen.UpdateWorld -= UpdateWorld;
    }

    private static void UpdateWorld(On_WorldGen.orig_UpdateWorld orig)
    {
        // Players still update; random terrain updates must not race a partly built arena.
        if (ModContent.GetInstance<RoundManager>().CurrentPhase != RoundManager.RoundPhase.Generating)
            orig();
    }

    internal bool TryBegin(BossFightPreset preset, out string failure)
    {
        failure = "";
        if (Main.netMode != NetmodeID.Server)
            failure = "Arena preparation must run on the server.";
        else if (Main.maxTilesX != ArenaWorldSystem.Width || Main.maxTilesY != ArenaWorldSystem.Height)
            failure = "Reopen the server to load Arenas_v10.";
        else
            failure = ArenaWorldSystem.MissingArena(preset.ArenaKind);
        if (failure.Length > 0)
            return false;

        Rectangle lobby = ArenaSpawnBoxIntegration.LobbyBounds;
        if (!ArenaGeneration.TryFindSpawn(lobby, Main.spawnTileY, out Point spawn))
        {
            failure = "The lobby needs a dry, grounded spawn before terrain can be generated.";
            return false;
        }
        bool jungle = preset.ArenaKind == ArenaKind.UndergroundJungle;
        if ((jungle || authoredWorldChanged) && !ArenaTemplate.Available)
        {
            failure = "The authored arena template is unavailable. Reopen the server.";
            return false;
        }

        Cancel();
        protectedLobby = lobby;
        StagingSpawn = spawn;
        if (!jungle && !authoredWorldChanged)
            return true;

        ModContent.GetInstance<WorldGenPassRunner>()?.Shutdown();
        int seed = jungle ? Random.Shared.Next() : 0, jobEpoch = generationEpoch;
        progressLog = new(jungle ? "Plantera arena" : "Restore Arenas_v10", seed, Main.maxTilesX, Main.maxTilesY,
            (message, stalled) => Main.QueueMainThreadAction(() =>
            {
                if (Main.netMode == NetmodeID.Server && jobEpoch == generationEpoch && progressLog != null)
                    ChatHelper.BroadcastChatMessage(NetworkText.FromLiteral(message), stalled ? Color.OrangeRed : Color.LightGreen);
            }));
        try
        {
            progressLog.Report("Clearing previous arena actors", 0);
            ClearActors();
            ArenaTemplate.ClearEntitiesOutside(lobby);
            steps = (jungle ? MirroredJungleGenerator.GenerateWithProgress(seed, lobby, progressLog)
                : ArenaTemplate.Restore(lobby)).GetEnumerator();
            restoringAuthored = !jungle;
            if (restoringAuthored) progressLog.Report("Restoring authored terrain", 0);
            authoredWorldChanged = true;
        }
        catch (Exception exception)
        {
            progressLog.Fail(exception);
            Cancel();
            throw;
        }
        return true;
    }

    internal bool Advance(out string failure)
    {
        failure = "";
        if (Main.netMode != NetmodeID.Server)
            return false;
        try
        {
            if (steps?.MoveNext() == true)
            {
                Rectangle area = steps.Current;
                // Native passes work in private buffers; players keep updating while the worker runs.
                if (area.IsEmpty)
                    return false;
                if (restoringAuthored) progressLog?.Report("Restoring authored terrain", area.Right / (double)Main.maxTilesX);
                // The authored snapshot already contains valid frames. Reframing it
                // against the next, still-jungle strip can change or destroy objects.
                if (!restoringAuthored)
                {
                    progressLog?.SetOperation("Framing tiles");
                    FrameOutsideLobby(area);
                    progressLog?.SetOperation(null);
                }
                pendingSync = pendingSync.IsEmpty ? area : Rectangle.Union(pendingSync, area);
                if (++stepsSinceSync >= 8)
                    FlushTiles();
                return false;
            }
            if (steps != null)
            {
                steps.Dispose();
                steps = null;
                if (restoringAuthored)
                {
                    progressLog?.SetOperation("Verifying authored tiles");
                    ArenaTemplate.VerifyRestore(protectedLobby);
                    progressLog?.SetOperation(null);
                }
                authoredWorldChanged = !restoringAuthored;
            }
            progressLog?.Report("Finishing tile synchronization", 0);
            FlushTiles();
            progressLog?.SetOperation("Synchronizing world metadata");
            NetMessage.SendData(MessageID.WorldData);
            progressLog?.SetOperation(null);
            progressLog?.Report("Finishing tile synchronization", 1);
            progressLog?.Complete();
            progressLog = null;
            generationEpoch++;
            return true;
        }
        catch (Exception exception)
        {
            authoredWorldChanged = true;
            failure = exception.Message;
            progressLog?.Fail(exception);
            Log.Error(exception);
            Cancel();
            return true;
        }
    }

    internal void MarkWorldChanged() => authoredWorldChanged = true;

    private void FrameOutsideLobby(Rectangle area)
    {
        area.Inflate(1, 1);
        area = Rectangle.Intersect(area, new Rectangle(1, 1, Main.maxTilesX - 3, Main.maxTilesY - 3));
        Rectangle framingExclusion = protectedLobby;
        framingExclusion.Inflate(2, 2);
        // TileFrame recursively frames merging neighbors beyond its requested coordinates.
        // Retain the occupied lobby's live framing state; a wider exclusion alone cannot bound recursion.
        int count = protectedLobby.Width * protectedLobby.Height;
        if (lobbyFramingState?.Length != count) lobbyFramingState = new TileWallWireStateData[count];
        TileWallWireStateData[] states = Main.tile.GetData<TileWallWireStateData>();
        for (int x = 0; x < protectedLobby.Width; x++)
            Array.Copy(states, (protectedLobby.Left + x) * Main.tile.Height + protectedLobby.Top,
                lobbyFramingState, x * protectedLobby.Height, protectedLobby.Height);
        try
        {
            for (int x = area.Left; x < area.Right; x++)
            for (int y = area.Top; y < area.Bottom; y++)
                if (!framingExclusion.Contains(x, y))
                {
                    WorldGen.TileFrame(x, y, noBreak: true);
                    Framing.WallFrame(x, y);
                }
        }
        finally
        {
            for (int x = 0; x < protectedLobby.Width; x++)
                Array.Copy(lobbyFramingState, x * protectedLobby.Height, states,
                    (protectedLobby.Left + x) * Main.tile.Height + protectedLobby.Top, protectedLobby.Height);
        }
    }

    private static void ClearActors()
    {
        for (int i = 0; i < Main.maxNPCs; i++)
            if (Main.npc[i]?.active == true)
            {
                Main.npc[i].active = false;
                NetMessage.SendData(MessageID.SyncNPC, number: i);
            }
        for (int i = 0; i < Main.maxProjectiles; i++)
            if (Main.projectile[i]?.active == true)
            {
                // Do not run Kill callbacks: explosives must not alter the protected spawn.
                Main.projectile[i].active = false;
                NetMessage.SendData(MessageID.KillProjectile, number: Main.projectile[i].identity,
                    number2: Main.projectile[i].owner);
            }
    }

    internal void Cancel()
    {
        generationEpoch++; // Notices already queued by the timer belong only to the cancelled preparation.
        try
        {
            if (steps != null)
            {
                // A cancelled restore also leaves a mixed world that must be restored before the next fight.
                authoredWorldChanged = true;
                steps.Dispose();
                steps = null;
            }
            FlushTiles();
        }
        finally
        {
            progressLog?.Dispose();
            progressLog = null;
        }
    }

    private void FlushTiles()
    {
        if (!pendingSync.IsEmpty)
        {
            progressLog?.SetOperation("Synchronizing tiles to clients");
            ArenaTileSync.Send(pendingSync);
            progressLog?.SetOperation(null);
        }
        pendingSync = Rectangle.Empty;
        stepsSinceSync = 0;
    }

    public override void OnWorldLoad() { Cancel(); authoredWorldChanged = false; }
    public override void OnWorldUnload() { Cancel(); lobbyFramingState = null; }
}
