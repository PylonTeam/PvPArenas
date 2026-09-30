using PvPArenas.Common.Game;
using PvPArenas.Common.Generation;
using ErkySSC.Common.RegionProtection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Terraria.DataStructures;
using Terraria.ID;

namespace PvPArenas.Common.AdminTools.WorldGenManager;

/// <summary>One server-owned native job, generated off-world and committed on the main thread.</summary>
internal sealed class WorldGenPassRunner : ModSystem
{
    private string[] passNames = [];
    private bool busy, available;
    private string status = "Ready";
    private double progress;
    private int seed, revision, epoch;
    private NativeWorldGenSession session;
    private Task worker;
    private long nextStatusTick;

    internal IReadOnlyList<string> PassNames => passNames;
    internal bool Busy => busy;
    internal bool Available => available;
    internal string Status => status;
    internal double Progress => Volatile.Read(ref progress);
    internal int Seed => seed;

    public override void Load() => On_Main.ShouldUpdateEntities += ShouldUpdateEntities;
    public override void Unload()
    {
        Shutdown();
        On_Main.ShouldUpdateEntities -= ShouldUpdateEntities;
    }
    private bool ShouldUpdateEntities(On_Main.orig_ShouldUpdateEntities orig, Main self) =>
        !busy && orig(self);

    public override void OnWorldLoad()
    {
        passNames = [];
        busy = available = false;
        progress = 0;
        seed = revision = 0;
        status = Main.netMode == NetmodeID.MultiplayerClient ? "Connecting…"
            : Main.netMode == NetmodeID.Server ? "Ready" : "Generation must run on the server.";
        if (Main.netMode != NetmodeID.Server)
            return;
        try
        {
            if (!NativeLibraryLoader.TryLoad(Mod, out string error))
                throw new InvalidOperationException(error);
            using NativeWorldGenSession catalog = CreateSession(0);
            passNames = catalog.PassNames.ToArray();
            available = passNames.Length > 0;
            if (!available)
                status = "The native library has no generation passes.";
        }
        catch (Exception exception)
        {
            status = exception.Message;
            Log.Warn(status);
        }
    }

    internal static string[] OrderPasses(IReadOnlyList<string> catalog, IReadOnlyList<string> selected)
    {
        if (selected == null || selected.Count == 0)
            throw new ArgumentException("Select at least one pass.");
        HashSet<string> requested = new(selected, StringComparer.OrdinalIgnoreCase);
        if (requested.Any(name => !catalog.Contains(name, StringComparer.OrdinalIgnoreCase)))
            throw new ArgumentException("The selection contains an unknown native pass.");
        return catalog.Where(requested.Contains).ToArray();
    }

    internal bool TryRun(IReadOnlyList<string> selected, out string error)
    {
        error = "";
        bool preparingArena = ModContent.GetInstance<RoundManager>()?.CurrentPhase == RoundManager.RoundPhase.Generating;
        if (Main.netMode != NetmodeID.Server || busy || preparingArena || !available)
        {
            error = Main.netMode != NetmodeID.Server ? "Generation must run on the server."
                : busy || preparingArena ? "Generation is already running." : status;
            return false;
        }
        string[] ordered;
        try { ordered = OrderPasses(passNames, selected); }
        catch (ArgumentException exception) { error = exception.Message; return false; }
        try
        {
            bool resetWorld = ordered.Contains("Reset", StringComparer.OrdinalIgnoreCase);
            bool initialize = session == null || resetWorld;
            if (initialize)
            {
                session?.Dispose();
                session = null;
                seed = Random.Shared.Next(1, int.MaxValue);
                session = CreateSession(seed, ReportProgress);
            }
            session.BindTileArrays();
            // Leave the match idle until the operator starts it on the new terrain.
            ModContent.GetInstance<RoundManager>().ExecuteAdminAction(RoundManager.AdminAction.SetIdle, -1);
            busy = true;
            status = "Preparing…";
            progress = 0;
            int jobEpoch = ++epoch;
            nextStatusTick = 0;
            WorldGenManagerNetHandler.SendStatus(this, includePasses: true);
            worker = Task.Run(() => Generate(ordered, jobEpoch, initialize, resetWorld));
            return true;
        }
        catch (Exception exception)
        {
            session?.Dispose();
            session = null;
            busy = false;
            status = error = exception.Message;
            Log.Error(exception);
            return false;
        }
    }

