using System.Collections.Generic;
using Terraria.UI;

namespace PvPArenas.Common.Game;

[Autoload(Side = ModSide.Client)]
internal sealed class ArenaBoundary : ModSystem
{
    public override void Unload() => ArenaBoundaryDrawer.ClearGradientMasks();

    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        int index = layers.FindIndex(layer => layer.Name == "Vanilla: Interface Logic 1");
        if (index != -1)
            layers.Insert(index + 1, new BoundaryInterfaceLayer());
    }

    private sealed class BoundaryInterfaceLayer()
        : GameInterfaceLayer("Arenas: Boundary", InterfaceScaleType.Game)
    {
        protected override bool DrawSelf()
        {
            RoundManager manager = ModContent.GetInstance<RoundManager>();
            if (manager.CurrentLayout != null
                && manager.CurrentPhase is RoundManager.RoundPhase.FreezeCountdown
                    or RoundManager.RoundPhase.Playing)
            {
                ArenaBoundaryDrawer.Draw(Main.spriteBatch, manager.CurrentLayout.ArenaBounds);
            }

            return true;
        }
    }
}
