using System.Collections.Generic;

namespace PvPArenas.Common.Game;

/// <summary>Local copy of the active ErkySSC gradient settings; independent of region protection.</summary>
internal static class ArenaBoundarySettings
{
    internal static int Version { get; private set; }

    internal static Color GlowTint, HighlightTint;
    internal static float GlowTintLerp, HighlightTintLerp;
    internal static bool DrawGlow, DrawHighlight;
    internal static int GlowExtentPixels, GlowCutoffWidthPixels, CornerRadiusPixels, PixelStepPixels;
    internal static float NearGlowSigmaPixels, FarGlowSigmaPixels, NearGlowStrength, FarGlowStrength;
    internal static float RimThicknessPixels, RimSigmaPixels, RimStrength, RimOffsetFactor;
    internal static float GaussianFalloff, GradientPower, CornerNormPower, CornerBlendDistance, CornerBlendPower;

    // Update every draw so local style edits also invalidate the cached textures during hot reload.
    internal static void Update()
    {
        Set(ref GlowTint, Color.White);
        Set(ref GlowTintLerp, 0f);
        Set(ref HighlightTint, Color.White);
        Set(ref HighlightTintLerp, .62f);
        Set(ref DrawGlow, true);
        Set(ref DrawHighlight, true);
        Set(ref GlowExtentPixels, 392);
        Set(ref GlowCutoffWidthPixels, 16);
        Set(ref CornerRadiusPixels, 6);
        Set(ref PixelStepPixels, 2);
        Set(ref NearGlowSigmaPixels, 14f);
        Set(ref FarGlowSigmaPixels, 38f);
        Set(ref NearGlowStrength, .14f);
        Set(ref FarGlowStrength, .28f);
        Set(ref RimThicknessPixels, 1.35f);
        Set(ref RimSigmaPixels, 2.4f);
        Set(ref RimStrength, .86f);
        Set(ref RimOffsetFactor, .35f);
        Set(ref GaussianFalloff, .5f);
        Set(ref GradientPower, 2f);
        Set(ref CornerNormPower, 4f);
        Set(ref CornerBlendDistance, 4f);
        Set(ref CornerBlendPower, 1f);
    }

    private static void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Version++;
    }
}
