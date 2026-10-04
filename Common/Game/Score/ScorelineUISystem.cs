using PvPArenas.Core.Configs;
using PvPFramework.Common.Combat.TeamBoss;
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria.GameContent;
using Terraria.Enums;
using Terraria.UI;
using PvPFramework.Common.Game;
using PvPArenas.Common.Game.BossVoting;

namespace PvPArenas.Common.Game.Score;

[Autoload(Side = ModSide.Client)]
internal sealed class ScorelineUISystem : ModSystem
{
    private const float DamageScale = .95f;
    private const int ChooseTeamSidePadding = 56;
    private const int TeamHeight = 32;
    private const int PanelGap = 0;
    private const int HeadSize = 28;
    private const int HeadPadding = 14;
    private const int MaxTeamWidth = 260;
    private const int DamagePanelWidth = 64;

    private static float opacity = 1f;

    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        int index = layers.FindIndex(layer => layer.Name == "Vanilla: Mouse Text");
        if (index >= 0)
            layers.Insert(index, new LegacyGameInterfaceLayer(
                "Arenas: Scoreline Top HUD", Draw, InterfaceScaleType.UI));
    }

    private static bool Draw()
    {
        if (Main.gameMenu)
            return true;

        RoundManager manager = ModContent.GetInstance<RoundManager>();
        if (!manager.IsActive)
            return true;
        bool playing = manager.CurrentPhase == RoundManager.RoundPhase.Playing;
        Rectangle panel = GameTimerUISystem.PanelBounds;

        Player[] redPlayers = TeamPlayers(Team.Red);
        Player[] bluePlayers = TeamPlayers(Team.Blue);
        NPC boss = playing ? FindRoundBoss(manager.SelectedBossType) : null;
        long redDamage = TeamBossDamage(boss, Team.Red);
        long blueDamage = TeamBossDamage(boss, Team.Blue);
        string redDamageText = playing ? redDamage.ToString() : "";
        string blueDamageText = playing ? blueDamage.ToString() : "";
        Rectangle redPanel = TeamPanel(panel, Team.Red, redPlayers.Length, redDamageText);
        Rectangle bluePanel = TeamPanel(panel, Team.Blue, bluePlayers.Length, blueDamageText);

        Player[] nonePlayers = TeamPlayers(Team.None);
        int noneWidth = Math.Clamp(HeadPadding * 2 + nonePlayers.Length * HeadSize, TeamHeight, MaxTeamWidth);
        int noneX = (bluePlayers.Length > 0 ? bluePanel.Right : panel.Right) + PanelGap;
        Rectangle nonePanel = new(noneX, panel.Y, noneWidth, TeamHeight);

        Rectangle hoverBounds = panel;
        if (redPlayers.Length > 0)
            hoverBounds = Rectangle.Union(hoverBounds, redPanel);
        if (bluePlayers.Length > 0)
            hoverBounds = Rectangle.Union(hoverBounds, bluePanel);
        if (nonePlayers.Length > 0)
            hoverBounds = Rectangle.Union(hoverBounds, nonePanel);

        bool hovered = hoverBounds.Contains(GameTimerUISystem.MousePoint);
        opacity = MathHelper.Lerp(opacity, hovered ? .3f : 1f, 1f / 12f);
        if (redPlayers.Length > 0)
            DrawTeamPanel(redPanel, Team.Red, redPlayers, redDamageText, redDamage, playing);
        if (bluePlayers.Length > 0)
            DrawTeamPanel(bluePanel, Team.Blue, bluePlayers, blueDamageText, blueDamage, playing);
        if (nonePlayers.Length > 0)
            DrawNoTeamPanel(nonePanel, nonePlayers);

        return true;
    }

    internal static void DrawTimerDecoration(RoundManager manager, Rectangle panel, float alpha)
    {
        if (manager.CurrentPhase is RoundManager.RoundPhase.FreezeCountdown or RoundManager.RoundPhase.Playing)
        {
            const int iconSize = 38;
            Rectangle icon = new(panel.Center.X - iconSize / 2, panel.Center.Y - iconSize / 2, iconSize, iconSize);
            BossVoteDrawer.DrawBossHead(manager.SelectedBossType, icon, .5f * alpha);
        }
        if (manager.CurrentPhase == RoundManager.RoundPhase.VotingOrEndScreen
            && (Team)Main.LocalPlayer.team is not (Team.Red or Team.Blue))
        {
            Texture2D icons = TextureAssets.Pvp[1].Value;
            DrawTeamIcon(icons, Team.Red, new Vector2(panel.X + ChooseTeamSidePadding / 2f, panel.Center.Y), alpha);
            DrawTeamIcon(icons, Team.Blue, new Vector2(panel.Right - ChooseTeamSidePadding / 2f, panel.Center.Y), alpha);
        }
    }
    private static Player[] TeamPlayers(Team team) => Main.player
        .Where(player => player?.active == true && (Team)player.team == team)
        .ToArray();

    private static Rectangle TeamPanel(Rectangle statusPanel, Team team, int playerCount, string damage)
    {
        int width = string.IsNullOrEmpty(damage)
            ? Math.Clamp(HeadPadding * 2 + playerCount * HeadSize, TeamHeight, MaxTeamWidth)
            : DamagePanelWidth;
        int x = team == Team.Red
            ? statusPanel.X - PanelGap - width
            : statusPanel.Right + PanelGap;
        return new Rectangle(x, statusPanel.Y, width, TeamHeight);
    }

    private static void DrawTeamPanel(Rectangle panel, Team team, Player[] players, string damageText,
        long damage, bool playing)
    {
        Color teamColor = Main.teamColor[(int)team];
        Utils.DrawInvBG(Main.spriteBatch, panel, teamColor * .72f * opacity);

        if (playing)
        {
            DrawCenteredText(damageText, panel, Color.White * opacity, DamageScale, panel.Width - 16);
            if (panel.Contains(GameTimerUISystem.MousePoint))
            {
                Main.LocalPlayer.mouseInterface = true;
                Main.instance.MouseText($"{team} team boss damage: {damage}");
            }
            return;
        }

        DrawHeadRow(panel, players, teamColor);
    }

    private static void DrawTeamIcon(Texture2D pvpIcons, Team team, Vector2 center, float alpha)
    {
        Rectangle frame = pvpIcons.Frame(6, 1, (int)team);
        frame.Width -= 2;
        Main.spriteBatch.Draw(pvpIcons, center, frame, Color.White * .5f * alpha, 0f,
            frame.Size() / 2f, 1f, Microsoft.Xna.Framework.Graphics.SpriteEffects.None, 0f);
    }

    private static void DrawNoTeamPanel(Rectangle panel, Player[] players)
    {
        Utils.DrawInvBG(Main.spriteBatch, panel, new Color(214, 214, 220) * .55f * opacity);
        DrawHeadRow(panel, players, Color.LightGray);
    }

    private static void DrawHeadRow(Rectangle panel, Player[] players, Color headColor)
    {
        float firstCenter = panel.X + HeadPadding + HeadSize / 2f;
        float lastCenter = panel.Right - HeadPadding - HeadSize / 2f;
        float step = players.Length <= 1 ? 0f : (lastCenter - firstCenter) / (players.Length - 1);
        for (int i = 0; i < players.Length; i++)
        {
            Vector2 center = new(firstCenter + step * i, panel.Center.Y);
            Rectangle hitbox = new((int)center.X - HeadSize / 2, panel.Center.Y - HeadSize / 2,
                HeadSize, HeadSize);
            Main.MapPlayerRenderer.DrawPlayerHead(Main.Camera, players[i],
                center - new Vector2(2f, 2f), opacity, .6f * HeadSize / 26f, headColor * opacity);

            if (!hitbox.Contains(GameTimerUISystem.MousePoint))
                continue;

            Main.LocalPlayer.mouseInterface = true;
            Main.instance.MouseText($"{players[i].name} ({TeamLabel((Team)players[i].team)})");
        }
    }

    private static string TeamLabel(Team team) => team == Team.None ? "No Team" : $"{team} Team";

    private static long TeamBossDamage(NPC boss, Team team)
    {
        if (boss == null || !boss.GetGlobalNPC<TeamBossNPC>().TeamLife.TryGetValue(team, out int life))
            return 0;

        return Math.Max(0L, (long)boss.lifeMax - life);
    }

    private static NPC FindRoundBoss(int bossType)
    {
        NPC fallback = null;
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (npc?.active != true || !npc.boss || npc.type != bossType || npc.realLife >= 0)
                continue;

            if (npc.GetGlobalNPC<TeamBossNPC>().TeamLife.Count > 0)
                return npc;
            fallback ??= npc;
        }

        return fallback;
    }

    private static Vector2 MeasureText(string value, float scale) =>
        FontAssets.MouseText.Value.MeasureString(value) * scale;

    private static void DrawCenteredText(string value, Rectangle panel, Color color, float scale, float maxWidth = 0f)
    {
        float width = MeasureText(value, scale).X;
        if (maxWidth > 0f && width > maxWidth)
            scale *= maxWidth / width;

        Vector2 size = MeasureText(value, scale);
        Utils.DrawBorderString(Main.spriteBatch, value,
            new Vector2(panel.Center.X, panel.Center.Y - size.Y / 2f + 4f), color, scale, .5f);
    }
}
