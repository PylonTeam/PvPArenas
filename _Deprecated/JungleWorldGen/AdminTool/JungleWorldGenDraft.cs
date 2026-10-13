#if DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PvPArenas.Common.Generation;

namespace PvPArenas.Common.AdminTools.JungleWorldGen;

/// <summary>Client-local edits. Server polling never changes this model implicitly.</summary>
internal sealed class JungleWorldGenDraft
{
    private readonly HashSet<string> selected = new(StringComparer.OrdinalIgnoreCase);
    internal bool UseFixedSeed { get; set; }
    internal string SeedText { get; set; } = "0";
    internal IReadOnlyList<string> Passes => VanillaJungleProfile.Catalog.Where(pass => selected.Contains(pass.Name)).Select(pass => pass.Name).ToArray();
    internal bool IsSelected(string name) => selected.Contains(name);

    internal JungleWorldGenDraft() => SetPasses(VanillaJungleProfile.Default.Passes);

    internal void SetPasses(IEnumerable<string> passes)
    {
        VanillaJungleProfile profile = new(passes);
        selected.Clear();
        selected.UnionWith(profile.Passes);
    }

    internal bool SetPass(string name, bool enabled)
    {
        VanillaJunglePass pass = VanillaJungleProfile.Catalog.FirstOrDefault(pass => pass.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (pass == null || pass.Required && !enabled) return false;
        bool changed = enabled ? selected.Add(pass.Name) : selected.Remove(pass.Name);
        if (enabled)
        {
            if (pass.Name == "Jungle Chests Placement") changed |= selected.Add("Jungle Chests");
            if (pass.Name == "Larva") changed |= selected.Add("Hives");
        }
        else
        {
            if (pass.Name == "Jungle Chests") changed |= selected.Remove("Jungle Chests Placement");
            if (pass.Name == "Hives") changed |= selected.Remove("Larva");
        }
        return changed;
    }

    internal void SetPreset(string preset)
    {
        switch (preset)
        {
            case "Minimum": SetPasses([]); break;
            case "Caves":
                SetPasses(VanillaJungleProfile.Catalog.Where(pass => pass.Required || pass.Name is "Small Holes" or "Dirt Layer Caves" or "Rock Layer Caves" or "Dirt To Mud" or "Smooth World" or "Cave Walls" or "Quick Cleanup" or "Tile Cleanup").Select(pass => pass.Name));
                break;
            case "No hives":
                SetPasses(VanillaJungleProfile.Default.Passes);
                SetPass("Hives", false);
                break;
            case "Less wet detail":
                SetPasses(VanillaJungleProfile.Default.Passes);
                SetPass("Wet Jungle", false);
                SetPass("Webs And Honey", false);
                SetPass("Water Plants", false);
                break;
            default: SetPasses(VanillaJungleProfile.Default.Passes); break;
        }
    }

    internal void InvertOptional()
    {
        string[] inverted = VanillaJungleProfile.Catalog.Where(pass => pass.Required || !selected.Contains(pass.Name)).Select(pass => pass.Name).ToArray();
        selected.Clear();
        selected.UnionWith(inverted);
        // An inverted prerequisite that becomes disabled also disables its dependent.
        if (!selected.Contains("Jungle Chests")) selected.Remove("Jungle Chests Placement");
        if (!selected.Contains("Hives")) selected.Remove("Larva");
    }

    internal void Load(IReadOnlyList<string> passes, int? seed)
    {
        SetPasses(passes);
        UseFixedSeed = seed.HasValue;
        SeedText = seed?.ToString(CultureInfo.InvariantCulture) ?? "0";
    }

    internal bool TryGetRecipe(out IReadOnlyList<string> passes, out int? seed, out string error)
    {
        passes = Passes;
        seed = null;
        error = "";
        if (!UseFixedSeed) return true;
        if (int.TryParse(SeedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            seed = parsed;
            return true;
        }
        error = "Seed must be a signed 32-bit integer (-2147483648 to 2147483647).";
        return false;
    }
}
#endif
