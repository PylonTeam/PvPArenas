using Microsoft.Xna.Framework;
using ReLogic.Graphics;
using System;

namespace PvPArenas.Common.Game;

internal readonly record struct LoadoutTextArgs(DynamicSpriteFont Font, string Text, Rectangle Area,
    float PreferredScale, Vector2 Offset, float Padding = 2f);

internal readonly record struct LoadoutTextPlacement(Vector2 Position, float Scale);

internal readonly record struct LoadoutStatTextArgs(DynamicSpriteFont Font, string Text, Rectangle Area,
    Vector2 IconSize, int IconHeight, float PreferredTextScale, float LayoutScale, LoadoutUISettings Settings);

internal readonly record struct LoadoutStatPlacement(Vector2 IconCenter, float IconScale,
    Vector2 TextPosition, float TextScale);

/// <summary>Place text by rendered glyph rectangles rather than the font's blank line spacing.</summary>
internal static class LoadoutUIText
{
    // X/Y are the first visible glyph edge; Z/W are width/height. Matches native kerning advances.
    internal static Vector4 GlyphBounds(DynamicSpriteFont font, string text)
    {
        float x = 0, y = 0, left = float.MaxValue, top = float.MaxValue;
        float right = float.MinValue, bottom = float.MinValue;
        bool first = true;
        foreach (char character in text ?? "")
        {
            if (character == '\r') continue;
            if (character == '\n') { x = 0; y += font.LineSpacing; first = true; continue; }
            var glyph = font.SpriteCharacters.TryGetValue(character, out var value) ? value : font.DefaultCharacterData;
            x += first ? Math.Max(0, glyph.Kerning.X) : font.CharacterSpacing + glyph.Kerning.X;
            if (!char.IsWhiteSpace(character) && glyph.Glyph.Width > 0 && glyph.Glyph.Height > 0)
            {
                float gx = x + glyph.Padding.X, gy = y + glyph.Padding.Y;
                left = Math.Min(left, gx); top = Math.Min(top, gy);
                right = Math.Max(right, gx + glyph.Glyph.Width); bottom = Math.Max(bottom, gy + glyph.Glyph.Height);
            }
            x += glyph.Kerning.Y + glyph.Kerning.Z;
            first = false;
        }
        return left == float.MaxValue ? Vector4.Zero : new(left, top, right - left, bottom - top);
    }

    internal static LoadoutTextPlacement Centered(LoadoutTextArgs args)
    {
        Vector4 ink = GlyphBounds(args.Font, args.Text);
        float scale = Math.Max(0, Math.Min(args.PreferredScale,
            Math.Min(Math.Max(0, args.Area.Width - args.Padding * 2) / Math.Max(1, ink.Z),
                Math.Max(0, args.Area.Height - args.Padding * 2) / Math.Max(1, ink.W))));
        Vector2 center = new(args.Area.X + args.Area.Width / 2f, args.Area.Y + args.Area.Height / 2f);
        return new(center + args.Offset - new Vector2(ink.X + ink.Z / 2f, ink.Y + ink.W / 2f) * scale, scale);
    }

    internal static LoadoutStatPlacement Stat(LoadoutStatTextArgs args)
    {
        LoadoutUISettings ui = args.Settings;
        Vector4 ink = GlyphBounds(args.Font, args.Text);
        float padding = Math.Max(0, ui.HealthManaTextPadding * args.LayoutScale);
        float iconHeight = Math.Max(1, Math.Min(args.IconHeight * args.LayoutScale, args.Area.Height - padding * 2));
        float iconScale = iconHeight / Math.Max(1, args.IconSize.Y);
        float iconWidth = args.IconSize.X * iconScale;
        float gap = Math.Max(0, ui.HealthManaIconTextGap * args.LayoutScale);
        float scale = Math.Max(0, Math.Min(args.PreferredTextScale * args.LayoutScale,
            Math.Min(Math.Max(0, args.Area.Width - padding * 2 - iconWidth - gap) / Math.Max(1, ink.Z),
                Math.Max(0, args.Area.Height - padding * 2) / Math.Max(1, ink.W))));
        float groupWidth = iconWidth + gap + ink.Z * scale;
        float x = args.Area.X + (args.Area.Width - groupWidth) / 2f;
        float cy = args.Area.Y + args.Area.Height / 2f;
        return new(new(x + iconWidth / 2f, cy), iconScale,
            new(x + iconWidth + gap - ink.X * scale,
                cy - (ink.Y + ink.W / 2f) * scale + ui.HealthManaTextYOffset * args.LayoutScale), scale);
    }
}
