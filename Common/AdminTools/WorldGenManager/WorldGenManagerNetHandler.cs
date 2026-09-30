using PvPArenas.Core.Compat;
using System.Collections.Generic;
using System.IO;
using Terraria.Chat;
using Terraria.ID;
using Terraria.Localization;

namespace PvPArenas.Common.AdminTools.WorldGenManager;

internal static class WorldGenManagerNetHandler
{
    private enum Request : byte { Status, Run }
    private const int MaxPasses = 512;

    internal static void RequestStatus()
    {
        if (Main.netMode == NetmodeID.MultiplayerClient)
            BeginRequest(Request.Status).Send();
    }

    internal static bool RequestRunPasses(IReadOnlyList<string> passes, out string error)
    {
        WorldGenPassRunner runner = ModContent.GetInstance<WorldGenPassRunner>();
        if (Main.netMode != NetmodeID.MultiplayerClient)
            return runner.TryRun(passes, Main.myPlayer, out error);
        error = "";
        if (runner.Busy || passes == null || passes.Count == 0 || passes.Count > MaxPasses)
        {
            error = runner.Busy ? "Generation is already running." : "Select at least one pass.";
            return false;
        }
        ModPacket packet = BeginRequest(Request.Run);
        packet.Write((ushort)passes.Count);
        foreach (string pass in passes)
            packet.Write(pass);
        packet.Send();
        runner.SetAwaitingServer();
        return true;
    }

    internal static void HandleRequest(BinaryReader reader, int whoAmI)
    {
        if (Main.netMode != NetmodeID.Server)
            return;
        Request request = (Request)reader.ReadByte();
        if (!ErkySSCCompat.IsAdmin(whoAmI, out string error))
        {
            ChatHelper.SendChatMessageToClient(NetworkText.FromLiteral(error), Color.OrangeRed, whoAmI);
            SendStatus(ModContent.GetInstance<WorldGenPassRunner>(), whoAmI, includePasses: true);
            return;
        }
        WorldGenPassRunner runner = ModContent.GetInstance<WorldGenPassRunner>();
        if (request == Request.Run)
        {
            int count = reader.ReadUInt16();
            if (count == 0 || count > MaxPasses)
                error = "Invalid pass selection.";
            else
            {
                string[] names = new string[count];
                for (int i = 0; i < count; i++)
                    names[i] = reader.ReadString();
                runner.TryRun(names, whoAmI, out error);
            }
            if (!string.IsNullOrEmpty(error))
                ChatHelper.SendChatMessageToClient(NetworkText.FromLiteral(error), Color.OrangeRed, whoAmI);
        }
        SendStatus(runner, whoAmI, includePasses: true);
    }

    internal static void HandleStatus(BinaryReader reader)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient)
            ModContent.GetInstance<WorldGenPassRunner>().ReadStatus(reader);
    }

    internal static void SendStatus(WorldGenPassRunner runner, int toClient = -1, bool includePasses = false)
    {
        if (Main.netMode != NetmodeID.Server)
            return;
        ModPacket packet = ModContent.GetInstance<PvPArenas>().GetPacket();
        packet.Write((byte)PvPArenas.PacketType.WorldGenStatus);
        runner.WriteStatus(packet, includePasses);
        packet.Send(toClient);
    }

    private static ModPacket BeginRequest(Request request)
    {
        ModPacket packet = ModContent.GetInstance<PvPArenas>().GetPacket();
        packet.Write((byte)PvPArenas.PacketType.WorldGenRequest);
        packet.Write((byte)request);
        return packet;
    }
}
