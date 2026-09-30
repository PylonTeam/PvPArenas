using System;
using System.Reflection.Metadata;
using S = PvPArenas.Common.Game.ArenaBoundarySettings;

[assembly: MetadataUpdateHandler(typeof(PvPArenas.Common.Game.ArenaBoundaryDrawer))]

namespace PvPArenas.Common.Game;

/// <summary>Local copy of ErkySSC's gradient renderer, with Arenas-owned settings and textures.</summary>
internal static class ArenaBoundaryDrawer
{
    private const float TileSize = 16f;
    private static readonly Color BoundaryColor = new(255, 80, 80);
    private static int MaskSize => Math.Clamp(S.GlowExtentPixels, 1, 1024);
    private static float CutoffWidth => Math.Max(1, S.GlowCutoffWidthPixels);
    private static float PixelStep => Math.Max(1, S.PixelStepPixels);
    private static int maskVersion = -1;
    private static int graphicsThreadId;
    private static Texture2D horizontalGlowMask, verticalGlowMask, cornerGlowMask;
    private static Texture2D horizontalHighlightMask, verticalHighlightMask, cornerHighlightMask;

    public static void ClearCache(Type[] _) => maskVersion = -1;

    internal static void Draw(SpriteBatch spriteBatch, Rectangle tileArea)
    {
        if (Main.dedServ || Main.gameMenu || tileArea.Width <= 0 || tileArea.Height <= 0)
            return;

        S.Update();
        DrawPixelBorder(spriteBatch, GetScreenRect(tileArea), BoundaryColor, .98f);
    }

    private static Rectangle GetScreenRect(Rectangle area)
    {
        Vector2 screenTopLeft = new(area.Left * TileSize, area.Top * TileSize);
        Vector2 screenBottomRight = new(area.Right * TileSize, area.Bottom * TileSize);

        screenTopLeft -= Main.screenPosition;
        screenBottomRight -= Main.screenPosition;

        Rectangle rect = new(
            (int)Math.Floor(screenTopLeft.X),
            (int)Math.Floor(screenTopLeft.Y),
            (int)Math.Round(screenBottomRight.X - screenTopLeft.X),
            (int)Math.Round(screenBottomRight.Y - screenTopLeft.Y));

        return rect;
    }

    // Region box mask.
    private static void DrawPixelBorder(SpriteBatch sb, Rectangle boundary, Color color, float opacity)
    {
        Color glowColor = Color.Lerp(color, S.GlowTint, Saturate(S.GlowTintLerp));
        Color rimColor = Color.Lerp(glowColor, S.HighlightTint, Saturate(S.HighlightTintLerp));
        int depth = Math.Min(MaskSize, Math.Min(boundary.Width, boundary.Height) / 2);

        if (depth <= 0)
            return;

        EnsureGradientMasks();

        // The saturated layer supplies both the dense inner glow and its long tail. The second,
        // color-tinted highlight layer blends the bright rim into it instead of jumping from
        // pure white directly to a faint team color.
        if (S.DrawGlow) DrawNineSliceMask(sb, boundary, depth,
            horizontalGlowMask, verticalGlowMask, cornerGlowMask,
            glowColor * opacity);
        if (S.DrawHighlight) DrawNineSliceMask(sb, boundary, depth,
            horizontalHighlightMask, verticalHighlightMask, cornerHighlightMask,
            rimColor * opacity);
    }

    private static void EnsureGradientMasks()
    {
        graphicsThreadId = Environment.CurrentManagedThreadId;

        if (horizontalGlowMask is { IsDisposed: false } && maskVersion == S.Version)
            return;

        ClearGradientMasks();
        maskVersion = S.Version;
        GraphicsDevice graphicsDevice = Main.graphics.GraphicsDevice;
        horizontalGlowMask = CreateHorizontalMask(graphicsDevice, EvaluateGlowOpacity);
        verticalGlowMask = CreateVerticalMask(graphicsDevice, EvaluateGlowOpacity);
        cornerGlowMask = CreateCornerMask(graphicsDevice, EvaluateGlowOpacity);
        horizontalHighlightMask = CreateHorizontalMask(graphicsDevice, EvaluateHighlightOpacity);
        verticalHighlightMask = CreateVerticalMask(graphicsDevice, EvaluateHighlightOpacity);
        cornerHighlightMask = CreateCornerMask(graphicsDevice, EvaluateHighlightOpacity);
    }

    private static Texture2D CreateHorizontalMask(GraphicsDevice graphicsDevice, Func<float, float> opacityFunction)
    {
        Texture2D texture = new(graphicsDevice, 1, MaskSize, false, SurfaceFormat.Color);
        Color[] data = new Color[MaskSize];

        for (int y = 0; y < data.Length; y++)
            data[y] = PremultipliedMask(opacityFunction(y + 0.5f));

        texture.SetData(data);
        return texture;
    }

