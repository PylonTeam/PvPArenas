using System.Collections.Generic;
using System.Linq;
using Terraria.ID;

namespace PvPArenas.Common.Generation;

/// <summary>Vanilla objects whose frames predate TileObjectData.</summary>
internal static class JungleLegacyObjects
{
    private readonly record struct FrameState(bool Active, ushort Type, short X, short Y);

    internal static bool Normalize(Rectangle bounds)
    {
        List<Point> objects = [];
        for (int x = bounds.Left; x < bounds.Right; x++)
        for (int y = bounds.Top; y < bounds.Bottom; y++)
        {
            Tile tile = Main.tile[x, y];
            if (tile.HasTile && Main.tileFrameImportant[tile.TileType]) objects.Add(new(x, y));
        }
        FrameState State(Point point)
        {
            Tile tile = Main.tile[point.X, point.Y];
            return new(tile.HasTile, tile.TileType, tile.TileFrameX, tile.TileFrameY);
        }
        bool previousLiquidCheck = WorldGen.noLiquidCheck;
        bool previousGeneration = WorldGen.gen, previousTileActions = WorldGen.noTileActions;
        try
        {
            WorldGen.noLiquidCheck = true;
            WorldGen.gen = WorldGen.noTileActions = true;
            // Native framing validates anchors as well as complete object bodies.
            // Settle it before mirroring: mirrored solid frames can otherwise cause
            // incidental neighbour checks only on one side of an invalid object.
            for (int pass = 0; pass < 8; pass++)
            {
                FrameState[] before = objects.Select(State).ToArray();
                foreach (Point point in objects)
                {
                    Tile tile = Main.tile[point.X, point.Y];
                    if (tile.HasTile && Main.tileFrameImportant[tile.TileType])
                        WorldGen.TileFrame(point.X, point.Y, noBreak: false);
                }
                if (before.SequenceEqual(objects.Select(State))) return true;
            }
            return false;
        }
        finally
        {
            WorldGen.noLiquidCheck = previousLiquidCheck;
            WorldGen.gen = previousGeneration;
            WorldGen.noTileActions = previousTileActions;
        }
    }

    internal static bool TryGetBounds(int x, int y, Tile tile, out Rectangle bounds)
    {
        bounds = new(x, y, 1, 1);
        switch (tile.TileType)
        {
            case TileID.Pots:
            case TileID.LifeFruit:
            case TileID.PlanteraBulb:
                bounds = new(x - tile.TileFrameX % 36 / 18, y - tile.TileFrameY % 36 / 18, 2, 2);
                return true;
            case TileID.PlantDetritus:
                int width = tile.TileFrameY < 36 ? 3 : 2;
                bounds = new(x - tile.TileFrameX % (width * 18) / 18, y - tile.TileFrameY % 36 / 18, width, 2);
                return true;
            case TileID.SmallPiles when tile.TileFrameY == 18:
                bounds = new(x - tile.TileFrameX % 36 / 18, y, 2, 1);
                return true;
            case TileID.Stalactite when tile.TileFrameY < 72:
                bounds = new(x, y - tile.TileFrameY % 36 / 18, 1, 2);
                return true;
            default:
                return false;
        }
    }

    internal static void ReflectTrapFrame(Tile original, Tile reflected)
    {
        int frame = original.TileFrameX, style = original.TileFrameY / 18;
        if (style is 0 or 1 or 2 or 5)
            frame = frame switch { 0 => 18, 18 => 0, _ => frame };
        else if (style is 3 or 4)
            frame = frame switch { 54 => 72, 72 => 54, _ => frame };
        reflected.TileFrameX = (short)frame;
        reflected.TileFrameY = original.TileFrameY;
    }

    internal static void ReflectTreeFrame(Tile original, Tile reflected)
    {
        int x = original.TileFrameX, y = original.TileFrameY;
        if (x is 132 or 154 && y is >= 0 and <= 44)
            y += 66;
        else if (x is 132 or 154 && y is >= 66 and <= 110)
            y -= 66;
        else if (y is >= 198 and <= 242)
        {
            if (x == 44) x = 66;
            else if (x == 66) x = 44;
        }
        else if (y is >= 132 and <= 176)
        {
            x = x switch { 22 => 44, 44 => 22, 0 => 66, 66 => 0, _ => x };
        }
        else if (y is >= 0 and <= 44)
        {
            if (x == 88) { x = 66; y += 66; }
            else if (x == 66) { x = 88; y += 66; }
        }
        else if (y is >= 66 and <= 110)
        {
            if (x == 66) { x = 88; y -= 66; }
            else if (x == 88) { x = 66; y -= 66; }
        }
        reflected.TileFrameX = (short)x;
        reflected.TileFrameY = (short)y;
    }
}
