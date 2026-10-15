using System.IO;
using PvPArenas.Common.Game;
using PvPArenas.Common.Game.LoadoutSelector;
using Terraria.ID;

namespace PvPArenas.Common.Loadouts;

/// <summary>Old owner slot updates must not overwrite a kit before its ordered acknowledgement.</summary>
internal sealed class LoadoutDeliverySystem : ModSystem
{
    public override bool HijackGetData(ref byte messageType, ref BinaryReader reader, int playerNumber)
    {
        if (Main.netMode != NetmodeID.Server
            || messageType is not (MessageID.SyncEquipment or MessageID.PlayerLifeMana or MessageID.PlayerMana)
            || playerNumber < 0 || playerNumber >= Main.maxPlayers)
            return false;
        Player player = Main.player[playerNumber];
        RoundManager manager = ModContent.GetInstance<RoundManager>();
        if (!ArenaRespawns.IsInitialParticipant(player)
            || manager.CurrentPhase is not (RoundManager.RoundPhase.FreezeCountdown or RoundManager.RoundPhase.Playing)
            || !player.GetModPlayer<ArenaPlayer>().Delivery.Pending)
            return false;
        if (messageType != MessageID.SyncEquipment)
            return true;

        long position = reader.BaseStream.Position;
        try
        {
            reader.ReadByte(); // Vanilla forces the player ID to the authenticated sender.
            int slot = reader.ReadInt16();
            return slot >= PlayerItemSlotID.Inventory0 && slot < PlayerItemSlotID.Bank1_0
                || slot == PlayerItemSlotID.TrashItem
                || slot >= PlayerItemSlotID.Loadout1_Armor_0
                    && slot < PlayerItemSlotID.Loadout3_Dye_0 + player.Loadouts[2].Dye.Length;
        }
        catch (EndOfStreamException) { return true; }
        finally { reader.BaseStream.Position = position; }
    }
}