    private static Texture2D CreateVerticalMask(GraphicsDevice graphicsDevice, Func<float, float> opacityFunction)
    {
        Texture2D texture = new(graphicsDevice, MaskSize, 1, false, SurfaceFormat.Color);
        Color[] data = new Color[MaskSize];

        for (int x = 0; x < data.Length; x++)
            data[x] = PremultipliedMask(opacityFunction(x + 0.5f));

        texture.SetData(data);
        return texture;
    }

    private static Texture2D CreateCornerMask(GraphicsDevice graphicsDevice, Func<float, float> opacityFunction)
    {
        Texture2D texture = new(graphicsDevice, MaskSize, MaskSize, false, SurfaceFormat.Color);
        Color[] data = new Color[MaskSize * MaskSize];

        for (int y = 0; y < MaskSize; y++)
        {
            for (int x = 0; x < MaskSize; x++)
            {
                float sampleX = x + 0.5f;
                float sampleY = y + 0.5f;
                float pixelatedDistance = RoundedCornerInsideDistance(
                    QuantizeCoordinate(sampleX), QuantizeCoordinate(sampleY));
                float smoothDistance = RoundedCornerInsideDistance(sampleX, sampleY);

                if (pixelatedDistance < 0f)
                {
                    data[y * MaskSize + x] = Color.Transparent;
                    continue;
                }

                // Close to the exterior corner, the rounded-rectangle distance keeps the visible
                // rim on its pixelated arc. Deeper inside, two perpendicular edge fields are joined
                // with a fourth-order norm. Unlike min(x, y), this has no diagonal derivative crease,
                // and unlike additive blending it does not turn the corner into a solid color block.
                float arcDistance = Math.Max(0.5f, smoothDistance);
                float arcOpacity = opacityFunction(arcDistance);
                float edgeOpacity = CombinePerpendicularOpacities(
                    opacityFunction(sampleX), opacityFunction(sampleY));
                float edgeBlend = SmootherStep(MathF.Pow(Saturate(arcDistance / Math.Max(.01f, S.CornerBlendDistance)), Math.Max(.01f, S.CornerBlendPower)));
                float opacity = MathHelper.Lerp(arcOpacity, edgeOpacity, edgeBlend);
                data[y * MaskSize + x] = PremultipliedMask(opacity);
            }
        }

        texture.SetData(data);
        return texture;
    }

    private static float RoundedCornerInsideDistance(float x, float y)
    {
        // Outside the roundover's square, the nearest boundary is simply the nearest straight
        // edge. Inside it, this is the signed distance to the quarter-circle. Using the same
        // distance for the rim and both glow regions guarantees that the diagonal is continuous.
        if (x >= S.CornerRadiusPixels || y >= S.CornerRadiusPixels)
            return Math.Min(x, y);

        float fromCenterX = S.CornerRadiusPixels - x;
        float fromCenterY = S.CornerRadiusPixels - y;
        return S.CornerRadiusPixels - MathF.Sqrt(fromCenterX * fromCenterX + fromCenterY * fromCenterY);
    }

    private static float QuantizeCoordinate(float coordinate) =>
        MathF.Floor(coordinate / PixelStep) * PixelStep + PixelStep * 0.5f;

    private static float EvaluateGlowOpacity(float distance)
    {
        if (distance < 0f || distance >= MaskSize)
            return 0f;

        // The narrow Gaussian supplies the substantial color close to the wall, while the wider,
        // lower-energy Gaussian makes the reference's soft tail. Their sum falls sooner than the
        // old piecewise plateau but has no slope changes for the eye to pick out as bands.
        float nearGlow = S.NearGlowStrength * Gaussian(distance, S.NearGlowSigmaPixels);
        float farGlow = S.FarGlowStrength * Gaussian(distance, S.FarGlowSigmaPixels);
        return (nearGlow + farGlow) * EvaluateCutoff(distance);
    }

    private static float EvaluateHighlightOpacity(float distance)
    {
        if (distance < 0f)
            return 0f;

        // The Gaussian blends the thin bright rim into the colored glow.
        float adjustedDistance = Math.Max(0f, distance - S.RimThicknessPixels * S.RimOffsetFactor);
        return S.RimStrength * Gaussian(adjustedDistance, S.RimSigmaPixels) * EvaluateCutoff(distance);
    }

    private static float Gaussian(float distance, float sigma)
    {
        float normalizedDistance = distance / Math.Max(.01f, sigma);
        return MathF.Exp(-Math.Max(0f, S.GaussianFalloff) * MathF.Pow(normalizedDistance, Math.Max(.01f, S.GradientPower)));
    }

