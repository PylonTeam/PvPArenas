using Microsoft.Xna.Framework;

namespace PvPArenas.Common.Game;

internal static class ArenaSpawnBoxes
{
    internal const int Thickness = 1;
    private const int TileSize = 16;

    internal static Rectangle BorderOuterTileArea(Rectangle area)
    {
        area.Inflate(Thickness, Thickness);
        return area;
    }

    internal static Rectangle TileToWorld(Rectangle tiles) =>
        new(tiles.X * TileSize, tiles.Y * TileSize, tiles.Width * TileSize, tiles.Height * TileSize);
}
