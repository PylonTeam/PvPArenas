using System.IO;
using Terraria.GameContent;

namespace PvPArenas.Common.Generation;

/// <summary>Value-owned world layers and visual metadata shared by generated and authored arenas.</summary>
internal sealed class ArenaWorldMetadata
{
    private readonly double surface = Main.worldSurface, rock = Main.rockLayer;
    private readonly Point spawn = new(Main.spawnTileX, Main.spawnTileY), dungeon = new(Main.dungeonX, Main.dungeonY);
    private readonly bool crimson = WorldGen.crimson;
    private readonly int moon = Main.moonType;
    private readonly int[] trees = (int[])Main.treeX.Clone(), treeStyles = (int[])Main.treeStyle.Clone();
    private readonly int[] caves = (int[])Main.caveBackX.Clone(), caveStyles = (int[])Main.caveBackStyle.Clone();
    private readonly int ice = Main.iceBackStyle, jungle = Main.jungleBackStyle, hell = Main.hellBackStyle;
    private readonly int[] backgrounds =
    [
        WorldGen.treeBG1, WorldGen.corruptBG, WorldGen.jungleBG, WorldGen.snowBG, WorldGen.hallowBG,
        WorldGen.crimsonBG, WorldGen.desertBG, WorldGen.oceanBG, WorldGen.mushroomBG, WorldGen.underworldBG,
        WorldGen.treeBG2, WorldGen.treeBG3, WorldGen.treeBG4
    ];
    private readonly byte[] treeTops;

    private ArenaWorldMetadata()
    {
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            WorldGen.TreeTops.SyncSend(writer);
        treeTops = stream.ToArray();
    }

    internal static ArenaWorldMetadata Capture() => new();

    internal void Apply()
    {
        Main.worldSurface = surface;
        Main.rockLayer = rock;
        Main.spawnTileX = spawn.X; Main.spawnTileY = spawn.Y;
        Main.dungeonX = dungeon.X; Main.dungeonY = dungeon.Y;
        WorldGen.crimson = crimson;
        Main.moonType = moon;
        Main.treeX = (int[])trees.Clone(); Main.treeStyle = (int[])treeStyles.Clone();
        Main.caveBackX = (int[])caves.Clone(); Main.caveBackStyle = (int[])caveStyles.Clone();
        for (int i = 0; i < backgrounds.Length; i++) WorldGen.setBG(i, backgrounds[i]);
        Main.iceBackStyle = ice; Main.jungleBackStyle = jungle; Main.hellBackStyle = hell;
        WorldGen.TreeTops = new TreeTopsInfo();
        using BinaryReader reader = new(new MemoryStream(treeTops, writable: false));
        WorldGen.TreeTops.SyncReceive(reader);
        WorldGen.BackgroundsCache = new BackgroundChangeFlashInfo();
    }
}
