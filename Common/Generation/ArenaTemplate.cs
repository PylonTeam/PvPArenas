using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
    private static Point spawn, dungeon;
    private static bool crimson;
    internal static bool Available => tiles != null;

    public override void OnWorldLoad()
    {
        Clear();
        if (Main.netMode != NetmodeID.Server || Main.maxTilesX != ArenaWorldSystem.Width
            || Main.maxTilesY != ArenaWorldSystem.Height)
            return;
        ArenaWorldSystem.ConfigureAuthoredLayers();
        NormalizeAuthoredFrames();
        // Tilemap.Height is the memory stride, not the loaded world's height. Terraria
        // can retain a 2401-row allocation for this 600-row world. Store only live tiles.
        tiles = CurrentTiles().Select(array =>
        {
            Array copy = Array.CreateInstance(array.GetType().GetElementType(), Main.maxTilesX * Main.maxTilesY);
            for (int x = 0; x < Main.maxTilesX; x++)
                Array.Copy(array, x * Main.tile.Height, copy, x * Main.maxTilesY, Main.maxTilesY);
            return copy;
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
        surface = Main.worldSurface;
        rock = Main.rockLayer;
        spawn = new Point(Main.spawnTileX, Main.spawnTileY);
        dungeon = new Point(Main.dungeonX, Main.dungeonY);
        crimson = WorldGen.crimson;
        Log.Debug($"[worldgen] PASS | Template captured | Tiles: {Main.maxTilesX * Main.maxTilesY} | "
            + $"World: {Main.maxTilesX}x{Main.maxTilesY} | TileStorage: {Main.tile.Width}x{Main.tile.Height}");
    }

    internal static IEnumerable<Rectangle> Restore(Rectangle protectedLobby)
    {
        if (Main.netMode != NetmodeID.Server || !Available)
            throw new InvalidOperationException("The server's authored arena template is unavailable.");
        if (Main.maxTilesX != ArenaWorldSystem.Width || Main.maxTilesY != ArenaWorldSystem.Height)
            throw new InvalidOperationException("The world dimensions changed after the arena template was captured.");

        Main.worldSurface = surface;
        Main.rockLayer = rock;
        Main.spawnTileX = spawn.X;
        Main.spawnTileY = spawn.Y;
        Main.dungeonX = dungeon.X;
        Main.dungeonY = dungeon.Y;
        WorldGen.crimson = crimson;
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
                int source = x * Main.maxTilesY, target = x * Main.tile.Height;
                if (protectedLobby.Width > 0 && x >= protectedLobby.Left && x < protectedLobby.Right)
                {
                    Array.Copy(tiles[data], source, current[data], target, protectedLobby.Top);
                    Array.Copy(tiles[data], source + protectedLobby.Bottom, current[data], target + protectedLobby.Bottom,
                        Main.maxTilesY - protectedLobby.Bottom);
                }
                else
                    Array.Copy(tiles[data], source, current[data], target, Main.maxTilesY);
            }
            yield return new Rectangle(start, 0, end - start, Main.maxTilesY);
        }
        if (Main.netMode == NetmodeID.Server)
            Liquid.ReInit();
    }

    private static void NormalizeAuthoredFrames()
    {
        // TileSection clients reframe the complete map. The authored file contains some
        // unsupported vines/torches which that vanilla pass removes. Normalize once before
        // capture so the authoritative template already matches the playable client world.
        // Match SectionTileFrame's bounds/order without its client-only sectionManager.
        bool previousMapUpdate = WorldGen.noMapUpdate;
        try
        {
            WorldGen.noMapUpdate = true;
            for (int x = 0; x < Main.maxTilesX - 1; x++)
            for (int y = 0; y < Main.maxTilesY - 1; y++)
                WorldGen.Reframe(x, y, resetFrame: true);
        }
        finally { WorldGen.noMapUpdate = previousMapUpdate; }
        Liquid.ReInit();
        Log.Debug("[worldgen] PASS | Authored world normalized before template capture | Client framing: vanilla");
    }

    /// <summary>Checks every restored tile component; preserved lobby tiles are explicitly excluded.</summary>
    internal static void VerifyRestore(Rectangle protectedLobby)
    {
        if (Main.netMode != NetmodeID.Server)
            throw new InvalidOperationException("Only the server can verify an arena restore.");
        bool[] mismatches = new bool[Main.maxTilesX * Main.maxTilesY];
        Compare((TileTypeData[])tiles[0], Main.tile.GetData<TileTypeData>(), mismatches);
        Compare((WallTypeData[])tiles[1], Main.tile.GetData<WallTypeData>(), mismatches);
        Compare((LiquidData[])tiles[2], Main.tile.GetData<LiquidData>(), mismatches);
        Compare((TileWallBrightnessInvisibilityData[])tiles[3], Main.tile.GetData<TileWallBrightnessInvisibilityData>(), mismatches);
        Compare((TileWallWireStateData[])tiles[4], Main.tile.GetData<TileWallWireStateData>(), mismatches);
        int total = 0, failed = 0;
        Point first = new(-1, -1);
        for (int x = 0; x < Main.maxTilesX; x++)
        for (int y = 0; y < Main.maxTilesY; y++)
        {
            if (protectedLobby.Contains(x, y)) continue;
            total++;
            if (!mismatches[x * Main.maxTilesY + y]) continue;
            if (failed++ == 0) first = new(x, y);
        }
        Log.Debug($"[worldgen] {(failed == 0 ? "PASS" : "FAIL")} | Restore Arenas_v10 | "
            + $"TilesVerifiedToWork: {total - failed}/{total} | LobbyExcluded: {mismatches.Length - total} | "
            + $"Stride: {Main.tile.Height} | FirstMismatch: {first}");
        if (failed > 0)
            throw new InvalidOperationException($"Arena restore verification failed: {failed}/{total} tiles differ; first at {first}. See server.log.");
    }

    private static void Compare<T>(T[] expected, T[] actual, bool[] mismatches) where T : unmanaged
    {
        for (int x = 0; x < Main.maxTilesX; x++)
        {
            ReadOnlySpan<byte> source = MemoryMarshal.AsBytes(expected.AsSpan(x * Main.maxTilesY, Main.maxTilesY));
            ReadOnlySpan<byte> target = MemoryMarshal.AsBytes(actual.AsSpan(x * Main.tile.Height, Main.maxTilesY));
            if (source.SequenceEqual(target)) continue;
            int size = source.Length / Main.maxTilesY;
            for (int y = 0; y < Main.maxTilesY; y++)
                if (!source.Slice(y * size, size).SequenceEqual(target.Slice(y * size, size)))
                    mismatches[x * Main.maxTilesY + y] = true;
        }
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
