using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria.ID;
using Terraria.ModLoader.IO;

namespace PvPArenas.Common.Game.LoadoutSelector;

/// <summary>Client-only inventory/equipment slot preferences. Kit definitions remain authoritative.</summary>
internal static class LocalLoadoutPositions
{
    private static readonly Dictionary<string, int[]> positions = [];
    private static bool loaded, dirty;
    private static long saveAfter;
    private static string FilePath => Path.Combine(Main.SavePath, "PvPArenas", "Loadouts.nbt");
    private static bool IsLocal => !Main.dedServ && Main.netMode != NetmodeID.Server;

    internal static Loadout Apply(BossFightPreset preset, int index, Loadout loadout) =>
        LoadoutSlotLayout.Arrange(loadout, Get(preset, index, loadout), Main.LocalPlayer);

    internal static int[] Get(BossFightPreset preset, int index, Loadout loadout)
    {
        if (!IsLocal || index < 0 || index >= (preset?.Loadouts?.Count ?? 0))
            return LoadoutSlotLayout.Types(LoadoutSlotLayout.Arrange(loadout, null));
        Load();
        string key = Key(preset, index);
        // Persist slot preferences independently of the current character/world's unlocks.
        // The actual grant checks usable equipment slots with the owning player.
        int[] valid = LoadoutSlotLayout.Types(LoadoutSlotLayout.Arrange(loadout, positions.GetValueOrDefault(key)));
        Set(key, valid); // Seeds defaults and repairs obsolete entries quietly.
        return (int[])valid.Clone();
    }

    internal static void Remember(BossFightPreset preset, int index, Loadout loadout, Player player)
    {
        if (!IsLocal || index < 0 || index >= (preset?.Loadouts?.Count ?? 0)) return;
        Load();
        Set(Key(preset, index), LoadoutSlotLayout.Types(LoadoutSlotLayout.Arrange(loadout, LoadoutSlotLayout.Types(player))));
    }

    private static string Key(BossFightPreset preset, int index)
    {
        string name = preset.Loadouts[index]?.Name ?? "Loadout";
        string suffix = preset.Loadouts.Count(option => option?.Name == name) > 1 ? $":{index}" : "";
        return $"{preset.Boss.Type}:{name}{suffix}";
    }

    private static void Set(string key, int[] value)
    {
        if (positions.TryGetValue(key, out int[] old) && old.SequenceEqual(value)) return;
        positions[key] = value;
        dirty = true;
        saveAfter = Environment.TickCount64 + 500;
    }

    private static void Load()
    {
        if (loaded || !IsLocal) return;
        loaded = true;
        if (!File.Exists(FilePath)) return;
        try
        {
            TagCompound root = TagIO.FromFile(FilePath);
            int version = root.GetInt("Version");
            if (version is not (2 or 3)) return; // Old index permutations cannot identify changed kit items safely.
            foreach (TagCompound entry in root.GetList<TagCompound>("Loadouts"))
            {
                // Keep v2 entries inventory-only until their configured equipment is normalized.
                int[] slots = new int[version == 2 ? LoadoutSlotLayout.SlotCount : LoadoutSlotLayout.PositionCount];
                foreach (TagCompound item in entry.GetList<TagCompound>("Slots"))
                {
                    int slot = item.GetInt("InventoryIndex");
                    if (slot >= 0 && slot < LoadoutSlotLayout.SlotCount) slots[slot] = item.GetInt("ItemID");
                }
                if (version == 3)
                    foreach (TagCompound item in entry.GetList<TagCompound>("ArmorSlots"))
                    {
                        int slot = item.GetInt("ArmorIndex");
                        if (slot >= 0 && slot < LoadoutSlotLayout.ArmorSlotCount)
                            slots[LoadoutSlotLayout.SlotCount + slot] = item.GetInt("ItemID");
                    }
                positions[entry.GetString("Key")] = slots;
            }
        }
        catch (Exception error) { Log.Warn($"Loadout positions could not be read: {error.Message}"); }
    }

    internal static void Flush(bool force = false)
    {
        if (!IsLocal || !dirty || !force && Environment.TickCount64 < saveAfter) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            TagCompound root = new()
            {
                ["Version"] = 3,
                ["Loadouts"] = positions.OrderBy(p => p.Key).Select(pair => new TagCompound
                {
                    ["Key"] = pair.Key,
                    ["Slots"] = pair.Value.Take(LoadoutSlotLayout.SlotCount).Select((type, index) => new TagCompound
                    {
                        ["InventoryIndex"] = index, ["ItemID"] = type
                    }).Where(tag => tag.GetInt("ItemID") > 0).ToList(),
                    ["ArmorSlots"] = pair.Value.Skip(LoadoutSlotLayout.SlotCount).Select((type, index) => new TagCompound
                    {
                        ["ArmorIndex"] = index, ["ItemID"] = type
                    }).Where(tag => tag.GetInt("ItemID") > 0).ToList()
                }).ToList()
            };
            TagIO.ToFile(root, FilePath + ".tmp");
            File.Move(FilePath + ".tmp", FilePath, overwrite: true);
            dirty = false;
        }
        catch (Exception error)
        {
            saveAfter = Environment.TickCount64 + 30_000;
            Log.Warn($"Loadout positions could not be saved: {error.Message}");
        }
    }

    internal static void Unload()
    {
        Flush(force: true);
        loaded = dirty = false;
        positions.Clear();
    }
}
