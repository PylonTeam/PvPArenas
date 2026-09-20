using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PvPFramework.Core.Utilities;
using ReLogic.Graphics;
using System;
using System.Collections.Generic;
using Terraria.Enums;
using Terraria.GameContent;
using Terraria.Map;
using Terraria.UI;

namespace PvPArenas.Common.Game;

internal static class ArenaSpawnBoxes
{
    internal const int Thickness = 1;
    private const int TileSize = 16;

    internal static bool Enabled
    {
        get
        {
            RoundManager manager = ModContent.GetInstance<RoundManager>();
            return manager.CurrentLayout != null
                && manager.CurrentPhase is RoundManager.RoundPhase.FreezeCountdown
                    or RoundManager.RoundPhase.Playing;
        }
    }

    internal static Rectangle RedTileArea => Enabled
        ? ModContent.GetInstance<RoundManager>().CurrentLayout.RedSpawnBox
        : Rectangle.Empty;

    internal static Rectangle BlueTileArea => Enabled
        ? ModContent.GetInstance<RoundManager>().CurrentLayout.BlueSpawnBox
        : Rectangle.Empty;

    internal static Rectangle[] TileAreas => Enabled ? [RedTileArea, BlueTileArea] : [];

    internal static Rectangle BorderOuterTileArea(Rectangle area)
    {
        area.Inflate(Thickness, Thickness);
        return area;
    }

    internal static Rectangle[] BorderTileAreas(Rectangle inner)
    {
        Rectangle outer = BorderOuterTileArea(inner);
        int t = Thickness;
        return
        [
            new Rectangle(outer.X, outer.Y, outer.Width, t),
            new Rectangle(outer.X, inner.Bottom, outer.Width, t),
            new Rectangle(outer.X, inner.Y, t, inner.Height),
            new Rectangle(inner.Right, inner.Y, t, inner.Height)
        ];
    }

    internal static Rectangle[] BorderWorldAreas(Rectangle area)
    {
        Rectangle[] tiles = BorderTileAreas(area);
        for (int i = 0; i < tiles.Length; i++)
            tiles[i] = TileToWorld(tiles[i]);
        return tiles;
    }

    internal static bool ContainsTile(int x, int y) =>
        RedTileArea.Contains(x, y) || BlueTileArea.Contains(x, y);

    internal static bool TouchesWorldHitbox(Rectangle hitbox)
    {
        Rectangle tiles = hitbox.ToTileRectangle();
        return RedTileArea.Intersects(tiles) || BlueTileArea.Intersects(tiles);
    }

    internal static bool TouchesTileRectangle(Rectangle tiles) =>
        RedTileArea.Intersects(tiles) || BlueTileArea.Intersects(tiles);

    internal static Rectangle TileToWorld(Rectangle tiles) =>
        new(tiles.X * TileSize, tiles.Y * TileSize, tiles.Width * TileSize, tiles.Height * TileSize);
}

