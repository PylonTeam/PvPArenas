using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria.ID;
using Terraria.ModLoader.IO;

namespace PvPArenas.Common.Game.LoadoutSelector;

/// <summary>Client-only inventory-index/item-ID preferences. Kit definitions remain authoritative.</summary>
internal static class LocalLoadoutPositions
{
    private static readonly Dictionary<string, int[]> positions = [];
    private static bool loaded, dirty;
    private static long saveAfter;
    private static string FilePath => Path.Combine(Main.SavePath, "PvPArenas", "Loadouts.nbt");
    private static bool IsLocal => !Main.dedServ && Main.netMode != NetmodeID.Server;

    internal static Loadout Apply(BossFightPreset preset, int index, Loadout loadout) =>
        LoadoutSlotLayout.Arrange(loadout, Get(preset, index, loadout));

    internal static int[] Get(BossFightPreset preset, int index, Loadout loadout)
    {
        if (!IsLocal || index < 0 || index >= (preset?.Loadouts?.Count ?? 0))
            return LoadoutSlotLayout.Types(LoadoutSlotLayout.Arrange(loadout, null));
        Load();
        string key = Key(preset, index);
        int[] valid = LoadoutSlotLayout.Types(LoadoutSlotLayout.Arrange(loadout, positions.GetValueOrDefault(key)));
        Set(key, valid); // Seeds defaults and repairs obsolete entries quietly.
        return (int[])valid.Clone();
    }

    internal static void Remember(BossFightPreset preset, int index, Loadout loadout, Item[] inventory)
    {
        if (!IsLocal || index < 0 || index >= (preset?.Loadouts?.Count ?? 0)) return;
        Load();
        Set(Key(preset, index), LoadoutSlotLayout.Types(LoadoutSlotLayout.Arrange(loadout, LoadoutSlotLayout.Types(inventory))));
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
            if (root.GetInt("Version") != 2) return; // Old index permutations cannot identify changed kit items safely.
            foreach (TagCompound entry in root.GetList<TagCompound>("Loadouts"))
            {
                int[] slots = new int[LoadoutSlotLayout.SlotCount];
                foreach (TagCompound item in entry.GetList<TagCompound>("Slots"))
                {
                    int slot = item.GetInt("InventoryIndex");
                    if (slot >= 0 && slot < slots.Length) slots[slot] = item.GetInt("ItemID");
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
                ["Version"] = 2,
                ["Loadouts"] = positions.OrderBy(p => p.Key).Select(pair => new TagCompound
                {
                    ["Key"] = pair.Key,
                    ["Slots"] = pair.Value.Select((type, index) => new TagCompound
                    {
                        ["InventoryIndex"] = index, ["ItemID"] = type
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
