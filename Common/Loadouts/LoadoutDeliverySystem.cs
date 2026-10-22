using System.IO;
using PvPArenas.Common.Game;
using PvPArenas.Common.Game.LoadoutSelector;
using Terraria.ID;

namespace PvPArenas.Common.Loadouts;

/// <summary>Old owner updates must not overwrite round preparation before its ordered acknowledgement.</summary>
internal sealed class LoadoutDeliverySystem : ModSystem
{
    public override bool HijackGetData(ref byte messageType, ref BinaryReader reader, int playerNumber)
    {
        if (Main.netMode != NetmodeID.Server
            || messageType is not (MessageID.SyncEquipment or MessageID.PlayerLifeMana or MessageID.PlayerMana or MessageID.PlayerBuffs)
            || playerNumber < 0 || playerNumber >= Main.maxPlayers)
            return false;
        Player player = Main.player[playerNumber];
        RoundManager manager = ModContent.GetInstance<RoundManager>();
        if (messageType == MessageID.PlayerBuffs)
        {
            if (!manager.IsActive || player?.active != true) return false;
            if (manager.CurrentPhase is RoundManager.RoundPhase.WaitingForPlayers
                or RoundManager.RoundPhase.VotingOrEndScreen or RoundManager.RoundPhase.Generating)
                return true;
            ArenaPlayer owner = player.GetModPlayer<ArenaPlayer>();
            return manager.CurrentPhase is RoundManager.RoundPhase.FreezeCountdown or RoundManager.RoundPhase.Playing
                && (!owner.HasRoundLoadout || owner.Delivery.Pending);
        }
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
