using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria.DataStructures;
using Terraria.ID;

namespace PvPArenas.Common.Generation;

/// <summary>Keeps the authored arena in memory so a later round can restore it without reloading the world.</summary>
internal sealed class ArenaTemplate : ModSystem
{
    private static Array[] tiles;
    private static Chest[] chests;
    private static Sign[] signs;
    private static byte[][] entities;
    private static double surface, rock;
    internal static bool Available => tiles != null;

    public override void OnWorldLoad()
    {
        Clear();
        if (Main.netMode != NetmodeID.Server || Main.maxTilesX != ArenaWorldSystem.Width
            || Main.maxTilesY != ArenaWorldSystem.Height)
            return;
        tiles = CurrentTiles().Select(array => (Array)array.Clone()).ToArray();
        chests = Main.chest.Select(CloneChest).ToArray();
        signs = Main.sign.Select(CloneSign).ToArray();
        entities = TileEntity.ByID.Values.Select(entity =>
        {
            using MemoryStream stream = new();
            using BinaryWriter writer = new(stream);
            TileEntity.Write(writer, entity, networkSend: true);
            return stream.ToArray();
        }).ToArray();
        surface = Main.worldSurface;
        rock = Main.rockLayer;
    }

    internal static IEnumerable<Rectangle> Restore(Rectangle protectedLobby)
    {
        if (Main.netMode != NetmodeID.Server || !Available)
            throw new InvalidOperationException("The server's authored arena template is unavailable.");
        if (Main.maxTilesX != ArenaWorldSystem.Width || Main.maxTilesY != ArenaWorldSystem.Height)
            throw new InvalidOperationException("The world dimensions changed after the arena template was captured.");

        Main.worldSurface = surface;
        Main.rockLayer = rock;
        RestoreEntities(protectedLobby);
        Array[] current = CurrentTiles();
        protectedLobby = Rectangle.Intersect(protectedLobby, new Rectangle(0, 0, Main.maxTilesX, Main.maxTilesY));
        for (int start = 0; start < Main.maxTilesX; start += 200)
        {
            if (Main.netMode != NetmodeID.Server)
                yield break;
            int end = Math.Min(start + 200, Main.maxTilesX);
            for (int x = start; x < end; x++)
            for (int data = 0; data < tiles.Length; data++)
            {
                int offset = x * Main.maxTilesY;
                if (protectedLobby.Width > 0 && x >= protectedLobby.Left && x < protectedLobby.Right)
                {
                    Array.Copy(tiles[data], offset, current[data], offset, protectedLobby.Top);
                    Array.Copy(tiles[data], offset + protectedLobby.Bottom, current[data], offset + protectedLobby.Bottom,
                        Main.maxTilesY - protectedLobby.Bottom);
                }
                else
                    Array.Copy(tiles[data], offset, current[data], offset, Main.maxTilesY);
            }
            yield return new Rectangle(start, 0, end - start, Main.maxTilesY);
        }
        if (Main.netMode == NetmodeID.Server)
            Liquid.ReInit();
    }

    internal static void ClearEntitiesOutside(Rectangle protectedLobby)
    {
        if (Main.netMode != NetmodeID.Server)
            return;
        for (int i = 0; i < Main.chest.Length; i++)
            if (Main.chest[i] is { } chest && !protectedLobby.Contains(chest.x, chest.y))
                Main.chest[i] = null;
        for (int i = 0; i < Main.sign.Length; i++)
            if (Main.sign[i] is { } sign && !protectedLobby.Contains(sign.x, sign.y))
                Main.sign[i] = null;
        foreach (TileEntity entity in TileEntity.ByID.Values.ToArray())
        {
            if (protectedLobby.Contains(entity.Position.X, entity.Position.Y))
                continue;
            TileEntity.ByID.Remove(entity.ID);
            TileEntity.ByPosition.Remove(entity.Position);
            NetMessage.SendData(MessageID.TileEntitySharing, -1, -1, null, entity.ID);
        }
    }

    private static void RestoreEntities(Rectangle protectedLobby)
    {
        ClearEntitiesOutside(protectedLobby);
        for (int i = 0; i < chests.Length; i++)
            if (chests[i] is { } chest && !protectedLobby.Contains(chest.x, chest.y))
            {
                int slot = Main.chest[i] == null ? i : Array.FindIndex(Main.chest, entry => entry == null);
                if (slot < 0) throw new InvalidOperationException("No free slot remains for an authored chest.");
                Main.chest[slot] = CloneChest(chest);
            }
        for (int i = 0; i < signs.Length; i++)
            if (signs[i] is { } sign && !protectedLobby.Contains(sign.x, sign.y))
            {
                int slot = Main.sign[i] == null ? i : Array.FindIndex(Main.sign, entry => entry == null);
                if (slot < 0) throw new InvalidOperationException("No free slot remains for an authored sign.");
                Main.sign[slot] = CloneSign(sign);
            }
        foreach (byte[] bytes in entities)
        {
            using BinaryReader reader = new(new MemoryStream(bytes, writable: false));
            TileEntity entity = TileEntity.Read(reader, networkSend: true);
            if (protectedLobby.Contains(entity.Position.X, entity.Position.Y))
                continue;
            while (TileEntity.ByID.ContainsKey(entity.ID))
                entity.ID = TileEntity.AssignNewID();
            TileEntity.ByID[entity.ID] = entity;
            TileEntity.ByPosition[entity.Position] = entity;
            TileEntity.TileEntitiesNextID = Math.Max(TileEntity.TileEntitiesNextID, entity.ID + 1);
        }
    }

    private static Chest CloneChest(Chest chest)
    {
        if (chest == null) return null;
        Chest clone = (Chest)chest.Clone();
        clone.item = chest.item.Select(item => item?.Clone()).ToArray();
        return clone;
    }

    private static Sign CloneSign(Sign sign) => sign == null ? null : new Sign { x = sign.x, y = sign.y, text = sign.text };

    private static Array[] CurrentTiles() =>
    [
        Main.tile.GetData<TileTypeData>(), Main.tile.GetData<WallTypeData>(), Main.tile.GetData<LiquidData>(),
        Main.tile.GetData<TileWallBrightnessInvisibilityData>(), Main.tile.GetData<TileWallWireStateData>()
    ];

    public override void OnWorldUnload() => Clear();
    public override void Unload() => Clear();
    private static void Clear() { tiles = null; chests = null; signs = null; entities = null; }
}
