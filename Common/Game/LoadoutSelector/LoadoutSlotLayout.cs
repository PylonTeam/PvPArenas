using System;
using System.Collections.Generic;
using System.Linq;
using Terraria.ID;

namespace PvPArenas.Common.Game.LoadoutSelector;

/// <summary>Slot preferences never define kit contents, quantities, or equipment.</summary>
internal static class LoadoutSlotLayout
{
    internal const int SlotCount = 58; // Backpack/hotbar, coins, ammo; excludes the cursor slot.

    internal static Loadout Arrange(Loadout loadout, IReadOnlyList<int> positions)
    {
        List<LoadoutItem> source = loadout?.Inventory ?? [];
        LoadoutItem[] slots = new LoadoutItem[SlotCount];
        bool[] used = new bool[source.Count];
        for (int slot = 0; slot < Math.Min(positions?.Count ?? 0, SlotCount); slot++)
        {
            int type = positions[slot];
            if (type <= 0 || !Allows(slot, type)) continue;
            for (int i = 0; i < source.Count; i++)
                if (!used[i] && source[i]?.Item?.Type == type)
                {
                    slots[slot] = source[i];
                    used[i] = true;
                    break;
                }
        }
        // Missing/trashed/stale entries return to their configured slot when free,
        // otherwise the first suitable hole. Consume each configured entry once.
        for (int i = 0; i < source.Count; i++)
        {
            int type = source[i]?.Item?.Type ?? 0;
            if (used[i] || type <= 0) continue;
            int target = i < SlotCount && slots[i] == null && Allows(i, type) ? i
                : Enumerable.Range(0, SlotCount).FirstOrDefault(s => slots[s] == null && Allows(s, type), -1);
            if (target < 0) throw new InvalidOperationException("The selected loadout contains more items than fit in inventory.");
            slots[target] = source[i];
        }
        int count = Math.Max(10, Array.FindLastIndex(slots, item => item != null) + 1);
        return new Loadout
        {
            Armor = loadout?.Armor, Accessories = loadout?.Accessories, Equipment = loadout?.Equipment,
            Inventory = slots.Take(count).ToList()
        };
    }

    internal static int[] Types(Loadout loadout) => Enumerable.Range(0, SlotCount)
        .Select(i => i < (loadout.Inventory?.Count ?? 0) ? loadout.Inventory[i]?.Item?.Type ?? 0 : 0).ToArray();

    internal static int[] Types(Item[] inventory) => Enumerable.Range(0, SlotCount)
        .Select(i => i < inventory.Length && inventory[i]?.IsAir == false ? inventory[i].type : 0).ToArray();

    // Reorders existing objects only: no item creation, healing, stack reset, or removal.
    internal static Item[] Reorder(Item[] inventory, Loadout loadout, IReadOnlyList<int> positions)
    {
        int[] wanted = Types(Arrange(loadout, positions));
        Item[] result = new Item[SlotCount];
        bool[] used = new bool[SlotCount];
        for (int slot = 0; slot < SlotCount; slot++)
        {
            if (wanted[slot] <= 0) continue;
            for (int i = 0; i < SlotCount; i++)
                if (!used[i] && inventory[i]?.IsAir == false && inventory[i].type == wanted[slot])
                {
                    result[slot] = inventory[i];
                    used[i] = true;
                    break;
                }
        }
        for (int i = 0; i < SlotCount; i++)
        {
            if (used[i] || inventory[i]?.IsAir != false) continue;
            int slot = result[i] == null ? i : Enumerable.Range(0, SlotCount)
                .FirstOrDefault(s => result[s] == null && Allows(s, inventory[i].type), -1);
            // A pickup-filled inventory may leave no legal packing. Keep it intact.
            if (slot < 0) return inventory.Take(SlotCount).ToArray();
            result[slot] = inventory[i];
        }
        for (int i = 0; i < SlotCount; i++) result[i] ??= new Item();
        return result;
    }

    private static bool Allows(int slot, int type) => slot < 50
        || slot < 54 && type is >= ItemID.CopperCoin and <= ItemID.PlatinumCoin
        || slot >= 54 && ContentSamples.ItemsByType.TryGetValue(type, out Item item) && item.ammo > 0;
}
