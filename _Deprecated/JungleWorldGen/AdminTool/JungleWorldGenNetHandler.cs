#if DEBUG
using PvPArenas.Common.AdminTools.WorldGenManager;
using PvPArenas.Common.Game;
using PvPArenas.Common.Generation;
using PvPArenas.Core.Compat;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria.ID;

namespace PvPArenas.Common.AdminTools.JungleWorldGen;

internal enum JungleWorldGenRequest : byte { Status, Apply, Generate, Print }

internal sealed record JungleWorldGenStatus(IReadOnlyList<string> Passes, int? FixedSeed,
    bool Busy, bool CanGenerate, bool CanPrint, float Progress, string ProgressText,
    string Message, bool Success, int? LastSeed, string ExportPath)
{
    internal static JungleWorldGenStatus Empty(string message = "Connecting to the server…", bool success = true) =>
        new(VanillaJungleProfile.Default.Passes, null, false, false, false, 0, "", message, success, null, "");
}

/// <summary>DEBUG panel transport. Recipes and generation remain owned by the authenticated server.</summary>
internal static class JungleWorldGenNetHandler
{
    private const long TimeoutMs = 15000;
    private static uint nextRequest, pendingRequest;
    private static long pendingUntil, statusUntil;
    internal static JungleWorldGenStatus ClientStatus { get; private set; } = JungleWorldGenStatus.Empty();
    internal static bool HasStatus { get; private set; }
    internal static bool Pending => pendingRequest != 0;
    internal static int StatusVersion { get; private set; }

    internal static void RequestStatus()
    {
        if (Main.netMode != NetmodeID.MultiplayerClient) return;
        BeginRequest(JungleWorldGenRequest.Status, 0).Send();
        if (!HasStatus && statusUntil == 0) statusUntil = Environment.TickCount64 + TimeoutMs;
    }

    internal static void Apply(IReadOnlyList<string> passes, int? fixedSeed) =>
        Send(JungleWorldGenRequest.Apply, passes, fixedSeed);
    internal static void Generate(IReadOnlyList<string> passes, int? fixedSeed) =>
        Send(JungleWorldGenRequest.Generate, passes, fixedSeed);
    internal static void Print() => Send(JungleWorldGenRequest.Print, null, null);

    private static void Send(JungleWorldGenRequest request, IReadOnlyList<string> passes, int? fixedSeed)
    {
        if (Pending) return;
        if (Main.netMode != NetmodeID.MultiplayerClient)
        {
            SetClientError("Jungle world generation requires a multiplayer DEBUG server.");
            return;
        }
        try
        {
            // Validate locally before publishing a request or marking it pending.
            if (request is JungleWorldGenRequest.Apply or JungleWorldGenRequest.Generate)
                passes = new VanillaJungleProfile(passes).Passes;
            uint id = ++nextRequest;
            if (id == 0) id = ++nextRequest;
            ModPacket packet = BeginRequest(request, id);
            if (request is JungleWorldGenRequest.Apply or JungleWorldGenRequest.Generate)
                WriteRecipe(packet, passes, fixedSeed);
            packet.Send();
            pendingRequest = id;
            pendingUntil = Environment.TickCount64 + TimeoutMs;
            ClientStatus = ClientStatus with { Message = "Waiting for the server…", Success = true };
            StatusVersion++;
        }
        catch (ArgumentException error) { SetClientError(error.Message); }
    }

    internal static void HandleRequest(BinaryReader reader, int whoAmI)
    {
        if (Main.netMode != NetmodeID.Server) return;
        uint id = 0;
        try
        {
            JungleWorldGenRequest request = (JungleWorldGenRequest)reader.ReadByte();
            id = reader.ReadUInt32();
            // Do not read or execute a supplied recipe until authority has been checked.
            if (!ErkySSCCompat.IsAdmin(whoAmI, out string denied))
            {
                SendStatus(whoAmI, id, JungleWorldGenStatus.Empty(denied, false));
                return;
            }
            if (!Enum.IsDefined(request)) throw new InvalidDataException("Unknown Jungle tool operation.");
            string[] passes = null;
            int? seed = null;
            if (request is JungleWorldGenRequest.Apply or JungleWorldGenRequest.Generate)
                ReadRecipe(reader, out passes, out seed);
            bool success = ExecuteRequest(request, whoAmI, passes, seed, out string message);
            SendStatus(whoAmI, id, CaptureStatus(message, success));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException)
        {
            SendStatus(whoAmI, id, JungleWorldGenStatus.Empty("Invalid Jungle tool request: " + error.Message, false));
        }
    }

