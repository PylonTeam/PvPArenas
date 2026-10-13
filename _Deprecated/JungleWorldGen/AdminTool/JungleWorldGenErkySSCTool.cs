#if DEBUG
using ErkySSC.Common.AdminTools;
using PvPArenas.Common.AdminTools.UI;
using ReLogic.Content;

namespace PvPArenas.Common.AdminTools.JungleWorldGen;

[Autoload(Side = ModSide.Client)]
internal sealed class JungleWorldGenErkySSCTool : AdminTool
{
    public override string DisplayName => "Arenas: Jungle World Gen";
    public override string Tooltip => "Configure vanilla underground Jungle passes, generate a protected preview, and export its map and recipe (DEBUG)";
    public override bool ShowInQuickbar => true;
    public override bool ShowInCollapsedQuickbar => false;
    public override bool ShowInRadialBar => false;
    public override int SortOrder => 33;
    public override Asset<Texture2D> Icon => VanillaAdminIcons.MixedSeed.Asset;
    public override bool IsVisible => !Main.dedServ && ModContent.GetInstance<JungleWorldGenUISystem>().IsActive;
    public override void Toggle() => ModContent.GetInstance<JungleWorldGenUISystem>().Toggle();
    public override void Close() => ModContent.GetInstance<JungleWorldGenUISystem>().Close();
    public override void ResetTransientState() => ModContent.GetInstance<JungleWorldGenUISystem>().ResetClient();
}
#endif
