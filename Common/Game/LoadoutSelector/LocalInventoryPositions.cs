using System.Linq;
using PvPArenas.Core.Configs;
using Terraria.ID;
using Terraria.UI;

namespace PvPArenas.Common.Game.LoadoutSelector;

/// <summary>Observes real inventory interactions, never network updates or round inventory clears.</summary>
[Autoload(Side = ModSide.Client)]
internal sealed class LocalInventoryPositions : ModSystem
{
    private static BossFightPreset activePreset;
    private static int activeIndex;
    private static int[] observed;

    public override void Load()
    {
        On_ItemSlot.LeftClick_ItemArray_int_int += LeftClick;
        On_ItemSlot.RightClick_ItemArray_int_int += RightClick;
        On_ItemSorting.SortInventory += Sort;
    }

    public override void PostSetupContent()
    {
        for (int boss = 0; boss < FightPresets.Count; boss++)
        {
            BossFightPreset preset = ModContent.GetInstance<ServerConfig>().GetFightPreset(boss);
            for (int i = 0; i < (preset?.Loadouts?.Count ?? 0); i++)
                LocalLoadoutPositions.Get(preset, i, ArenaPlayer.ResolveBaseLoadout(preset, i));
        }
        LocalLoadoutPositions.Flush(force: true);
    }

    public override void PostUpdateEverything() => LocalLoadoutPositions.Flush();
    public override void OnWorldLoad() => Stop();
    public override void OnWorldUnload() => Stop();
    public override void Unload()
    {
        On_ItemSlot.LeftClick_ItemArray_int_int -= LeftClick;
        On_ItemSlot.RightClick_ItemArray_int_int -= RightClick;
        On_ItemSorting.SortInventory -= Sort;
        Stop();
        LocalLoadoutPositions.Unload();
    }

    internal static void Begin(BossFightPreset preset, int index)
    {
        activePreset = preset;
        activeIndex = index;
        observed = LoadoutSlotLayout.Types(Main.LocalPlayer.inventory);
        LoadoutPreviewDrawer.Invalidate();
    }

    internal static void Stop()
    {
        LocalLoadoutPositions.Flush(force: true);
        activePreset = null;
        observed = null;
    }

    private static void LeftClick(On_ItemSlot.orig_LeftClick_ItemArray_int_int orig, Item[] inv, int context, int slot)
    {
        bool clicked = Main.mouseLeft && Main.mouseLeftRelease;
        orig(inv, context, slot);
        if (clicked) Remember();
    }

    private static void RightClick(On_ItemSlot.orig_RightClick_ItemArray_int_int orig, Item[] inv, int context, int slot)
    {
        bool clicked = Main.mouseRight;
        orig(inv, context, slot);
        if (clicked) Remember();
    }

    private static void Sort(On_ItemSorting.orig_SortInventory orig)
    {
        orig();
        Remember();
    }

    internal static void Remember()
    {
        Player player = Main.LocalPlayer;
        RoundManager manager = ModContent.GetInstance<RoundManager>();
        if (Main.dedServ || Main.netMode == NetmodeID.Server || Main.gameMenu || activePreset == null
            || player.dead || player.ghost || !Main.mouseItem.IsAir
            || manager.CurrentPhase is not (RoundManager.RoundPhase.FreezeCountdown or RoundManager.RoundPhase.Playing)
            || !manager.TryGetSelectedPreset(out BossFightPreset preset) || preset.Boss.Type != activePreset.Boss.Type
            || player.GetModPlayer<ArenaPlayer>().SelectedLoadoutIndex != activeIndex) return;
        int[] current = LoadoutSlotLayout.Types(player.inventory);
        if (observed != null && observed.SequenceEqual(current)) return;
        LocalLoadoutPositions.Remember(preset, activeIndex, ArenaPlayer.ResolveBaseLoadout(preset, activeIndex), player.inventory);
        observed = current;
        LoadoutPreviewDrawer.Invalidate();
    }
}