    // Shared execution gate also protects callers other than the packet decoder.
    internal static bool ExecuteRequest(JungleWorldGenRequest request, int playerId,
        IReadOnlyList<string> passes, int? fixedSeed, out string message)
    {
        message = "";
        if (Main.netMode != NetmodeID.Server)
        {
            message = "Jungle world generation must run on the server.";
            return false;
        }
        if (!ErkySSCCompat.IsAdmin(playerId, out message)) return false;
        if (!Enum.IsDefined(request))
        {
            message = "Unknown Jungle tool operation.";
            return false;
        }
        if (request == JungleWorldGenRequest.Status) return true;

        RoundManager manager = ModContent.GetInstance<RoundManager>();
        if (manager.CurrentPhase == RoundManager.RoundPhase.Generating
            || ModContent.GetInstance<WorldGenPassRunner>()?.Busy == true)
        {
            message = "Wait for the current generation to finish.";
            return false;
        }
        if (request == JungleWorldGenRequest.Print)
        {
            if (manager.SelectedBossType != NPCID.Plantera || manager.CurrentLayout == null || !JungleMapExport.HasRecipe)
            {
                message = "Generate a complete Plantera arena before printing its map.";
                return false;
            }
            string path = JungleMapExport.TryExportLast();
            message = path == null ? "Export failed. See server.log for details."
                : "Server saved the PNG overview and JSON recipe: " + path;
            return path != null;
        }

        JungleWorldGenSettings settings = ModContent.GetInstance<JungleWorldGenSettings>();
        VanillaJungleProfile previous = JungleWorldGenSettings.CaptureProfile();
        int? previousSeed = settings.FixedSeed;
        if (!settings.TryApply(passes, fixedSeed, out message)) return false;
        if (request == JungleWorldGenRequest.Generate && !manager.TryDebugGenerateJungle(playerId, out message))
        {
            // A rejected run must not replace the server recipe as a side effect.
            settings.TryApply(previous.Passes, previousSeed, out _);
            return false;
        }
        message = request == JungleWorldGenRequest.Generate
            ? "Generating a protected Jungle preview. Players stay staged until the complete arena is verified."
            : "Applied the recipe for future Plantera generations in this world session.";
        Log.Info($"[worldgen] Jungle admin tool {request} by player {playerId}; seed={fixedSeed?.ToString() ?? "random"}; passes={string.Join(", ", JungleWorldGenSettings.CaptureProfile().Passes)}");
        return true;
    }

    private static JungleWorldGenStatus CaptureStatus(string message, bool success)
    {
        RoundManager manager = ModContent.GetInstance<RoundManager>();
        WorldGenPassRunner runner = ModContent.GetInstance<WorldGenPassRunner>();
        bool preparing = manager.CurrentPhase == RoundManager.RoundPhase.Generating;
        bool busy = preparing || runner?.Busy == true;
        (string stage, float progress) = preparing
            ? ModContent.GetInstance<ArenaPreparation>().DebugProgress
            : runner?.Busy == true ? (runner.Status, (float)runner.Progress) : ("Ready", 0);
        if (!busy && string.IsNullOrEmpty(message) && !string.IsNullOrEmpty(manager.PreparationFailure))
        {
            message = manager.PreparationFailure;
            success = false;
        }
        return new(JungleWorldGenSettings.CaptureProfile().Passes,
            ModContent.GetInstance<JungleWorldGenSettings>().FixedSeed, busy, manager.IsActive && !busy,
            !busy && manager.SelectedBossType == NPCID.Plantera && manager.CurrentLayout != null && JungleMapExport.HasRecipe,
            progress, stage, message, success, JungleMapExport.LastSeed, JungleMapExport.LastImagePath);
    }

    internal static void HandleStatus(BinaryReader reader)
    {
        if (Main.netMode != NetmodeID.MultiplayerClient) return;
        try
        {
            uint id = reader.ReadUInt32();
            JungleWorldGenStatus status = ReadStatus(reader);
            // Polls update live state without erasing the most recent action's result.
            if (id == 0 && status.Success && string.IsNullOrEmpty(status.Message) && HasStatus)
                status = ClientStatus.Busy && !status.Busy
                    ? status with { Message = status.CanPrint ? "Jungle preview ready. Players can inspect the arena." : "Generation finished." }
                    : status with { Message = ClientStatus.Message, Success = ClientStatus.Success };
            ClientStatus = status;
            HasStatus = true;
            statusUntil = 0;
            if (id != 0 && id == pendingRequest) pendingRequest = 0;
            StatusVersion++;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException)
        {
            pendingRequest = 0;
            SetClientError("Invalid Jungle tool response: " + error.Message);
        }
    }

