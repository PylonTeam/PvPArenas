// Retired Arrange items implementation; retained for reference only.
// using System;
// using System.Collections.Generic;
// using System.IO;
// using System.Linq;
// using Terraria.ModLoader.IO;
//
// namespace PvPArenas._Deprecated.LoadoutSelector;
//
// internal static class LocalLoadoutOrder
// {
//     private static string Folder => Path.Combine(Main.SavePath, "PvPArenas");
//     private static string File => Path.Combine(Folder, "Loadouts.nbt");
//
//     private static readonly Dictionary<string, List<int>> Orders = [];
//     private static bool loaded;
//
//     internal static List<int> GetOrder(
//         BossFightPreset preset,
//         int loadoutIndex,
//         Loadout loadout)
//     {
//         Load();
//
//         string key = Key(preset, loadoutIndex);
//         int count = SlotCount(loadout);
//
//         if (!Orders.TryGetValue(key, out List<int> order) ||
//             !IsValid(order, loadout, count))
//         {
//             Orders[key] = order = DefaultOrder(loadout, count);
//         }
//
//         return [.. order];
//     }
//
//     internal static void SetOrder(
//         BossFightPreset preset,
//         int loadoutIndex,
//         Loadout loadout,
//         List<int> order)
//     {
//         int count = SlotCount(loadout);
//
//         if (!IsValid(order, loadout, count))
//             return;
//
//         Orders[Key(preset, loadoutIndex)] = [.. order];
//         Save();
//     }
//
//     internal static Loadout Apply(
//         BossFightPreset preset,
//         int loadoutIndex,
//         Loadout loadout) =>
//         Apply(loadout, GetOrder(preset, loadoutIndex, loadout));
//
//     internal static Loadout Apply(
//         Loadout loadout,
//         IReadOnlyList<int> order)
//     {
//         List<LoadoutItem> source = loadout?.Inventory?.ToList() ?? [];
//
//         while (source.Count < order.Count)
//             source.Add(null);
//
//         return new Loadout
//         {
//             Armor = loadout?.Armor,
//             Accessories = loadout?.Accessories,
//             Equipment = loadout?.Equipment,
//             Inventory =
//             [
//                 .. order.Select(index =>
//                     index >= 0 && index < source.Count
//                         ? source[index]
//                         : null)
//             ]
//         };
//     }
//
//     internal static LoadoutItem ItemAt(
//         Loadout loadout,
//         int originalIndex)
//     {
//         if (originalIndex < 0 ||
//             originalIndex >= (loadout?.Inventory?.Count ?? 0))
//         {
//             return null;
//         }
//
//         return loadout.Inventory[originalIndex];
//     }
//
//     private static int SlotCount(Loadout loadout) =>
//         Math.Min(50, Math.Max(10, loadout?.Inventory?.Count ?? 0));
//
//     private static List<int> DefaultOrder(
//         Loadout loadout,
//         int count)
//     {
//         List<int> order = [];
//
//         for (int i = 0; i < count; i++)
//         {
//             LoadoutItem item =
//                 i < (loadout?.Inventory?.Count ?? 0)
//                     ? loadout.Inventory[i]
//                     : null;
//
//             order.Add((item?.Item?.Type ?? 0) > 0 ? i : -1);
//         }
//
//         return order;
//     }
//
//     private static bool IsValid(
//         IReadOnlyList<int> order,
//         Loadout loadout,
//         int count)
//     {
//         if (order == null || order.Count != count)
//             return false;
//
//         List<int> expected = DefaultOrder(loadout, count)
//             .Where(index => index >= 0)
//             .OrderBy(index => index)
//             .ToList();
//
//         List<int> actual = order
//             .Where(index => index >= 0)
//             .OrderBy(index => index)
//             .ToList();
//
//         return actual.Count == actual.Distinct().Count() &&
//             actual.SequenceEqual(expected);
//     }
//
//     private static string Key(
//         BossFightPreset preset,
//         int loadoutIndex)
//     {
//         string name =
//             loadoutIndex >= 0 &&
//             loadoutIndex < (preset?.Loadouts?.Count ?? 0)
//                 ? preset.Loadouts[loadoutIndex]?.Name
//                 : null;
//
//         return $"{preset?.Boss?.Type ?? 0}:{loadoutIndex}:{name}";
//     }
//
//     private static void Load()
//     {
//         if (loaded)
//             return;
//
//         loaded = true;
//
//         if (!System.IO.File.Exists(File))
//             return;
//
//         try
//         {
//             TagCompound root = TagIO.FromFile(File);
//
//             foreach (TagCompound tag in root.GetList<TagCompound>("Loadouts"))
//             {
//                 Orders[tag.GetString("Key")] =
//                     [.. tag.GetList<int>("Order")];
//             }
//         }
//         catch (Exception e)
//         {
//             Log.Error($"Loadout order load failed: {e}");
//         }
//     }
//
//     private static void Save()
//     {
//         if (Main.dedServ)
//             return;
//
//         try
//         {
//             Directory.CreateDirectory(Folder);
//
//             TagCompound root = new()
//             {
//                 ["Loadouts"] = Orders.Select(pair => new TagCompound
//                 {
//                     ["Key"] = pair.Key,
//                     ["Order"] = pair.Value
//                 }).ToList()
//             };
//
//             TagIO.ToFile(root, File);
//         }
//         catch (Exception e)
//         {
//             Log.Error($"Loadout order save failed: {e}");
//         }
//     }
// }
