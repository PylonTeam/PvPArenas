using Microsoft.Xna.Framework;

namespace PvPArenas.Common.Game;

internal static class ArenaSpawnBoxes
{
    private const int TileSize = 16;

    internal static Rectangle TileToWorld(Rectangle tiles) =>
        new(tiles.X * TileSize, tiles.Y * TileSize, tiles.Width * TileSize, tiles.Height * TileSize);
}