    internal static void WriteRecipe(BinaryWriter writer, IReadOnlyList<string> passes, int? fixedSeed)
    {
        IReadOnlyList<string> selected = new VanillaJungleProfile(passes).Passes;
        ulong mask = 0;
        for (int i = 0; i < VanillaJungleProfile.Catalog.Count; i++)
            if (selected.Contains(VanillaJungleProfile.Catalog[i].Name)) mask |= 1UL << i;
        writer.Write(mask);
        writer.Write(fixedSeed.HasValue);
        if (fixedSeed.HasValue) writer.Write(fixedSeed.Value);
    }

    internal static void ReadRecipe(BinaryReader reader, out string[] passes, out int? fixedSeed)
    {
        ulong mask = reader.ReadUInt64();
        if ((mask >> VanillaJungleProfile.Catalog.Count) != 0)
            throw new InvalidDataException("The selection contains an unknown Jungle pass.");
        fixedSeed = reader.ReadBoolean() ? reader.ReadInt32() : null;
        passes = new VanillaJungleProfile(VanillaJungleProfile.Catalog
            .Where((_, i) => (mask & (1UL << i)) != 0).Select(pass => pass.Name)).Passes.ToArray();
    }

    internal static void WriteStatus(BinaryWriter writer, JungleWorldGenStatus status)
    {
        WriteRecipe(writer, status.Passes, status.FixedSeed);
        writer.Write(status.Busy);
        writer.Write(status.CanGenerate);
        writer.Write(status.CanPrint);
        writer.Write(status.Progress);
        writer.Write(status.ProgressText);
        writer.Write(status.Message);
        writer.Write(status.Success);
        writer.Write(status.LastSeed.HasValue);
        if (status.LastSeed.HasValue) writer.Write(status.LastSeed.Value);
        writer.Write(status.ExportPath);
    }

    internal static JungleWorldGenStatus ReadStatus(BinaryReader reader)
    {
        ReadRecipe(reader, out string[] passes, out int? seed);
        bool busy = reader.ReadBoolean(), canGenerate = reader.ReadBoolean(), canPrint = reader.ReadBoolean();
        float progress = reader.ReadSingle();
        if (!float.IsFinite(progress) || progress < 0 || progress > 1)
            throw new InvalidDataException("Invalid generation progress.");
        string stage = ReadText(reader), message = ReadText(reader);
        bool success = reader.ReadBoolean();
        int? lastSeed = reader.ReadBoolean() ? reader.ReadInt32() : null;
        string path = ReadText(reader);
        return new(Array.AsReadOnly(passes), seed, busy, canGenerate, canPrint, progress, stage, message, success, lastSeed, path);
    }

    private static string ReadText(BinaryReader reader)
    {
        string value = reader.ReadString();
        if (value.Length > 4096) throw new InvalidDataException("Jungle tool status text is too long.");
        return value;
    }

    private static ModPacket BeginRequest(JungleWorldGenRequest request, uint id)
    {
        ModPacket packet = ModContent.GetInstance<PvPArenas>().GetPacket();
        packet.Write((byte)PvPArenas.PacketType.JungleWorldGenRequest);
        packet.Write((byte)request);
        packet.Write(id);
        return packet;
    }

    private static void SendStatus(int toClient, uint id, JungleWorldGenStatus status)
    {
        ModPacket packet = ModContent.GetInstance<PvPArenas>().GetPacket();
        packet.Write((byte)PvPArenas.PacketType.JungleWorldGenStatus);
        packet.Write(id);
        WriteStatus(packet, status);
        packet.Send(toClient);
    }

    internal static void TickTimeout()
    {
        if (Pending && Environment.TickCount64 >= pendingUntil
            || !HasStatus && statusUntil != 0 && Environment.TickCount64 >= statusUntil)
        {
            pendingRequest = 0;
            statusUntil = 0;
            SetClientError("No response from the server. Both client and server need the DEBUG build; refresh to retry.");
        }
    }

    private static void SetClientError(string message)
    {
        ClientStatus = ClientStatus with { Message = message, Success = false };
        StatusVersion++;
    }

    internal static void ResetClient()
    {
        pendingRequest = 0;
        pendingUntil = statusUntil = 0;
        HasStatus = false;
        ClientStatus = JungleWorldGenStatus.Empty();
        StatusVersion++;
    }
}

internal sealed class JungleWorldGenNetworkLifecycle : ModSystem
{
    public override void PostUpdateEverything() => JungleWorldGenNetHandler.TickTimeout();
    public override void OnWorldLoad() => JungleWorldGenNetHandler.ResetClient();
    public override void OnWorldUnload() => JungleWorldGenNetHandler.ResetClient();
    public override void Unload() => JungleWorldGenNetHandler.ResetClient();
}
#endif
