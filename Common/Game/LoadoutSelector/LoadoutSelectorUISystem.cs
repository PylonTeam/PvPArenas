using System.Collections.Generic;
using Terraria.UI;

namespace PvPArenas.Common.Game.LoadoutSelector;

/// <summary>Owns the loadout selector / preview interface layer shown during the freeze countdown.</summary>
[Autoload(Side = ModSide.Client)]
internal sealed class LoadoutSelectorUISystem : ModSystem
{
    private const int Top = 80;

    public override void OnWorldLoad() => LoadoutPreviewDrawer.Reset();
    public override void OnWorldUnload() => LoadoutPreviewDrawer.Reset();

    public override void UpdateUI(GameTime gameTime)
    {
        if (Main.gameMenu || ModContent.GetInstance<RoundManager>().CurrentPhase != RoundManager.RoundPhase.FreezeCountdown)
        {
            LoadoutPreviewDrawer.Reset();
            return;
        }

        LoadoutPreviewDrawer.Update((float)gameTime.ElapsedGameTime.TotalSeconds);
    }

    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        int index = layers.FindIndex(layer => layer.Name == "Vanilla: Mouse Text");
        if (index >= 0)
            layers.Insert(index, new LegacyGameInterfaceLayer(
                "Arenas: Loadout Selector", Draw, InterfaceScaleType.UI));
    }

    private static bool Draw()
    {
        if (Main.gameMenu)
            return true;

        RoundManager manager = ModContent.GetInstance<RoundManager>();
        if (manager.CurrentPhase == RoundManager.RoundPhase.FreezeCountdown)
            LoadoutPreviewDrawer.Draw(Top);

        return true;
    }
}