    private static NativeWorldGenSession CreateSession(int runSeed, WgProgressCallback callback = null) =>
        new(runSeed, Main.maxTilesX switch { 4200 => 0, 6400 => 1, 8400 => 2, _ => 3 },
            Main.maxTilesX, Main.maxTilesY, WorldGen.crimson ? 2 : 1, Main.GameMode, callback);

    private void Generate(string[] ordered, int jobEpoch, bool initialize, bool resetWorld)
    {
        Exception failure = null;
        Stopwatch timer = Stopwatch.StartNew();
        try
        {
            if (initialize)
            {
                session.Initialize();
                // Bootstrap a loaded world once; later jobs retain native prerequisite state.
                if (!resetWorld)
                    Check(session.RunPass("Reset"));
            }
            session.SyncFromTml();
            session.CopyWorldFieldsToNative();
            Check(session.RunPasses(ordered));
            session.SyncToTml(); // Writes staging arrays; Terraria is updated only in Finish.
        }
        catch (Exception exception) { failure = exception; }
        Main.QueueMainThreadAction(() =>
        {
            if (jobEpoch == epoch)
                Finish(failure, resetWorld, timer.Elapsed);
        });
    }

    private void ReportProgress(IntPtr userData, int taskId, float value, int pass, IntPtr message)
    {
        status = message == IntPtr.Zero ? "Generating…" : Marshal.PtrToStringUTF8(message) ?? "Generating…";
        Volatile.Write(ref progress, Math.Clamp((double)value, 0, 1));
        long now = Environment.TickCount64;
        if (now < nextStatusTick)
            return;
        nextStatusTick = now + 250;
        int jobEpoch = epoch;
        // World/entity hooks are paused, so dispatch progress through the main-thread queue.
        Main.QueueMainThreadAction(() =>
        {
            if (jobEpoch == epoch && busy)
                WorldGenManagerNetHandler.SendStatus(this);
        });
    }

    private void Finish(Exception failure, bool resetWorld, TimeSpan elapsed)
    {
        bool committed = false;
        try
        {
            if (failure != null)
                throw failure;
            session.CommitTiles();
            committed = true;
            session.ApplyWorldFields();
            if (resetWorld)
            {
                Array.Clear(Main.sign);
                TileEntity.ByID.Clear();
                TileEntity.ByPosition.Clear();
            }
            session.ImportChests(resetWorld);
            WorldGen.RangeFrame(1, 1, Main.maxTilesX - 2, Main.maxTilesY - 2);
            Liquid.ReInit();
        }
        catch (Exception exception) { failure = exception; }
        // Even a failed metadata/framing step can follow a successful tile commit.
        // Invalidate clients in that case too; this disposable world has no rollback.
        if (committed)
        {
            ModContent.GetInstance<ArenaPreparation>()?.MarkWorldChanged();
            revision++;
            try
            {
                if (Main.netMode == NetmodeID.Server)
                {
                    Netplay.ResetSections();
                    NetMessage.SendData(MessageID.WorldData);
                    ArenaTileSync.Send(new Rectangle(0, 0, Main.maxTilesX, Main.maxTilesY));
                }
                RefreshRendering();
                RelocatePlayers();
            }
            catch (Exception exception) { failure ??= exception; }
        }
        try
        {
            if (failure != null)
            {
                status = "Generation failed: " + failure.Message;
                Log.Error(failure);
                session?.Dispose();
                session = null;
            }
            else
            {
                status = $"Done · {elapsed.TotalSeconds:0.0}s · Seed {seed}";
                progress = 1;
            }
        }
        finally
        {
            worker = null;
            busy = false;
            WorldGenManagerNetHandler.SendStatus(this, includePasses: true);
        }
    }

