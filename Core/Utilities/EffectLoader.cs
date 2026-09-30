using System;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.ModLoader;

namespace PvPArenas.Core.Utilities;

public class EffectLoader : ModSystem
{
    private const string LiquidGlassPath = "Arenas/Assets/Effects/LiquidGlass";

    private static Effect liquidGlassEffect;

    public static bool TryGetLiquidGlassEffect(out Effect effect)
    {
        try
        {
            liquidGlassEffect ??= ModContent.Request<Effect>(LiquidGlassPath, AssetRequestMode.ImmediateLoad).Value;
            effect = liquidGlassEffect;
            return effect != null;
        }
        catch (Exception e)
        {
            Log.Warn($"Failed to load liquid glass effect '{LiquidGlassPath}': {e.Message}");
            effect = null;
            return false;
        }
    }

    public override void Unload()
    {
        liquidGlassEffect = null;
    }
}