    private static float EvaluateCutoff(float distance)
    {
        float cutoffStart = MaskSize - CutoffWidth;
        float cutoffProgress = Saturate((distance - cutoffStart) / CutoffWidth);
        return 1f - SmootherStep(cutoffProgress);
    }

    private static float CombinePerpendicularOpacities(float horizontal, float vertical)
    {
        // A fourth-order norm is a smooth approximation of max(horizontal, vertical). It rounds
        // equal-energy contours at the inner corner, but adds only about 19% there instead of the
        // large brightness increase produced by ordinary alpha/additive unions.
        float power = Math.Max(1f, S.CornerNormPower);
        float sum = MathF.Pow(horizontal, power) + MathF.Pow(vertical, power);
        return Saturate(MathF.Pow(sum, 1f / power));
    }

    private static float SmootherStep(float value)
    {
        value = Saturate(value);
        return value * value * value * (value * (value * 6f - 15f) + 10f);
    }

    private static float Saturate(float value) => Math.Clamp(value, 0f, 1f);

    private static Color PremultipliedMask(float opacity)
    {
        byte value = (byte)MathF.Round(Saturate(opacity) * byte.MaxValue);
        return new Color(value, value, value, value);
    }

    private static void DrawNineSliceMask(SpriteBatch sb, Rectangle boundary, int depth,
        Texture2D horizontalMask, Texture2D verticalMask, Texture2D cornerMask, Color color)
    {
        // Always sample the complete mask, even when a small box forces a shallower destination.
        // Cropping the source at `depth` used to discard the transparent end of the falloff and
        // expose a hard, opaque L at the inner edge. Scaling the full mask preserves a zero-alpha,
        // zero-slope endpoint for every supported spawn-box size.
        Rectangle horizontalSource = new(0, 0, 1, MaskSize);
        Rectangle verticalSource = new(0, 0, MaskSize, 1);
        Rectangle cornerSource = new(0, 0, MaskSize, MaskSize);
        int horizontalLength = boundary.Width - depth * 2;
        int verticalLength = boundary.Height - depth * 2;

        if (horizontalLength > 0)
        {
            sb.Draw(horizontalMask, new Rectangle(boundary.Left + depth, boundary.Top, horizontalLength, depth),
                horizontalSource, color, 0f, Vector2.Zero, SpriteEffects.None, 0f);
            sb.Draw(horizontalMask, new Rectangle(boundary.Left + depth, boundary.Bottom - depth, horizontalLength, depth),
                horizontalSource, color, 0f, Vector2.Zero, SpriteEffects.FlipVertically, 0f);
        }

        if (verticalLength > 0)
        {
            sb.Draw(verticalMask, new Rectangle(boundary.Left, boundary.Top + depth, depth, verticalLength),
                verticalSource, color, 0f, Vector2.Zero, SpriteEffects.None, 0f);
            sb.Draw(verticalMask, new Rectangle(boundary.Right - depth, boundary.Top + depth, depth, verticalLength),
                verticalSource, color, 0f, Vector2.Zero, SpriteEffects.FlipHorizontally, 0f);
        }

        sb.Draw(cornerMask, new Rectangle(boundary.Left, boundary.Top, depth, depth),
            cornerSource, color, 0f, Vector2.Zero, SpriteEffects.None, 0f);
        sb.Draw(cornerMask, new Rectangle(boundary.Right - depth, boundary.Top, depth, depth),
            cornerSource, color, 0f, Vector2.Zero, SpriteEffects.FlipHorizontally, 0f);
        sb.Draw(cornerMask, new Rectangle(boundary.Left, boundary.Bottom - depth, depth, depth),
            cornerSource, color, 0f, Vector2.Zero, SpriteEffects.FlipVertically, 0f);
        sb.Draw(cornerMask, new Rectangle(boundary.Right - depth, boundary.Bottom - depth, depth, depth),
            cornerSource, color, 0f, Vector2.Zero, SpriteEffects.FlipHorizontally | SpriteEffects.FlipVertically, 0f);
    }

    internal static void ClearGradientMasks()
    {
        Texture2D[] masks =
        [
            horizontalGlowMask,
            verticalGlowMask,
            cornerGlowMask,
            horizontalHighlightMask,
            verticalHighlightMask,
            cornerHighlightMask
        ];

        horizontalGlowMask = null;
        verticalGlowMask = null;
        cornerGlowMask = null;
        horizontalHighlightMask = null;
        verticalHighlightMask = null;
        cornerHighlightMask = null;

        void DisposeMasks()
        {
            foreach (Texture2D mask in masks)
                if (mask is { IsDisposed: false })
                    mask.Dispose();
        }

        if (Environment.CurrentManagedThreadId == graphicsThreadId)
            DisposeMasks();
        else
            Main.QueueMainThreadAction(DisposeMasks);
    }
}
