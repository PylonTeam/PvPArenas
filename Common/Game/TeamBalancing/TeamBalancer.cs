using System;
using System.Linq;
using Terraria.Enums;
using Terraria.ID;

namespace PvPArenas.Common.Game.TeamBalancing;

/// <summary>Server-owned Red/Blue assignment shared by joins, voting, and admin controls.</summary>
internal static class TeamBalancer
{
    internal static void AssignJoiningPlayer(Player player)
    {
        if (!ModContent.GetInstance<RoundManager>().IsActive || Main.netMode == NetmodeID.MultiplayerClient || player?.active != true
            || IsArenaTeam((Team)player.team))
            return;

        int red = Count(Team.Red);
        int blue = Count(Team.Blue);
        SetTeam(player, red <= blue ? Team.Red : Team.Blue);
    }

    internal static void AssignUnassignedPlayers()
    {
        if (!ModContent.GetInstance<RoundManager>().IsActive || Main.netMode == NetmodeID.MultiplayerClient)
            return;

        int red = Count(Team.Red);
        int blue = Count(Team.Blue);
        foreach (Player player in ActivePlayers().Where(player => !IsArenaTeam((Team)player.team)))
        {
            Team team = red <= blue ? Team.Red : Team.Blue;
            SetTeam(player, team);
            if (team == Team.Red)
                red++;
            else
                blue++;
        }
    }

    internal static void AutoBalanceTeams()
    {
        if (!ModContent.GetInstance<RoundManager>().IsActive || Main.netMode == NetmodeID.MultiplayerClient)
            return;

        Player[] players = ActivePlayers();
        for (int i = players.Length - 1; i > 0; i--)
        {
            int swap = Main.rand.Next(i + 1);
            (players[i], players[swap]) = (players[swap], players[i]);
        }

        int redTarget = (players.Length + 1) / 2;
        for (int i = 0; i < players.Length; i++)
            SetTeam(players[i], i < redTarget ? Team.Red : Team.Blue);
    }

    internal static bool AllActivePlayersAssigned()
    {
        Player[] players = ActivePlayers();
        return players.Length > 0 && players.All(player => IsArenaTeam((Team)player.team));
    }

    private static Player[] ActivePlayers() => Main.player
        .Where(player => player?.active == true)
        .OrderBy(player => player.whoAmI)
        .ToArray();

    private static int Count(Team team) => Main.player.Count(player =>
        player?.active == true && (Team)player.team == team);

    private static bool IsArenaTeam(Team team) => team is Team.Red or Team.Blue;

    private static void SetTeam(Player player, Team team)
    {
        if (player?.active != true || (Team)player.team == team)
            return;

        player.team = (int)team;
        if (Main.netMode == NetmodeID.Server)
            NetMessage.SendData(MessageID.PlayerTeam, -1, -1, null, player.whoAmI, player.team);
    }
}
