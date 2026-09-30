using PvPArenas.Common.AdminTools.WorldGenManager;
using PvPArenas.Common.Game;
using PvPArenas.Common.Game.LoadoutSelector;
using System;
using System.Collections.Generic;
using Terraria.ID;

namespace PvPArenas.Common.Generation;

/// <summary>Runs one bounded terrain step per server tick while the round holds players in the lobby.</summary>
internal sealed class ArenaPreparation : ModSystem
{
    private IEnumerator<Rectangle> steps;
    private Rectangle pendingSync;
    private Rectangle protectedLobby;
    private int stepsSinceSync;
    private bool authoredWorldChanged;
    private bool restoringAuthored;

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
        ClearActors();
        ArenaTemplate.ClearEntitiesOutside(lobby);
        steps = (jungle ? MirroredJungleGenerator.Generate(Random.Shared.Next(), lobby)
            : ArenaTemplate.Restore(lobby)).GetEnumerator();
        restoringAuthored = !jungle;
        authoredWorldChanged = true;
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
                FrameOutsideLobby(area);
                pendingSync = pendingSync.IsEmpty ? area : Rectangle.Union(pendingSync, area);
                if (++stepsSinceSync >= 8)
                    FlushTiles();
                return false;
            }
            if (steps != null)
            {
                steps.Dispose();
                steps = null;
                authoredWorldChanged = !restoringAuthored;
            }
            FlushTiles();
            NetMessage.SendData(MessageID.WorldData);
            return true;
        }
        catch (Exception exception)
        {
            authoredWorldChanged = true;
            failure = exception.Message;
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
        // Framing may update adjacent tiles; keep its neighborhood outside the occupied lobby too.
        for (int x = area.Left; x < area.Right; x++)
        for (int y = area.Top; y < area.Bottom; y++)
            if (!framingExclusion.Contains(x, y))
            {
                WorldGen.TileFrame(x, y, noBreak: true);
                Framing.WallFrame(x, y);
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
        if (steps != null)
        {
            // A cancelled restore also leaves a mixed world that must be restored before the next fight.
            authoredWorldChanged = true;
            steps.Dispose();
            steps = null;
        }
        FlushTiles();
    }

    private void FlushTiles()
    {
        if (!pendingSync.IsEmpty)
            ArenaTileSync.Send(pendingSync);
        pendingSync = Rectangle.Empty;
        stepsSinceSync = 0;
    }

    public override void OnWorldLoad() { Cancel(); authoredWorldChanged = false; }
    public override void OnWorldUnload() => Cancel();
}
