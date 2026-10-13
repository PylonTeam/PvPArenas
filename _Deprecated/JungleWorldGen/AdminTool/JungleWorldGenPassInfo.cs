#if DEBUG
namespace PvPArenas.Common.AdminTools.JungleWorldGen;

/// <summary>Descriptions of the installed vanilla passes offered by the Jungle recipe.</summary>
internal static class JungleWorldGenPassInfo
{
    internal static string GetDescription(string name) => name?.Trim().ToLowerInvariant() switch
    {
        "reset" => "Initializes the source world's generation state and biome settings. Required before every recipe.",
        "terrain" => "Builds the source world's dirt and stone layers and sets their depths. Required by the later passes.",
        "dirt wall backgrounds" => "Adds natural background walls beneath the surface. Usually has little effect on the underground crop.",
        "rocks in dirt" => "Mixes irregular stone pockets into the dirt layers for less uniform terrain.",
        "dirt in rocks" => "Mixes irregular dirt pockets into the deeper stone layers.",
        "clay" => "Scatters clay deposits through the source world's upper layers.",
        "small holes" => "Carves scattered small cavities, with some liquid-filled holes. Adds variation before the Jungle is formed.",
        "dirt layer caves" => "Carves longer caves through the dirt and upper stone layers.",
        "rock layer caves" => "Carves larger, longer caves through the deeper stone layers. Can make the Jungle more open.",
        "jungle" => "Creates the vanilla Jungle's mud mass, natural walls, gems, caves and entrance tunnel. Required for the underground Jungle crop.",
        "mud caves to grass" => "Spreads Jungle grass onto exposed mud and removes small isolated clumps. Required for Jungle biome terrain.",
        "dirt to mud" => "Scatters additional mud pockets through the deeper source terrain.",
        "shinies" => "Adds the source world's pre-Hardmode ore deposits, including occasional evil-biome ore.",
        "wet jungle" => "Adds water just above surface Jungle grass. Often has little or no visible effect in an underground crop.",
        "hives" => "Attempts to build bee hives and nearby honey pockets in suitable Jungle terrain. Records sites for the later Larva pass.",
        "jungle chests" => "Builds small Jungle shrines and records their chest sites. The chests themselves are added by Jungle Chests Placement.",
        "settle liquids" => "Runs vanilla liquid settling after the main terrain and structures. Required to stabilize water and honey.",
        "smooth world" => "Rounds natural terrain with slopes and half blocks and cleans up thin edges.",
        "wall variety" => "Adds patches of natural cave-wall variants, including Jungle backgrounds. Changes the look of enclosed caves.",
        "life crystals" => "Attempts to place Life Crystals on suitable underground floors.",
        "jungle chests placement" => "Places and fills Ivy Chests at the shrine sites recorded earlier. Automatically requires Jungle Chests.",
        "cave walls" => "Fills selected enclosed cave pockets with natural wall types suited to nearby terrain.",
        "jungle trees" => "Attempts to grow large underground Jungle trees where there is enough open space.",
        "quick cleanup" => "Removes unsupported slopes and half blocks and repairs some terrain edges before small objects are placed.",
        "pots" => "Attempts to place breakable pots on suitable floors throughout the source world.",
        "spreading grass" => "Finishes surface grass and nearby biome transitions. Usually has little effect on the underground crop.",
        "piles" => "Scatters small and large rubble piles and other floor decorations, using Jungle variants where appropriate.",
        "webs and honey" => "Adds hive decorations, converts liquid behind hive walls to honey and fills existing spider-wall pockets with cobwebs.",
        "weeds" => "Adds small plants on surface grass. Jungle undergrowth comes from the separate Jungle plant passes.",
        "glowing mushrooms and jungle plants" => "Adds small plants on exposed Jungle grass and grows mushrooms on any existing mushroom grass. Does not create a mushroom biome.",
        "jungle plants" => "Attempts to place larger decorative Jungle plants on suitable Jungle-grass floors.",
        "vines" => "Grows hanging vines beneath suitable grass, including underground Jungle vines and occasional hanging Jungle plants.",
        "muds walls in jungle" => "Finishes mud-wall backgrounds near the surface Jungle and records its horizontal bounds. Required by this recipe.",
        "larva" => "Places Queen Bee larvae at sites recorded by hive generation. Automatically requires Hives; it does not create hives itself.",
        "settle liquids again" => "Runs the final vanilla liquid settling after plants and decorations. Required to stabilize the finished source.",
        "tile cleanup" => "Repairs tile shapes and object frames, removes invalid water-sensitive tiles and adds some liquid drips.",
        "water plants" => "Adds lily pads, cattails, bamboo and seaweed in suitable water. Much of this pass targets the surface.",
        "stalac" => "Adds stalactites, stalagmites and gem trees where suitable stone and cave space exist.",
        _ => "Runs this vanilla generation pass in the source world before the underground Jungle is cropped and mirrored."
    };

    internal static string GetCategory(string name) => name?.Trim().ToLowerInvariant() switch
    {
        "reset" or "terrain" => "Foundation",
        "rocks in dirt" or "dirt in rocks" or "clay" or "dirt to mud" => "Terrain",
        "small holes" or "dirt layer caves" or "rock layer caves" => "Caves",
        "jungle" or "mud caves to grass" or "muds walls in jungle" => "Jungle",
        "hives" or "jungle chests" or "jungle chests placement" or "larva" => "Structures",
        "shinies" or "life crystals" or "pots" => "Resources",
        "wet jungle" or "settle liquids" or "settle liquids again" => "Liquids",
        "smooth world" or "quick cleanup" or "tile cleanup" => "Cleanup",
        "dirt wall backgrounds" or "wall variety" or "cave walls" => "Walls",
        "jungle trees" or "spreading grass" or "piles" or "webs and honey" or "weeds"
            or "glowing mushrooms and jungle plants" or "jungle plants" or "vines" or "water plants" or "stalac" => "Decoration",
        _ => "Other"
    };
}
#endif