[Autoload(Side = ModSide.Client)]
internal sealed class ArenaSpawnBoxWorld : ModSystem
{
    private const float TileSize = 16f;

    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        int index = layers.FindIndex(layer => layer.Name == "Vanilla: Interface Logic 1");
        if (index != -1)
            layers.Insert(index + 1, new SpawnBoxInterfaceLayer());
    }

    private sealed class SpawnBoxInterfaceLayer()
        : GameInterfaceLayer("Arenas: Team Spawn Boxes", InterfaceScaleType.Game)
    {
        private static readonly Color ArenaColor = new(255, 196, 72);
        private static readonly Color BossColor = new(255, 132, 24);

        protected override bool DrawSelf()
        {
            if (!ArenaSpawnBoxes.Enabled)
                return true;

            RoundManager manager = ModContent.GetInstance<RoundManager>();
            DrawAreaBorder(Main.spriteBatch, manager.CurrentLayout.ArenaBounds,
                ArenaColor, .55f, ArenaSpawnBoxes.Thickness * (int)TileSize);
            DrawAreaBorder(Main.spriteBatch, manager.CurrentLayout.BossBounds,
                BossColor, 1f, ArenaSpawnBoxes.Thickness * (int)TileSize);
            DrawAreaBorder(Main.spriteBatch, ArenaSpawnBoxes.RedTileArea,
                Main.teamColor[(int)Team.Red], .88f, ArenaSpawnBoxes.Thickness * (int)TileSize);
            DrawAreaBorder(Main.spriteBatch, ArenaSpawnBoxes.BlueTileArea,
                Main.teamColor[(int)Team.Blue], .88f, ArenaSpawnBoxes.Thickness * (int)TileSize);
            return true;
        }

        private static void DrawAreaBorder(SpriteBatch spriteBatch, Rectangle tileArea,
            Color color, float opacity, int thickness)
        {
            if (Main.dedServ || Main.gameMenu || tileArea.Width <= 0 || tileArea.Height <= 0)
                return;

            Rectangle inner = GetScreenRect(tileArea);
            if (inner.Width <= 0 || inner.Height <= 0 || thickness <= 0)
                return;

            if (global::PvPArenas.Core.Utilities.EffectLoader.TryGetSpawnBoxBorderEffect(out Effect effect))
                DrawShaderBorder(spriteBatch, inner, thickness, effect, color, opacity);
            else
                DrawPixelBorder(spriteBatch, inner, thickness, color * opacity);
        }

        private static Rectangle GetScreenRect(Rectangle area)
        {
            Vector2 screenTopLeft = new(area.Left * TileSize, area.Top * TileSize);
            Vector2 screenBottomRight = new(area.Right * TileSize, area.Bottom * TileSize);
            screenTopLeft -= Main.screenPosition;
            screenBottomRight -= Main.screenPosition;

            return new Rectangle(
                (int)Math.Floor(screenTopLeft.X),
                (int)Math.Floor(screenTopLeft.Y),
                (int)Math.Round(screenBottomRight.X - screenTopLeft.X),
                (int)Math.Round(screenBottomRight.Y - screenTopLeft.Y));
        }

        private static void DrawShaderBorder(SpriteBatch sb, Rectangle inner, int thickness,
            Effect effect, Color color, float opacity)
        {
            Rectangle outer = inner;
            outer.Inflate(thickness, thickness);
            if (outer.Width <= 0 || outer.Height <= 0)
                return;

            effect.Parameters["globalTime"]?.SetValue((float)Main.timeForVisualEffects);
            effect.Parameters["borderColor"]?.SetValue(color.ToVector3());
            effect.Parameters["opacity"]?.SetValue(opacity);
            effect.Parameters["borderSize"]?.SetValue(new Vector2(
                thickness / (float)outer.Width, thickness / (float)outer.Height));
            effect.Parameters["outerEdgeFade"]?.SetValue(.42f);
            effect.Parameters["innerEdgeFade"]?.SetValue(.30f);
            effect.Parameters["pulseStrength"]?.SetValue(.12f);
            effect.Parameters["shimmerStrength"]?.SetValue(.08f);
            effect.Parameters["shimmerScale"]?.SetValue(34f);
            effect.Parameters["shimmerSpeed"]?.SetValue(.07f);

            sb.End();
            sb.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullNone, effect,
                Main.GameViewMatrix.TransformationMatrix);
            effect.CurrentTechnique.Passes[0].Apply();
            sb.Draw(TextureAssets.MagicPixel.Value, outer, Color.White);
            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState,
                DepthStencilState.None, Main.Rasterizer, null, Main.UIScaleMatrix);
        }

        private static void DrawPixelBorder(SpriteBatch sb, Rectangle inner, int thickness, Color color)
        {
            Texture2D pixel = TextureAssets.MagicPixel.Value;
            sb.Draw(pixel, new Rectangle(inner.X - thickness, inner.Y - thickness,
                inner.Width + thickness * 2, thickness), color);
            sb.Draw(pixel, new Rectangle(inner.X - thickness, inner.Bottom,
                inner.Width + thickness * 2, thickness), color);
            sb.Draw(pixel, new Rectangle(inner.X - thickness, inner.Y, thickness, inner.Height), color);
            sb.Draw(pixel, new Rectangle(inner.Right, inner.Y, thickness, inner.Height), color);
        }
    }
}

internal sealed class ArenaSpawnBoxMap : ModMapLayer
{
    private const float MinimapSpawnLabelScale = .42f;
    private const float MinimapArenaLabelScale = .52f;

    public override void Draw(
        ref MapOverlayDrawContext context,
        ref string text)
    {
        // mapFullscreenScale is irrelevant while drawing the minimap.
        if (Main.mapFullscreen &&
            Main.mapFullscreenScale < .5f)
        {
            return;
        }

        DrawArenaAreas(ref context);

        DrawBox(
            ref context,
            ArenaSpawnBoxes.RedTileArea,
            Team.Red,
            ref text);

        DrawBox(
            ref context,
            ArenaSpawnBoxes.BlueTileArea,
            Team.Blue,
            ref text);
    }

