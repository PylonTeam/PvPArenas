using System;
using MonoMod.RuntimeDetour;
using PvPArenas.Common.Game;

namespace PvPArenas.Common.Generation;

/// <summary>The compact Jungle has surface and caves, without an artificial Hell band through its arena.</summary>
internal sealed class JungleDepthSystem : ModSystem
{
    private Hook underworld;

    internal static bool IsJungle => ArenaWorldSystem.IsCompactWorld
        && ModContent.GetInstance<RoundManager>() is { IsActive: true } manager
        && manager.TryGetSelectedPreset(out var preset) && preset.ArenaKind == ArenaKind.UndergroundJungle;

    public override void Load() => underworld = new Hook(
        typeof(Main).GetProperty(nameof(Main.UnderworldLayer)).GetMethod,
        (Func<Func<int>, int>)(original => IsJungle ? Main.maxTilesY - 10 : original()));

    public override void Unload()
    {
        underworld?.Dispose();
        underworld = null;
    }
}
