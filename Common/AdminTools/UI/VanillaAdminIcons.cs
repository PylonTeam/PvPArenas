using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using System.Collections.Generic;

namespace PvPArenas.Common.AdminTools.UI;

/// <summary>A vanilla UI texture plus an optional frame from one of Terraria's icon atlases.</summary>
internal readonly record struct AdminUIIcon(Asset<Texture2D> Asset, int Columns = 1, int Rows = 1,
    int Column = 0, int Row = 0)
{
    internal Rectangle Source(Texture2D texture)
    {
        int columns = Math.Max(1, Columns);
        int rows = Math.Max(1, Rows);
        int width = texture.Width / columns;
        int height = texture.Height / rows;
        return new Rectangle(
            Math.Clamp(Column, 0, columns - 1) * width,
            Math.Clamp(Row, 0, rows - 1) * height,
            width,
            height);
    }
}

/// <summary>
/// Lazy vanilla asset catalog for PvP admin UI. This mirrors ErkySSC's fitted icon approach
/// while keeping PvPArenas independent of that optional mod at compile time.
/// </summary>
internal static class VanillaAdminIcons
{
    private static readonly Dictionary<string, Asset<Texture2D>> Cache = [];

    internal static AdminUIIcon PlayPause => UI("IconPlayPause");
    internal static AdminUIIcon Pause => UI("IconMismatchPause");
    internal static AdminUIIcon MixedSeed => UI("IconMixedSeed");
    internal static void DrawFitted(SpriteBatch spriteBatch, AdminUIIcon icon, Rectangle box,
        Color color, bool allowUpscale = false)
    {
        Texture2D texture = icon.Asset?.Value;
        if (texture == null || texture.Width <= 0 || texture.Height <= 0 || box.Width <= 0 || box.Height <= 0)
            return;

        Rectangle source = icon.Source(texture);
        if (source.Width <= 0 || source.Height <= 0)
            return;

        float scale = Math.Min(box.Width / (float)source.Width, box.Height / (float)source.Height);
        if (!allowUpscale)
            scale = Math.Min(scale, 1f);

        Vector2 size = source.Size() * scale;
        Vector2 position = box.Center.ToVector2() - size * .5f;
        spriteBatch.Draw(texture, position, source, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }

    private static AdminUIIcon UI(string path) => Frame(path, 1, 1, 0, 0);

    private static AdminUIIcon Frame(string path, int columns, int rows, int column, int row)
    {
        if (Main.dedServ)
            return default;
        if (!Cache.TryGetValue(path, out Asset<Texture2D> asset))
        {
            asset = Main.Assets.Request<Texture2D>($"Images/UI/{path}");
            Cache[path] = asset;
        }
        return new AdminUIIcon(asset, columns, rows, column, row);
    }

}
