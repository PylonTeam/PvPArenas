using System;
using System.Collections.Generic;
using System.Linq;
using Terraria.ID;
using Terraria.ModLoader.Config;

namespace PvPArenas.Common.Game.LoadoutSelector;

/// <summary>Slot preferences arrange configured items without changing kit contents or quantities.</summary>
internal static class LoadoutSlotLayout
{
    internal const int SlotCount = 58; // Backpack/hotbar, coins, ammo; excludes the cursor slot.
    internal const int ArmorSlotCount = 20;
    internal const int PositionCount = SlotCount + ArmorSlotCount;

    internal static Loadout Arrange(Loadout loadout, IReadOnlyList<int> positions, Player player = null)
    {
        List<(LoadoutItem Item, int Slot)> source = [];
        for (int i = 0; i < (loadout?.Inventory?.Count ?? 0); i++)
            if (loadout.Inventory[i]?.Item?.Type > 0)
                source.Add((loadout.Inventory[i], i < SlotCount ? i : -1));
        IReadOnlyList<LoadoutItem> armor = ArmorItems(loadout);
        for (int i = 0; i < ArmorSlotCount; i++)
            if (armor[i]?.Item?.Type > 0)
                source.Add((armor[i], SlotCount + i));

        LoadoutItem[] slots = new LoadoutItem[PositionCount];
        bool[] used = new bool[source.Count];

        bool Allows(int slot, LoadoutItem entry)
        {
            int type = entry.Item.Type;
            if (slot < 50) return true;
            if (slot < 54) return type is >= ItemID.CopperCoin and <= ItemID.PlatinumCoin;
            if (!ContentSamples.ItemsByType.TryGetValue(type, out Item item)) return false;
            if (slot < SlotCount) return item.ammo > 0;

            int index = slot - SlotCount;
            if (entry.Stack != 1 || player != null && !player.IsItemSlotUnlockedAndUsable(index)) return false;
            int kind = index % 10;
            if (kind < 3)
                return kind switch { 0 => item.headSlot >= 0, 1 => item.bodySlot >= 0, _ => item.legSlot >= 0 };
            if (!item.accessory) return false;
            if (index >= 10 && !item.FitsAccessoryVanitySlot) return false;
            if (player != null && !ItemLoader.CanEquipAccessory(player, item, index, modded: false)) return false;
            for (int other = 3; other < ArmorSlotCount; other++)
                if (slots[SlotCount + other] is LoadoutItem equipped
                    && ContentSamples.ItemsByType.TryGetValue(equipped.Item.Type, out Item sample)
                    && sample.accessory
                    && (sample.type == type || (other >= 10) == (index >= 10)
                        && (index < 10 && sample.wingSlot > 0 && item.wingSlot > 0
                            || player != null && !ItemLoader.CanAccessoryBeEquippedWith(player, sample, item))))
                    return false;
            return true;
        }

        bool Place(int sourceIndex, int slot)
        {
            if (slot < 0 || slots[slot] != null || !Allows(slot, source[sourceIndex].Item)) return false;
            slots[slot] = source[sourceIndex].Item;
            used[sourceIndex] = true;
            return true;
        }

        // Inventory-only saves predate equipment preferences. Reserve configured equipment
        // first so an inventory copy of the same item cannot steal its equipped counterpart.
        if ((positions?.Count ?? 0) <= SlotCount)
            for (int i = 0; i < source.Count; i++)
                if (source[i].Slot >= SlotCount)
                    Place(i, source[i].Slot);

        // Match equipment first, then inventory; each configured stack can be consumed once.
        foreach (int slot in Enumerable.Range(SlotCount, ArmorSlotCount).Concat(Enumerable.Range(0, SlotCount)))
        {
            if (slot >= (positions?.Count ?? 0) || positions[slot] <= 0) continue;
            foreach (int i in Enumerable.Range(0, source.Count)
                .OrderBy(i => (slot >= SlotCount) == (source[i].Slot >= SlotCount) ? 0 : 1))
                if (!used[i] && source[i].Item.Item.Type == positions[slot] && Place(i, slot))
                {
                    break;
                }
        }

        // Unplaced entries return to a compatible configured/equipment slot or the backpack.
        foreach (int i in Enumerable.Range(0, source.Count).OrderBy(i => source[i].Slot >= SlotCount ? 0 : 1))
        {
            if (used[i] || Place(i, source[i].Slot)) continue;
            if (source[i].Slot >= SlotCount)
            {
                int first = SlotCount + (source[i].Slot - SlotCount) / 10 * 10;
                for (int slot = first; slot < first + 10 && !used[i]; slot++) Place(i, slot);
            }
            for (int slot = 0; slot < SlotCount && !used[i]; slot++) Place(i, slot);
            if (!used[i])
            {
                // A stale preference can fill the backpack with equipment even though the
                // configured kit fits. Retry its defaults before rejecting an oversized kit.
                if (positions is { Count: > 0 }) return Arrange(loadout, null, player);
                throw new InvalidOperationException("The selected loadout contains more items than fit in its valid slots.");
            }
        }

        LoadoutItem[] inventory = slots.Take(SlotCount).ToArray();
        LoadoutItem[] equippedSlots = slots.Skip(SlotCount).ToArray();
        ItemDefinition Definition(int index) => equippedSlots[index]?.Item ?? new(ItemID.None);
        int count = Math.Max(10, Array.FindLastIndex(inventory, item => item != null) + 1);
        return new Loadout
        {
            Armor = new() { Head = Definition(0), Body = Definition(1), Legs = Definition(2) },
            Accessories = new()
            {
                Accessory1 = Definition(3), Accessory2 = Definition(4), Accessory3 = Definition(5),
                Accessory4 = Definition(6), Accessory5 = Definition(7)
            },
            ArmorSlots = equippedSlots,
            Equipment = loadout?.Equipment,
            Inventory = inventory.Take(count).ToList()
        };
    }

    internal static IReadOnlyList<LoadoutItem> ArmorItems(Loadout loadout)
    {
        if (loadout?.ArmorSlots != null) return loadout.ArmorSlots;
        ItemDefinition[] definitions =
        [
            loadout?.Armor?.Head, loadout?.Armor?.Body, loadout?.Armor?.Legs,
            loadout?.Accessories?.Accessory1, loadout?.Accessories?.Accessory2,
            loadout?.Accessories?.Accessory3, loadout?.Accessories?.Accessory4, loadout?.Accessories?.Accessory5
        ];
        LoadoutItem[] slots = new LoadoutItem[ArmorSlotCount];
        for (int i = 0; i < definitions.Length; i++)
            if (definitions[i]?.Type > 0) slots[i] = new() { Item = definitions[i] };
        return slots;
    }

    internal static int[] Types(Loadout loadout) => Enumerable.Range(0, SlotCount)
        .Select(i => i < (loadout?.Inventory?.Count ?? 0) ? loadout.Inventory[i]?.Item?.Type ?? 0 : 0)
        .Concat(ArmorItems(loadout).Select(item => item?.Item?.Type ?? 0)).ToArray();

    internal static int[] Types(Item[] inventory) => Enumerable.Range(0, SlotCount)
        .Select(i => i < inventory.Length && inventory[i]?.IsAir == false ? inventory[i].type : 0).ToArray();

    internal static int[] Types(Player player) => Types(player.inventory)
        .Concat(Enumerable.Range(0, ArmorSlotCount)
            .Select(i => i < player.armor.Length && player.armor[i]?.IsAir == false ? player.armor[i].type : 0)).ToArray();
}
