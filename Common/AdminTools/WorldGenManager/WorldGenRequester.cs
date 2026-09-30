using System;
using Terraria.Chat;
using Terraria.ID;
using Terraria.Localization;

namespace PvPArenas.Common.AdminTools.WorldGenManager;

/// <summary>Bind job notices to the initiating connection, never to a reused player slot.</summary>
internal sealed class WorldGenRequester(int playerId, Player player, object connection)
{
    private readonly string name = player?.name ?? "server";
    internal string Description => playerId < 0 ? "server" : $"{name.Replace('\r', ' ').Replace('\n', ' ')}#{playerId}";

    internal static WorldGenRequester Capture(int id) => Main.netMode == NetmodeID.Server
        && id >= 0 && id < Main.maxPlayers && Main.player[id]?.active == true
        ? new(id, Main.player[id], Netplay.Clients[id]?.Socket) : new(-1, null, null);

    internal bool Matches(Player current, object socket) => playerId >= 0 && current?.active == true
        && ReferenceEquals(player, current) && current.name == name
        && connection != null && ReferenceEquals(connection, socket);

    internal void Notify(string message, bool warning)
    {
        if (Main.netMode != NetmodeID.Server || playerId < 0 || playerId >= Main.maxPlayers
            || !Matches(Main.player[playerId], Netplay.Clients[playerId]?.Socket)
            || !Netplay.Clients[playerId].IsConnected()) return;
        ChatHelper.SendChatMessageToClient(NetworkText.FromLiteral(message),
            warning ? Color.OrangeRed : Color.LightGreen, playerId);
    }
}