    private static void DrawArenaAreas(
        ref MapOverlayDrawContext context)
    {
        if (!ArenaSpawnBoxes.Enabled)
            return;

        RoundManager manager =
            ModContent.GetInstance<RoundManager>();

        DrawMapArea(
            ref context,
            manager.CurrentLayout.ArenaBounds,
            new Color(255, 196, 72) * .55f,
            2);

        DrawMapArea(
            ref context,
            manager.CurrentLayout.BossBounds,
            new Color(255, 132, 24),
            2);

        DrawArenaLabel(
            ref context,
            manager.CurrentLayout.ArenaBounds);
    }

    private static void DrawArenaLabel(
        ref MapOverlayDrawContext context,
        Rectangle tileArea)
    {
        if (tileArea.Width <= 0 ||
            tileArea.Height <= 0)
        {
            return;
        }

        const string label = "Arena";

        Rectangle mapRectangle =
            ToMapRectangle(
                ref context,
                tileArea);

        // Do not draw the label when its arena is entirely outside the
        // visible minimap.
        if (!IntersectsVisibleMap(mapRectangle))
            return;

        DynamicSpriteFont font =
            FontAssets.DeathText.Value;

        float scale = Main.mapFullscreen
            ? .25f
            : MinimapArenaLabelScale;

        if (scale <= 0f)
            return;

        Vector2 textSize =
            font.MeasureString(label) * scale;

        Vector2 position = new(
            mapRectangle.Center.X -
                textSize.X / 2f,
            mapRectangle.Top -
                textSize.Y -
                GetLabelGap(
                    context.DrawScale,
                    fullscreenGap: 6f,
                    minimapGap: 3f));

        Rectangle textBounds =
            CreateTextBounds(
                position,
                textSize,
                shadowPadding: 3);

        // SpriteBatch text is not automatically clipped by Terraria's
        // minimap. Cull labels that would leak beyond its bounds.
        if (!LabelFitsVisibleMap(textBounds))
            return;

        Main.spriteBatch.DrawString(
            font,
            label,
            position + new Vector2(3f, 3f),
            Color.Black * .9f,
            0f,
            Vector2.Zero,
            scale,
            SpriteEffects.None,
            0f);

        Main.spriteBatch.DrawString(
            font,
            label,
            position,
            new Color(255, 196, 72),
            0f,
            Vector2.Zero,
            scale,
            SpriteEffects.None,
            0f);
    }

    private static void DrawMapArea(
        ref MapOverlayDrawContext context,
        Rectangle tileArea,
        Color color,
        int thickness)
    {
        Rectangle rectangle =
            ToMapRectangle(
                ref context,
                tileArea);

        if (!ClipToVisibleMap(ref rectangle))
            return;

        DrawBorder(
            rectangle,
            color,
            thickness);
    }

    private static void DrawBox(
        ref MapOverlayDrawContext context,
        Rectangle tileArea,
        Team team,
        ref string hoverText)
    {
        if (tileArea.Width <= 0 ||
            tileArea.Height <= 0)
        {
            return;
        }

        Rectangle area =
            ArenaSpawnBoxes.BorderOuterTileArea(
                tileArea);

        // Keep the unmodified rectangle for label positioning.
        Rectangle mapRectangle =
            ToMapRectangle(
                ref context,
                area);

        Rectangle visibleRectangle =
            mapRectangle;

        if (!ClipToVisibleMap(
                ref visibleRectangle))
        {
            return;
        }

        Color teamColor =
            Main.teamColor[(int)team];

        Color borderColor =
            teamColor * .88f;

        int thickness = Main.mapFullscreen
            ? Math.Max(
                1,
                (int)Main.mapFullscreenScale)
            : 2;

        DrawBorder(
            visibleRectangle,
            borderColor,
            thickness);

        string label =
            $"{team} Team Spawn";

        DrawLabel(
            ref context,
            mapRectangle,
            label,
            teamColor);

        if (visibleRectangle.Contains(
                Main.MouseScreen.ToPoint()))
        {
            hoverText = label;
        }
    }

    private static void DrawLabel(
        ref MapOverlayDrawContext context,
        Rectangle mapRectangle,
        string label,
        Color color)
    {
        if (!IntersectsVisibleMap(mapRectangle))
            return;

        DynamicSpriteFont font =
            FontAssets.DeathText.Value;

        float scale = Main.mapFullscreen
            ? .25f
            : MinimapSpawnLabelScale;

        if (scale <= 0f)
            return;

        Vector2 textSize =
            font.MeasureString(label) * scale;

        Vector2 position = new(
            mapRectangle.Center.X -
                textSize.X / 2f,
            mapRectangle.Top -
                textSize.Y -
                GetLabelGap(
                    context.DrawScale,
                    fullscreenGap: 3f,
                    minimapGap: 2f));

        Rectangle textBounds =
            CreateTextBounds(
                position,
                textSize,
                shadowPadding: 1);

        if (!LabelFitsVisibleMap(textBounds))
            return;

        Main.spriteBatch.DrawString(
            font,
            label,
            position + Vector2.One,
            Color.Black * .8f,
            0f,
            Vector2.Zero,
            scale,
            SpriteEffects.None,
            0f);

        Main.spriteBatch.DrawString(
            font,
            label,
            position,
            color,
            0f,
            Vector2.Zero,
            scale,
            SpriteEffects.None,
            0f);
    }

