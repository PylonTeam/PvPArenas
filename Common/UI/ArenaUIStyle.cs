using System;

namespace PvPArenas.Common.UI;

internal static class ArenaUIStyle
{
    internal static readonly Color PanelFill = new(25, 34, 66);
    internal static readonly Color PanelEdge = new(81, 99, 151);
    internal static readonly Color CardFill = new(33, 44, 78);
    internal static readonly Color CardHover = new(52, 67, 108);
    internal static readonly Color Title = new(246, 216, 72);
    internal static readonly Color Accent = new(153, 218, 158);
    internal static readonly Color Progress = new(149, 175, 220);

    internal static float Ease(float t) => t * t * (3f - 2f * t);

    internal static float HoverBlend(float seconds) => 1f - MathF.Exp(-16f * Math.Max(0f, seconds));

    internal static Color ChoiceFill(float hover, float selection) =>
        Color.Lerp(Color.Lerp(CardFill, CardHover, hover), Accent, .14f * selection);
}
