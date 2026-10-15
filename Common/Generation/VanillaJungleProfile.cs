using System;
using System.Collections.Generic;
using System.Linq;

namespace PvPArenas.Common.Generation;

internal sealed record VanillaJunglePass(string Name, bool Required, bool DefaultEnabled = true);

/// <summary>A Jungle-only recipe, in Terraria's original pass order. Foundation passes cannot be removed.</summary>
internal sealed class VanillaJungleProfile
{
    internal static IReadOnlyList<VanillaJunglePass> Catalog { get; } = Array.AsReadOnly(new[]
    {
        new VanillaJunglePass("Reset", true),
        new VanillaJunglePass("Terrain", true),
        new VanillaJunglePass("Dirt Wall Backgrounds", false),
        new VanillaJunglePass("Rocks In Dirt", false),
        new VanillaJunglePass("Dirt In Rocks", false),
        new VanillaJunglePass("Clay", false),
        new VanillaJunglePass("Small Holes", false),
        new VanillaJunglePass("Dirt Layer Caves", false),
        new VanillaJunglePass("Rock Layer Caves", false),
        new VanillaJunglePass("Jungle", true),
        new VanillaJunglePass("Mud Caves To Grass", true),
        new VanillaJunglePass("Mushroom Patches", false),
        new VanillaJunglePass("Marble", false),
        new VanillaJunglePass("Granite", false),
        new VanillaJunglePass("Dirt To Mud", false),
        new VanillaJunglePass("Shinies", false),
        new VanillaJunglePass("Gems", false),
        new VanillaJunglePass("Wet Jungle", false),
        new VanillaJunglePass("Jungle Temple", false),
        new VanillaJunglePass("Hives", false),
        new VanillaJunglePass("Jungle Chests", false),
        new VanillaJunglePass("Settle Liquids", true),
        new VanillaJunglePass("Smooth World", false),
        new VanillaJunglePass("Wall Variety", false),
        new VanillaJunglePass("Life Crystals", false),
        new VanillaJunglePass("Statues", false),
        new VanillaJunglePass("Jungle Chests Placement", false),
        new VanillaJunglePass("Spider Caves", false),
        new VanillaJunglePass("Gem Caves", false),
        new VanillaJunglePass("Moss", false),
        new VanillaJunglePass("Temple", false),
        new VanillaJunglePass("Cave Walls", false),
        new VanillaJunglePass("Jungle Trees", false),
        new VanillaJunglePass("Quick Cleanup", false),
        new VanillaJunglePass("Pots", false),
        new VanillaJunglePass("Spreading Grass", false),
        new VanillaJunglePass("Traps", false),
        new VanillaJunglePass("Piles", false),
        new VanillaJunglePass("Planting Trees", false),
        new VanillaJunglePass("Webs And Honey", false),
        new VanillaJunglePass("Weeds", false),
        new VanillaJunglePass("Glowing Mushrooms and Jungle Plants", false),
        new VanillaJunglePass("Jungle Plants", false),
        new VanillaJunglePass("Vines", false),
        new VanillaJunglePass("Mushrooms", false),
        new VanillaJunglePass("Random Gems", false),
        new VanillaJunglePass("Moss Grass", false),
        new VanillaJunglePass("Muds Walls In Jungle", true),
        new VanillaJunglePass("Larva", false),
        new VanillaJunglePass("Settle Liquids Again", true),
        new VanillaJunglePass("Tile Cleanup", false),
        new VanillaJunglePass("Lihzahrd Altars", false),
        new VanillaJunglePass("Water Plants", false),
        new VanillaJunglePass("Stalac", false)
    });

    internal static VanillaJungleProfile Default => new(Catalog.Where(pass => pass.DefaultEnabled).Select(pass => pass.Name));
    internal IReadOnlyList<string> Passes { get; }

    internal VanillaJungleProfile(IEnumerable<string> enabledPasses)
    {
        ArgumentNullException.ThrowIfNull(enabledPasses);
        HashSet<string> selected = new(enabledPasses, StringComparer.OrdinalIgnoreCase);
        string unknown = selected.FirstOrDefault(name => !Catalog.Any(pass => pass.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
        if (unknown != null) throw new ArgumentException($"'{unknown}' is not an underground Jungle pass.");
        foreach (VanillaJunglePass pass in Catalog.Where(pass => pass.Required)) selected.Add(pass.Name);
        // Placement consumes the sites recorded by Jungle Chests. Larvae consume Hive sites.
        if (selected.Contains("Jungle Chests Placement")) selected.Add("Jungle Chests");
        if (selected.Contains("Larva")) selected.Add("Hives");
        if (selected.Contains("Lihzahrd Altars")) selected.Add("Temple");
        if (selected.Contains("Temple")) selected.Add("Jungle Temple");
        Passes = Array.AsReadOnly(Catalog.Where(pass => selected.Contains(pass.Name)).Select(pass => pass.Name).ToArray());
    }
}
