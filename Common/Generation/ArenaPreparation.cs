using PvPArenas.Common.AdminTools.WorldGenManager;
using PvPArenas.Common.Game;
using PvPArenas.Common.Game.LoadoutSelector;
using System;
using System.Collections.Generic;
using Terraria.Chat;
using Terraria.ID;
using Terraria.Localization;

namespace PvPArenas.Common.Generation;

/// <summary>Build the complete server world, verify it on clients, then release the round.</summary>
internal sealed class ArenaPreparation : ModSystem
{
    private IEnumerator<Rectangle> steps;
    private BossFightPreset preparingPreset;
    private bool restoringAuthored, syncing;
    private string recoveryFailure = "";
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
        // Keep the verified world stable until all waiting clients acknowledge it.
        if (ModContent.GetInstance<RoundManager>().CurrentPhase != RoundManager.RoundPhase.Generating)
            orig();
    }

    internal bool TryBegin(BossFightPreset preset, out string failure)
    {
        failure = Main.netMode != NetmodeID.Server ? "Arena preparation must run on the server."
            : !ArenaWorldSystem.IsCompactWorld ? "Reopen the server to load Arenas_v10."
            : !ArenaTemplate.Available ? "The authored arena template is unavailable. Reopen the server."
            : ArenaWorldSystem.MissingArena(preset.ArenaKind);
        if (failure.Length > 0) return false;

        Cancel();
        // Staging is a frozen player position, never an exclusion in the new terrain.
        StagingSpawn = new Point(Math.Clamp(Main.spawnTileX, 10, Main.maxTilesX - 11),
            Math.Clamp(Main.spawnTileY, 10, Main.maxTilesY - 11));
        ModContent.GetInstance<WorldGenPassRunner>()?.Shutdown();
        restoringAuthored = preset.ArenaKind != ArenaKind.UndergroundJungle;
        preparingPreset = preset;
        recoveryFailure = "";
        int seed = restoringAuthored ? 0 : Random.Shared.Next();
        StartLog(restoringAuthored ? "Restore Arenas_v10" : "Plantera arena", seed);
        ClearActors();
        ArenaTemplate.ClearEntitiesOutside(Rectangle.Empty);
        // Every transition replaces every tile, even repeated rounds of the same preset.
        steps = (restoringAuthored ? ArenaTemplate.Restore(Rectangle.Empty)
            : MirroredJungleGenerator.GenerateWithProgress(seed, Rectangle.Empty, progressLog)).GetEnumerator();
        return true;
    }

    internal bool Advance(out string failure)
    {
        failure = "";
        if (Main.netMode != NetmodeID.Server) return false;
        try
        {
            if (syncing)
            {
                if (!ArenaWorldSync.Advance(out string syncFailure)) return false;
                if (syncFailure.Length > 0) throw new InvalidOperationException(syncFailure);
                syncing = false;
                progressLog?.Report("Clients verified the complete arena", 1);
                progressLog?.Complete();
                progressLog = null;
                failure = recoveryFailure;
                recoveryFailure = "";
                preparingPreset = null;
                generationEpoch++;
                return true;
            }

            if (steps?.MoveNext() == true)
            {
                if (restoringAuthored)
                    progressLog?.Report("Restoring every authored tile", steps.Current.Right / (double)Main.maxTilesX);
                // No partial snapshots or framing against the previous arena are published.
                return false;
            }
            steps?.Dispose();
            steps = null;
            if (restoringAuthored) ArenaTemplate.VerifyRestore(Rectangle.Empty);
            if (recoveryFailure.Length == 0
                && !ArenaGeneration.TryResolve(preparingPreset, out _, out string layoutFailure))
                throw new InvalidOperationException(layoutFailure);
            progressLog?.Report("Synchronizing and verifying the complete arena", 0);
            ArenaWorldSync.Begin();
            syncing = true;
            return false;
        }
        catch (Exception exception)
        {
            Log.Error(exception);
            progressLog?.Fail(exception);
            progressLog = null;
            ArenaWorldSync.Cancel();
            syncing = false;
            steps?.Dispose();
            steps = null;
            if (recoveryFailure.Length == 0 && ArenaTemplate.Available)
            {
                // A failed job never leaves a half-built arena as a playable world.
                recoveryFailure = exception.Message;
                restoringAuthored = true;
                StartLog("Recover Arenas_v10", 0);
                ClearActors();
                steps = ArenaTemplate.Restore(Rectangle.Empty).GetEnumerator();
                return false;
            }
            failure = $"{recoveryFailure} Recovery failed: {exception.Message}";
            recoveryFailure = "";
            return true;
        }
    }

    private void StartLog(string purpose, int seed)
    {
        int epoch = generationEpoch;
        progressLog = new(purpose, seed, Main.maxTilesX, Main.maxTilesY,
            (message, stalled) => Main.QueueMainThreadAction(() =>
            {
                if (Main.netMode == NetmodeID.Server && epoch == generationEpoch && progressLog != null)
                    ChatHelper.BroadcastChatMessage(NetworkText.FromLiteral(message), stalled ? Color.OrangeRed : Color.LightGreen);
            }));
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
                Main.projectile[i].active = false;
                NetMessage.SendData(MessageID.KillProjectile, number: Main.projectile[i].identity,
                    number2: Main.projectile[i].owner);
            }
    }

    internal void Cancel()
    {
        generationEpoch++;
        bool interrupted = steps != null || syncing;
        steps?.Dispose();
        steps = null;
        preparingPreset = null;
        syncing = false;
        recoveryFailure = "";
        progressLog?.Dispose();
        progressLog = null;
        ArenaWorldSync.Cancel();
        if (interrupted && Main.netMode == NetmodeID.Server && ArenaWorldSystem.IsCompactWorld && ArenaTemplate.Available)
        {
            foreach (Rectangle _ in ArenaTemplate.Restore(Rectangle.Empty)) { }
            ArenaTemplate.VerifyRestore(Rectangle.Empty);
        }
    }

    public override void OnWorldLoad() => Cancel();
    public override void OnWorldUnload() => Cancel();
}