    private static Rectangle ToMapRectangle(
        ref MapOverlayDrawContext context,
        Rectangle area)
    {
        Vector2 topLeft =
            (new Vector2(area.X, area.Y) -
                context.MapPosition) *
            context.MapScale +
            context.MapOffset;

        Vector2 size = new(
            area.Width * context.MapScale,
            area.Height * context.MapScale);

        return new Rectangle(
            (int)MathF.Floor(topLeft.X),
            (int)MathF.Floor(topLeft.Y),
            Math.Max(
                1,
                (int)MathF.Ceiling(size.X)),
            Math.Max(
                1,
                (int)MathF.Ceiling(size.Y)));
    }

    private static bool ClipToVisibleMap(
        ref Rectangle rectangle)
    {
        if (TryGetMinimapBounds(
                out Rectangle minimap))
        {
            rectangle =
                Rectangle.Intersect(
                    rectangle,
                    minimap);
        }

        return rectangle.Width > 0 &&
            rectangle.Height > 0;
    }

    private static bool IntersectsVisibleMap(
        Rectangle rectangle)
    {
        if (!TryGetMinimapBounds(
                out Rectangle minimap))
        {
            return true;
        }

        return minimap.Intersects(rectangle);
    }

    private static bool LabelFitsVisibleMap(
        Rectangle textBounds)
    {
        if (!TryGetMinimapBounds(
                out Rectangle minimap))
        {
            return true;
        }

        // Text rendering is not scissored by the minimap. Completely hide
        // labels that would extend outside it.
        return textBounds.Left >= minimap.Left &&
            textBounds.Top >= minimap.Top &&
            textBounds.Right <= minimap.Right &&
            textBounds.Bottom <= minimap.Bottom;
    }

    private static bool TryGetMinimapBounds(
        out Rectangle minimap)
    {
        if (!Main.mapFullscreen &&
            Main.mapStyle == 1 &&
            Main.miniMapWidth > 0 &&
            Main.miniMapHeight > 0)
        {
            minimap = new Rectangle(
                Main.miniMapX,
                Main.miniMapY,
                Main.miniMapWidth,
                Main.miniMapHeight);

            return true;
        }

        minimap = Rectangle.Empty;
        return false;
    }

    private static Rectangle CreateTextBounds(
        Vector2 position,
        Vector2 size,
        int shadowPadding)
    {
        return new Rectangle(
            (int)MathF.Floor(position.X),
            (int)MathF.Floor(position.Y),
            Math.Max(
                1,
                (int)MathF.Ceiling(size.X) +
                shadowPadding),
            Math.Max(
                1,
                (int)MathF.Ceiling(size.Y) +
                shadowPadding));
    }

    private static float GetLabelGap(
        float drawScale,
        float fullscreenGap,
        float minimapGap)
    {
        return Main.mapFullscreen
            ? Math.Max(
                fullscreenGap - 1f,
                fullscreenGap * drawScale)
            : minimapGap;
    }

    private static void DrawBorder(
        Rectangle rectangle,
        Color color,
        int thickness)
    {
        thickness =
            Math.Max(1, thickness);

        if (rectangle.Width <=
                thickness * 2 ||
            rectangle.Height <=
                thickness * 2)
        {
            return;
        }

        Texture2D pixel =
            TextureAssets.MagicPixel.Value;

        Main.spriteBatch.Draw(
            pixel,
            new Rectangle(
                rectangle.X + thickness,
                rectangle.Y,
                rectangle.Width -
                    thickness * 2,
                thickness),
            color);

        Main.spriteBatch.Draw(
            pixel,
            new Rectangle(
                rectangle.X + thickness,
                rectangle.Bottom - thickness,
                rectangle.Width -
                    thickness * 2,
                thickness),
            color);

        Main.spriteBatch.Draw(
            pixel,
            new Rectangle(
                rectangle.X,
                rectangle.Y,
                thickness,
                rectangle.Height),
            color);

        Main.spriteBatch.Draw(
            pixel,
            new Rectangle(
                rectangle.Right - thickness,
                rectangle.Y,
                thickness,
                rectangle.Height),
            color);
    }
}
