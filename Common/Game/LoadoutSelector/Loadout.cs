using PvPArenas.Core.Configs.ConfigElements.LoadoutItems;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader.Config;

namespace PvPArenas.Common.Game.LoadoutSelector;

public class Loadout
{
    [CustomModConfigItem(typeof(LoadoutArmorElement))]
    public LoadoutArmor Armor { get; set; } = new();

    [CustomModConfigItem(typeof(LoadoutAccessoriesElement))]
    public LoadoutAccessories Accessories { get; set; } = new();

    [CustomModConfigItem(typeof(LoadoutEquipmentElement))]
    public LoadoutEquipment Equipment { get; set; } = new();

    [CustomModConfigItem(typeof(LoadoutInventoryListElement))]
    public List<LoadoutItem> Inventory { get; set; } = [];
}
public class LoadoutArmor
{
    [CustomModConfigItem(typeof(ItemHeadDefinitionElement))]
    public ItemDefinition Head { get; set; } = new(ItemID.None);

    [CustomModConfigItem(typeof(ItemBodyDefinitionElement))]
    public ItemDefinition Body { get; set; } = new(ItemID.None);

    [CustomModConfigItem(typeof(ItemLegsDefinitionElement))]
    public ItemDefinition Legs { get; set; } = new(ItemID.None);
}

public class LoadoutAccessories
{
    [CustomModConfigItem(typeof(ItemAccessoryDefinitionElement))]
    public ItemDefinition Accessory1 { get; set; } = new(ItemID.None);

    [CustomModConfigItem(typeof(ItemAccessoryDefinitionElement))]
    public ItemDefinition Accessory2 { get; set; } = new(ItemID.None);

    [CustomModConfigItem(typeof(ItemAccessoryDefinitionElement))]
    public ItemDefinition Accessory3 { get; set; } = new(ItemID.None);

    [CustomModConfigItem(typeof(ItemAccessoryDefinitionElement))]
    public ItemDefinition Accessory4 { get; set; } = new(ItemID.None);

    [CustomModConfigItem(typeof(ItemAccessoryDefinitionElement))]
    public ItemDefinition Accessory5 { get; set; } = new(ItemID.None);
}

public class LoadoutEquipment
{
    [CustomModConfigItem(typeof(ItemGrapplingHookDefinitionElement))]
    public ItemDefinition GrapplingHook { get; set; } = new(ItemID.None);

    [CustomModConfigItem(typeof(ItemMountDefinitionElement))]
    public ItemDefinition Mount { get; set; } = new(ItemID.None);
}

public class LoadoutItem
{
    private ItemDefinition _item = new(ItemID.None);
    private int _stack = 1;

    public ItemDefinition Item
    {
        get => _item;
        set
        {
            _item = value ?? new ItemDefinition(ItemID.None);
            Stack = _stack;
        }
    }

    [DefaultValue(1)]
    [Range(1, 9999)]
    public int Stack
    {
        get => _stack;
        set
        {
            _stack = Math.Clamp(value, 1, GetMaxStack(Item?.Type ?? 0));
        }
    }

    public static int GetMaxStack(int type)
    {
        if (type <= 0)
            return 1;

        if (ContentSamples.ItemsByType.TryGetValue(type, out Item sample))
            return Math.Max(1, sample.maxStack);
        Item temp = new();
        temp.SetDefaults(type);
        return Math.Max(1, temp.maxStack);
    }
}


