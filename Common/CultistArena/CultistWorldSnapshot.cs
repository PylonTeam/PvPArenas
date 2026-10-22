using PvPArenas.Common.Generation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Terraria.DataStructures;

namespace PvPArenas.Common.CultistArena;

/// <summary>Owns generated values after the scoped vanilla world has been disposed.</summary>
internal sealed class CultistWorldSnapshot
{
    private readonly Array[] tiles;
    private readonly Chest[] chests;
    private readonly Sign[] signs;
    private readonly byte[][] entities;
    private readonly ArenaWorldMetadata metadata;
    private readonly int width, height;

    internal CultistWorldSnapshot()
    {
        width = Main.maxTilesX; height = Main.maxTilesY;
        metadata = ArenaWorldMetadata.Capture();
        tiles = CurrentTiles().Select(array =>
        {
            Array clone = Array.CreateInstance(array.GetType().GetElementType(), width * height);
            for (int x = 0; x < width; x++)
                Array.Copy(array, x * Main.tile.Height, clone, x * height, height);
            return clone;
        }).ToArray();
        chests = Main.chest.Select(CloneChest).ToArray();
        signs = Main.sign.Select(CloneSign).ToArray();
        entities = TileEntity.ByID.Values.Select(entity =>
        {
            using MemoryStream stream = new();
            using BinaryWriter writer = new(stream);
            TileEntity.Write(writer, entity, networkSend: true);
            return stream.ToArray();
        }).ToArray();
    }

    internal void ApplyMetadata() => metadata.Apply();

    internal void CopyColumns(int start, int end)
    {
        if (Main.maxTilesX != width || Main.maxTilesY != height
            || Main.tile.Width < width || Main.tile.Height < height)
            throw new InvalidOperationException("The Cultist arena no longer fits the fixed session world.");
        Array[] current = CurrentTiles();
        for (int x = start; x < end; x++)
        for (int component = 0; component < tiles.Length; component++)
            Array.Copy(tiles[component], x * height, current[component], x * Main.tile.Height, height);
    }

    internal void ApplyEntities()
    {
        ArenaTemplate.ClearEntitiesOutside(Rectangle.Empty);
        Main.chest = chests.Select(CloneChest).ToArray();
        Main.sign = signs.Select(CloneSign).ToArray();
        TileEntity.ByID.Clear();
        TileEntity.ByPosition.Clear();
        TileEntity.TileEntitiesNextID = 0;
        foreach (byte[] bytes in entities)
        {
            using BinaryReader reader = new(new MemoryStream(bytes, writable: false));
            TileEntity entity = TileEntity.Read(reader, networkSend: true);
            TileEntity.ByID[entity.ID] = entity;
            TileEntity.ByPosition[entity.Position] = entity;
            TileEntity.TileEntitiesNextID = Math.Max(TileEntity.TileEntitiesNextID, entity.ID + 1);
        }
    }

    /// <summary>Same seven-byte row-major semantic export used to verify the selected prototype.</summary>
    internal static string SemanticFingerprint()
    {
        using MemoryStream stream = new(Main.maxTilesX * Main.maxTilesY * 7);
        using (BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        for (int y = 0; y < Main.maxTilesY; y++)
        for (int x = 0; x < Main.maxTilesX; x++)
        {
            Tile tile = Main.tile[x, y];
            writer.Write(tile.TileType); writer.Write(tile.WallType);
            writer.Write(tile.LiquidAmount); writer.Write((byte)tile.LiquidType);
            writer.Write((byte)(tile.HasTile ? 1 : 0));
        }
        return Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }

    private static Chest CloneChest(Chest chest)
    {
        if (chest == null) return null;
        Chest clone = (Chest)chest.Clone();
        clone.item = chest.item.Select(item => item?.Clone() ?? new Item()).ToArray();
        return clone;
    }

    private static Sign CloneSign(Sign sign) => sign == null ? null : new Sign { x = sign.x, y = sign.y, text = sign.text };

    private static Array[] CurrentTiles() =>
    [
        Main.tile.GetData<TileTypeData>(), Main.tile.GetData<WallTypeData>(), Main.tile.GetData<LiquidData>(),
        Main.tile.GetData<TileWallBrightnessInvisibilityData>(), Main.tile.GetData<TileWallWireStateData>()
    ];
}
