using PvPArenas.Common.AdminTools.WorldGenManager;
using PvPArenas.Common.Game;
using PvPArenas.Common.Game.BossVoting;
using PvPArenas.Common.Game.LoadoutSelector;
using PvPArenas.Core.Compat;
using PvPArenas.Common.Loadouts;
using PvPArenas.Common.Generation;
using System;
using System.IO;
using Terraria.ID;

namespace PvPArenas;

public sealed class PvPArenas : Mod
{
    internal enum PacketType : byte
    {
        CastVote,
        AdminRoundAction,
        SelectLoadout,
        WorldGenRequest,
        WorldGenStatus,
        LoadoutApplied,
        LoadoutPreferences,
        ArenaWorldSync,
        RequestLoadoutPreferences,
        AcknowledgeLoadout
    }

    public override void HandlePacket(BinaryReader reader, int whoAmI)
    {
        PacketType type = (PacketType)reader.ReadByte();
        switch (type)
        {
            case PacketType.ArenaWorldSync:
                ArenaWorldSync.HandlePacket(reader, whoAmI);
                break;

            case PacketType.CastVote:
                if (Main.netMode != NetmodeID.Server)
                    return;
                ModContent.GetInstance<BossVoteSystem>().CastVote(whoAmI, reader.ReadUInt32(), reader.ReadByte());
                break;

            case PacketType.AdminRoundAction:
                if (Main.netMode != NetmodeID.Server)
                    return;
                RoundManager.AdminAction action = (RoundManager.AdminAction)reader.ReadByte();
                if (!Enum.IsDefined(action))
                    return;
                if (!ErkySSCCompat.IsAdmin(whoAmI, out string reason))
                {
                    Log.Warn($"Rejected Arenas Game Manager action from player {whoAmI}: {reason}");
                    return;
                }

                ModContent.GetInstance<RoundManager>().ExecuteAdminAction(action, whoAmI);
                break;

            case PacketType.SelectLoadout:
                if (Main.netMode != NetmodeID.Server)
                    return;
                int index = reader.ReadInt32(), boss = reader.ReadInt32();
                ArenaPlayer.HandleLoadoutSelect(whoAmI, index, boss, ArenaLoadoutDelivery.ReadPreferences(reader));
                break;

            case PacketType.LoadoutApplied:
                if (Main.netMode == NetmodeID.MultiplayerClient)
                    ArenaLoadoutDelivery.ReceiveGrant(reader);
                break;

            case PacketType.LoadoutPreferences:
                if (Main.netMode == NetmodeID.Server)
                    ArenaLoadoutDelivery.HandlePreferences(whoAmI, reader);
                break;

            case PacketType.RequestLoadoutPreferences:
                ArenaLoadoutDelivery.HandleRequest(reader);
                break;

            case PacketType.AcknowledgeLoadout:
                ArenaLoadoutDelivery.HandleAcknowledgement(whoAmI, reader);
                break;

            case PacketType.WorldGenRequest:
                WorldGenManagerNetHandler.HandleRequest(reader, whoAmI);
                break;

            case PacketType.WorldGenStatus:
                WorldGenManagerNetHandler.HandleStatus(reader);
                break;
        }
    }
}

