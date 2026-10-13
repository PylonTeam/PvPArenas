#if DEBUG
using PvPArenas.Common.Generation;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PvPArenas.Common.AdminTools.JungleWorldGen;

/// <summary>Server-owned, world-session-only controls for the next Plantera generation.</summary>
internal sealed class JungleWorldGenSettings : ModSystem
{
    private VanillaJungleProfile profile = VanillaJungleProfile.Default;
    internal int? FixedSeed { get; private set; }

    internal static VanillaJungleProfile CaptureProfile() =>
        ModContent.GetInstance<JungleWorldGenSettings>().profile;

    internal static int CreateSeed() =>
        ModContent.GetInstance<JungleWorldGenSettings>().FixedSeed ?? Random.Shared.Next();

    internal void SetSeed(int? seed) => FixedSeed = seed;

    /// <summary>Validate the entire panel recipe before changing either server-owned setting.</summary>
    internal bool TryApply(IReadOnlyList<string> requested, int? fixedSeed, out string error)
    {
        error = "";
        try
        {
            VanillaJungleProfile next = new(requested);
            profile = next;
            FixedSeed = fixedSeed;
            return true;
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    internal void Reset()
    {
        profile = VanillaJungleProfile.Default;
        FixedSeed = null;
    }

    internal bool TrySetPasses(IReadOnlyList<string> requested, bool? enabled, out string error)
    {
        error = "";
        List<VanillaJunglePass> passes = [];
        foreach (string name in requested)
        {
            VanillaJunglePass pass = VanillaJungleProfile.Catalog.FirstOrDefault(candidate =>
                candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (pass == null)
            {
                error = $"Unknown underground Jungle pass '{name}'. Select a pass from the Jungle World Gen panel.";
                return false;
            }
            if (enabled == false && pass.Required)
            {
                error = $"'{pass.Name}' is required to initialize or finish the Jungle and cannot be disabled.";
                return false;
            }
            passes.Add(pass);
        }

        HashSet<string> selected = enabled == null ? new(StringComparer.OrdinalIgnoreCase)
            : new(profile.Passes, StringComparer.OrdinalIgnoreCase);
        foreach (VanillaJunglePass pass in passes)
        {
            if (enabled == false)
                selected.Remove(pass.Name);
            else
                selected.Add(pass.Name);
        }
        if (enabled == false)
        {
            // Removing a prerequisite removes its consumers, instead of the profile silently
            // re-enabling the prerequisite to satisfy a dependent left in the selection.
            if (!selected.Contains("Jungle Chests")) selected.Remove("Jungle Chests Placement");
            if (!selected.Contains("Hives")) selected.Remove("Larva");
        }
        // The profile supplies mandatory initialization and finishing dependencies in vanilla order.
        profile = new VanillaJungleProfile(selected);
        return true;
    }

    public override void OnWorldLoad() => Reset();
    public override void OnWorldUnload() => Reset();
}
#endif
