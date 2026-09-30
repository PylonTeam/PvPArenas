using System;
using System.IO;
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
                SendSection(client, new Rectangle(x, y, Math.Min(200, Main.maxTilesX - x), Math.Min(150, Main.maxTilesY - y)));
                Netplay.Clients[client].TileSections[sx, sy] = true;
                sections++;
            }
            SendFrames(client, left, top, right, bottom);
        }
        Log.Debug($"[worldgen] Tile sync | SectionsQueued: {sections}/{clients * (right - left + 1) * (bottom - top + 1)} | "
            + $"Clients: {clients} | Bounds: {bounds} | IDs: {MessageID.TileSection}/{MessageID.TileFrameSection}");
    }

    internal static void SendSection(int client, Rectangle bounds)
    {
        // Windows TcpSocket.AsyncSend retains the supplied array until BeginWrite finishes.
        // NetMessage.SendData reuses its writer buffer, which is unsafe for a burst of large
        // sections. Give each standard TileSection packet its own immutable backing array.
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((byte)MessageID.TileSection);
        NetMessage.CompressTileBlock(bounds.X, bounds.Y, (short)bounds.Width, (short)bounds.Height, stream);
        if (stream.Length > 60_000)
        {
            if (bounds.Width <= 1 && bounds.Height <= 1)
                throw new InvalidOperationException("An arena tile entity exceeds the network packet limit.");
            bool horizontal = bounds.Width >= bounds.Height;
            int half = (horizontal ? bounds.Width : bounds.Height) / 2;
            Rectangle first = bounds, second = bounds;
            if (horizontal)
            {
                first.Width = half;
                second.X += half;
                second.Width -= half;
            }
            else
            {
                first.Height = half;
                second.Y += half;
                second.Height -= half;
            }
            SendSection(client, first);
            SendSection(client, second);
            return;
        }
        stream.Position = 0;
        writer.Write((ushort)stream.Length);
        SendOwned(client, stream.ToArray());
    }

    internal static void SendFrames(int client, int left, int top, int right, int bottom)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)11);
        writer.Write((byte)MessageID.TileFrameSection);
        writer.Write((short)left);
        writer.Write((short)top);
        writer.Write((short)right);
        writer.Write((short)bottom);
        SendOwned(client, stream.ToArray());
    }

    private static void SendOwned(int client, byte[] packet)
    {
        if (Main.netMode != NetmodeID.Server || !Netplay.Clients[client].IsConnected())
            return;
        Netplay.Clients[client].Socket.AsyncSend(packet, 0, packet.Length, Netplay.Clients[client].ServerWriteCallBack);
    }

    // Hash only the state that Terraria's TileSection codec actually preserves. Decorative
    // terrain frames, wall frames, invisible inactive tile types and other runtime bits are
    // intentionally excluded. Important object frames are part of their tile identity.
    internal static ulong Fingerprint(Rectangle bounds)
    {
        ulong hash = 14695981039346656037UL;
        for (int y = bounds.Top; y < bounds.Bottom; y++)
        for (int x = bounds.Left; x < bounds.Right; x++)
        {
            Tile tile = Main.tile[x, y];
            bool active = tile.HasTile;
            Mix(ref hash, active ? (uint)tile.TileType + 1 : 0);
            Mix(ref hash, tile.WallType);
            uint flags = (tile.RedWire ? 1u : 0) | (tile.BlueWire ? 2u : 0) | (tile.GreenWire ? 4u : 0)
                | (tile.YellowWire ? 8u : 0) | (tile.HasActuator ? 16u : 0) | (tile.IsActuated ? 32u : 0)
                | (tile.IsTileInvisible ? 64u : 0) | (tile.IsWallInvisible ? 128u : 0)
                | (tile.IsTileFullbright ? 256u : 0) | (tile.IsWallFullbright ? 512u : 0);
            if (active && Main.tileSolid[tile.TileType])
                flags |= (uint)(tile.IsHalfBlock ? 1 : (int)tile.Slope == 0 ? 0 : (int)tile.Slope + 1) << 10;
            Mix(ref hash, flags);
            Mix(ref hash, (uint)tile.LiquidAmount | (tile.LiquidAmount > 0 ? (uint)tile.LiquidType << 8 : 0));
            Mix(ref hash, (active ? (uint)tile.TileColor : 0) | (tile.WallType != WallID.None ? (uint)tile.WallColor << 8 : 0));
            Mix(ref hash, active && Main.tileFrameImportant[tile.TileType]
                ? (uint)(ushort)tile.TileFrameX | (uint)(ushort)tile.TileFrameY << 16 : 0);
        }
        return hash;
    }

    private static void Mix(ref ulong hash, uint value) => hash = unchecked((hash ^ value) * 1099511628211UL);
}
