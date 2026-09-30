using PvPArenas.Common.AdminTools.WorldGenManager;
using PvPArenas.Common.Game;
using PvPArenas.Common.Game.BossVoting;
using PvPArenas.Common.Game.LoadoutSelector;
using PvPArenas.Core.Compat;
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
        LoadoutPositions
    }

    public override void HandlePacket(BinaryReader reader, int whoAmI)
    {
        PacketType type = (PacketType)reader.ReadByte();
        switch (type)
        {
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
                ArenaPlayer.HandleLoadoutSelect(whoAmI, reader.ReadInt32());
                break;

            case PacketType.LoadoutApplied:
                if (Main.netMode == NetmodeID.MultiplayerClient)
                    ArenaPlayer.ReceiveLoadoutApplied(reader);
                break;

            case PacketType.LoadoutPositions:
                if (Main.netMode == NetmodeID.Server)
                    ArenaPlayer.ReceiveLoadoutPositions(whoAmI, reader);
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

