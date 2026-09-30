using System;
using Terraria.ID;

namespace PvPArenas.Common.Generation;

/// <summary>Publishes changed tiles through Terraria's section messages, including partial edge sections.</summary>
internal static class ArenaTileSync
{
    internal static void Send(Rectangle bounds, int toClient = -1)
    {
        if (Main.netMode != NetmodeID.Server)
            return;
        bounds = Rectangle.Intersect(bounds, new Rectangle(0, 0, Main.maxTilesX, Main.maxTilesY));
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;
        int left = bounds.Left / 200, right = (bounds.Right - 1) / 200;
        int top = bounds.Top / 150, bottom = (bounds.Bottom - 1) / 150;
        int clients = 0, sections = 0;
        for (int client = 0; client < Main.maxPlayers; client++)
        {
            if (toClient >= 0 && client != toClient || !Netplay.Clients[client].IsConnected()
                || Netplay.Clients[client].State < 3)
                continue;
            clients++;
            for (int sx = left; sx <= right; sx++)
            for (int sy = top; sy <= bottom; sy++)
            {
                int x = sx * 200, y = sy * 150;
                // SendSection skips cached sections and assumes a full 200×150 block.
                NetMessage.SendData(MessageID.TileSection, client, -1, null, x, y,
                    Math.Min(200, Main.maxTilesX - x), Math.Min(150, Main.maxTilesY - y));
                Netplay.Clients[client].TileSections[sx, sy] = true;
                sections++;
            }
            NetMessage.SendData(MessageID.TileFrameSection, client, -1, null, left, top, right, bottom);
        }
        Log.Debug($"[worldgen] Tile sync | SectionsQueued: {sections}/{clients * (right - left + 1) * (bottom - top + 1)} | "
            + $"Clients: {clients} | Bounds: {bounds} | IDs: {MessageID.TileSection}/{MessageID.TileFrameSection}");
    }
}
