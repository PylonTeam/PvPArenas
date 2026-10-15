using System;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.ModLoader.IO;

namespace PvPArenas.Core.Inventory;

/// <summary>An atomic copy of carried equipment, without touching the character's banks.</summary>
internal sealed class CarriedItemSnapshot(Item[][] groups, Item trashItem)
{
    internal static void Write(BinaryWriter writer, Player player)
    {
        Item[][] carried = Groups(player);
        writer.Write((byte)carried.Length);
        foreach (Item[] group in carried)
        {
            writer.Write((ushort)group.Length);
            foreach (Item item in group)
                ItemIO.Send(item, writer, writeStack: true, writeFavorite: true);
        }
        ItemIO.Send(player.trashItem, writer, writeStack: true, writeFavorite: true);
    }

    internal static CarriedItemSnapshot Read(BinaryReader reader, Player player)
    {
        Item[][] expected = Groups(player);
        if (reader.ReadByte() != expected.Length)
            throw new InvalidDataException("The carried equipment groups do not match this character.");
        Item[][] received = new Item[expected.Length][];
        for (int i = 0; i < received.Length; i++)
        {
            int count = reader.ReadUInt16();
            if (count != expected[i].Length)
                throw new InvalidDataException("The carried equipment slots do not match this character.");
            received[i] = new Item[count];
            for (int slot = 0; slot < count; slot++)
                received[i][slot] = ItemIO.Receive(reader, readStack: true, readFavorite: true);
        }
        return new(received, ItemIO.Receive(reader, readStack: true, readFavorite: true));
    }

    internal void Apply(Player player)
    {
        Item[][] carried = Groups(player);
        for (int i = 0; i < carried.Length; i++)
            for (int slot = 0; slot < carried[i].Length; slot++)
                carried[i][slot] = groups[i][slot].Clone();
        player.trashItem = trashItem.Clone();
    }

    private static Item[][] Groups(Player player) =>
        new[] { player.inventory, player.armor, player.dye, player.miscEquips, player.miscDyes }
            .Concat(player.Loadouts.SelectMany(loadout => new[] { loadout.Armor, loadout.Dye })).ToArray();
}