    private static void RelocatePlayers()
    {
        // Find open ground near world spawn without deleting its floor or carving spawn boxes.
        int preferredX = Math.Clamp(Main.spawnTileX, 20, Main.maxTilesX - 21);
        Point? spawn = null;
        for (int distance = 0; distance < Main.maxTilesX * 2 && spawn == null; distance++)
        {
            int x = preferredX + (distance % 2 == 0 ? distance / 2 : -(distance + 1) / 2);
            if (x < 20 || x >= Main.maxTilesX - 20)
                continue;
            for (int y = 20; y < Main.maxTilesY - 20; y++)
            {
                if (ArenaGeneration.IsSafeSpawn(x, y)) { spawn = new Point(x, y); break; }
            }
        }
        if (spawn is not { } tile)
            return; // Reset alone can intentionally leave an empty world.
        Main.spawnTileX = tile.X;
        Main.spawnTileY = tile.Y;
        if (Main.netMode == NetmodeID.Server)
            NetMessage.SendData(MessageID.WorldData);
        foreach (Player player in Main.player)
        {
            if (player?.active != true)
                continue;
            Vector2 destination = new(tile.X * 16f + 8f - player.width / 2f, tile.Y * 16f - player.height);
            if (Main.netMode == NetmodeID.Server)
                RemoteClient.CheckSection(player.whoAmI, destination, 1);
            player.Teleport(destination, TeleportationStyleID.RodOfDiscord);
            player.velocity = Vector2.Zero;
            if (Main.netMode == NetmodeID.Server)
                RegionTeleportSystem.Synchronize(player, TeleportationStyleID.RodOfDiscord);
        }
    }

    private static void RefreshRendering()
    {
        if (Main.dedServ || Main.netMode == NetmodeID.Server)
            return;
        Main.instance.ClearCachedTileDraws();
        Lighting.Clear();
        Main.Map?.Clear();
        Main.clearMap = true;
        Main.instance.waterfallManager?.FindWaterfalls(true);
    }

    private static void Check(WgResult result)
    {
        if (result != WgResult.Ok)
            throw new InvalidOperationException(result.ToString());
    }

    public override void OnWorldUnload() => Shutdown();

    internal void Shutdown()
    {
        epoch++; // Queued completion must not touch a subsequently loaded world.
        if (worker is { IsCompleted: false })
            session?.RequestCancel();
        worker?.GetAwaiter().GetResult();
        session?.Dispose();
        session = null;
        worker = null;
        busy = false;
    }

    internal void SetAwaitingServer() { busy = true; status = "Starting…"; progress = 0; }

    internal void WriteStatus(BinaryWriter writer, bool includePasses)
    {
        writer.Write(busy);
        writer.Write(available);
        writer.Write(status);
        writer.Write(Progress);
        writer.Write(seed);
        writer.Write(revision);
        writer.Write(includePasses);
        if (!includePasses)
            return;
        writer.Write((ushort)passNames.Length);
        foreach (string name in passNames)
            writer.Write(name);
    }

    internal void ReadStatus(BinaryReader reader)
    {
        busy = reader.ReadBoolean();
        available = reader.ReadBoolean();
        status = reader.ReadString();
        progress = Math.Clamp(reader.ReadDouble(), 0, 1);
        seed = reader.ReadInt32();
        int incomingRevision = reader.ReadInt32();
        if (reader.ReadBoolean())
        {
            passNames = new string[reader.ReadUInt16()];
            for (int i = 0; i < passNames.Length; i++)
                passNames[i] = reader.ReadString();
        }
        if (!busy && incomingRevision != revision)
            RefreshRendering();
        revision = incomingRevision;
    }
}
