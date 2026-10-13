using PvPArenas.Common.Game;
using System.Collections.Generic;

namespace PvPArenas.Common.Generation;

/// <summary>Plantera generation entry point; uses Terraria's actual vanilla passes.</summary>
internal static class MirroredJungleGenerator
{
    internal static ArenaLayout CreateLayout() => VanillaMirroredJungleGenerator.CreateLayout();
    internal static void ResetLayout() => VanillaMirroredJungleGenerator.ResetLayout();
    internal static IEnumerable<Rectangle> Generate(int seed, Rectangle protectedLobby) => VanillaMirroredJungleGenerator.Generate(seed, protectedLobby);
    internal static IEnumerable<Rectangle> GenerateWithProgress(int seed, Rectangle protectedLobby, WorldGenProgressLog trace) =>
        VanillaMirroredJungleGenerator.GenerateWithProgress(seed, protectedLobby, trace);
}
