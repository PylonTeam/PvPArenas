using System;
using MonoMod.Cil;

namespace PvPArenas.Common.Generation;

/// <summary>Adapts vanilla's background depth assumptions to the compact arena, without changing tiles.</summary>
[Autoload(Side = ModSide.Client)]
internal sealed class CompactArenaBackground : ModSystem
{
    public override void Load()
    {
        IL_Main.DrawBackground += AdjustBackgroundDepth;
        IL_Main.OldDrawBackground += AdjustBackgroundDepth;
        On_Main.DrawUnderworldBackground += DrawUnderworld;
    }

    public override void Unload()
    {
        IL_Main.DrawBackground -= AdjustBackgroundDepth;
        IL_Main.OldDrawBackground -= AdjustBackgroundDepth;
        On_Main.DrawUnderworldBackground -= DrawUnderworld;
    }

    private static void AdjustBackgroundDepth(ILContext il)
    {
        // Vanilla starts its deep-cavern black fill at height-330 (height-230
        // with backgrounds disabled). In Arenas_v10 that is ABOVE the surface,
        // covering the sky before any underground background can be drawn.
        ILCursor cursor = new(il);
        if (!cursor.TryGotoNext(MoveType.After,
            i => i.MatchLdsfld<Main>(nameof(Main.maxTilesY)),
            i => i.MatchLdcI4(330) || i.MatchLdcI4(230),
            i => i.MatchSub()))
            throw new InvalidOperationException($"Could not adapt {il.Method.Name} to compact arena backgrounds.");
        cursor.EmitDelegate<Func<int, int>>(BackgroundDepth);
        Log.Debug($"[worldgen] PASS | Compact background hook: {il.Method.Name}");
    }

    internal static int BackgroundDepth(int vanillaDepth) => ArenaWorldSystem.IsCompactWorld
        ? Math.Max(vanillaDepth, (int)Main.rockLayer + 80) : vanillaDepth;

    private static void DrawUnderworld(On_Main.orig_DrawUnderworldBackground orig, Main main, bool flat)
    {
        // A surface arena extending below vanilla's fixed underworld cutoff
        // still needs its sky. The underground Plantera arena keeps vanilla rendering.
        if (ArenaWorldSystem.IsCompactWorld && Main.worldSurface >= Main.UnderworldLayer) return;
        orig(main, flat);
    }
}
