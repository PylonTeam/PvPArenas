using System;
using Terraria.ID;

namespace PvPArenas.Common.Game;

internal static class ArenaRoundBuffs
{
    internal static void Clear(Player player)
    {
        Array.Clear(player.buffType);
        Array.Clear(player.buffTime);
        player.potionDelay = 0;

        if (Main.netMode == NetmodeID.Server)
            NetMessage.SendData(MessageID.PlayerBuffs, number: player.whoAmI);
    }
}
