using MonoMod.RuntimeDetour;
using PvPArenas.Common.Game;
using PvPArenas.Common.Game.LoadoutSelector;
using System;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.UI;

namespace PvPArenas.Common.Loadouts;

/// <summary>Keep the owner's carried items unchanged until its ordered loadout delivery completes.</summary>
[Autoload(Side = ModSide.Client)]
internal sealed class LoadoutInputSystem : ModSystem
{
    private Hook ignoreMouseInterface;

    public override void Load()
    {
        ignoreMouseInterface = new Hook(
            typeof(PlayerInput).GetProperty(nameof(PlayerInput.IgnoreMouseInterface)).GetMethod,
            (Func<Func<bool>, bool>)IgnoreMouseInterface);
        On_ItemSlot.CanExecuteCommand += CanExecuteCommand;
        On_Player.dropItemCheck += DropItemCheck;
        On_Player.DropSelectedItem_int_refItem += DropSelectedItem;
        On_Player.GetItem += GetItem;
    }

    public override void Unload()
    {
        ignoreMouseInterface?.Dispose();
        ignoreMouseInterface = null;
        On_ItemSlot.CanExecuteCommand -= CanExecuteCommand;
        On_Player.dropItemCheck -= DropItemCheck;
        On_Player.DropSelectedItem_int_refItem -= DropSelectedItem;
        On_Player.GetItem -= GetItem;
    }

    internal static bool IsLocked(Player player)
    {
        if (Main.dedServ || Main.netMode != NetmodeID.MultiplayerClient || Main.gameMenu
            || player == null || !ReferenceEquals(player, Main.LocalPlayer)
            || !ArenaRespawns.IsInitialParticipant(player))
            return false;

        RoundManager manager = ModContent.GetInstance<RoundManager>();
        if (manager == null || !manager.TryGetSelectedPreset(out BossFightPreset preset) || preset.IsSandbox())
            return false;

        return manager.CurrentPhase == RoundManager.RoundPhase.Generating
            || manager.CurrentPhase is RoundManager.RoundPhase.FreezeCountdown or RoundManager.RoundPhase.Playing
                && player.GetModPlayer<ArenaPlayer>().AwaitingLoadoutDelivery;
    }

    // Vanilla's inventory slots, crafting, trash, sorting and chest buttons share this
    // input gate. Rendering remains intact; no global mouse state needs to be changed.
    private static bool IgnoreMouseInterface(Func<bool> orig) => IsLocked(Main.LocalPlayer) || orig();

    // Gamepad quick equip, favorite, drop and fast-use commands can run while drawing
    // their instructions, before the ordinary inventory mouse input is considered.
    private static bool CanExecuteCommand(On_ItemSlot.orig_CanExecuteCommand orig) =>
        !IsLocked(Main.LocalPlayer) && orig();

    private static void DropItemCheck(On_Player.orig_dropItemCheck orig, Player player)
    {
        // Closing an inventory or Guide/reforge menu can directly drop overflow,
        // bypassing DropSelectedItem. Defer that entire pass while delivery is pending.
        if (!IsLocked(player))
            orig(player);
    }

    private static void DropSelectedItem(On_Player.orig_DropSelectedItem_int_refItem orig,
        Player player, int slot, ref Item item)
    {
        if (!IsLocked(player))
            orig(player, slot, ref item);
    }

    private static Item GetItem(On_Player.orig_GetItem orig, Player player, int slot, Item item, GetItemSettings settings) =>
        // Leave pickups in the world instead of replacing them with a returning snapshot.
        IsLocked(player) ? item : orig(player, slot, item, settings);
}

[Autoload(Side = ModSide.Client)]
internal sealed class LoadoutInputPlayer : ModPlayer
{
    // Quick heal/mana/buff execute before SetControls. Their normal CanUseItem check
    // also covers them during a late join into an already-playing round.
    public override bool CanUseItem(Item item) => !LoadoutInputSystem.IsLocked(Player);
}
