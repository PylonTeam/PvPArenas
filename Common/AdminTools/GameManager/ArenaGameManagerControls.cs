using PvPArenas.Common.Game;
using PvPArenas.Common.AdminTools.UI;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria.Audio;
using Terraria.Enums;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.Localization;
using Terraria.UI;

namespace PvPArenas.Common.AdminTools.GameManager;

internal sealed class ArenaGameStatusPanel : UIPanel
{
    internal ArenaGameStatusPanel()
    {
        SetPadding(0f);
        BackgroundColor = new Color(20, 27, 62) * .95f;
        BorderColor = new Color(78, 104, 190) * .8f;
    }

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        base.DrawSelf(spriteBatch);
        Rectangle panel = GetDimensions().ToRectangle();
        RoundManager manager = ModContent.GetInstance<RoundManager>();
        string phase = manager.CurrentPhase == RoundManager.RoundPhase.WaitingForPlayers && manager.IsIdleHeld
            ? "Waiting"
            : PhaseName(manager.CurrentPhase);
        if (!string.IsNullOrEmpty(manager.PreparationFailure))
        {
            phase = Language.GetTextValue("Mods.PvPArenas.Round.PreparationFailedStatus");
            if (new Rectangle(panel.X, panel.Y, panel.Width, 32).Contains(Main.MouseScreen.ToPoint()))
                Main.instance.MouseText(Language.GetTextValue(
                    "Mods.PvPArenas.Round.PreparationFailed", manager.PreparationFailure));
        }
        if (manager.IsTimerPaused)
            phase += " (paused)";

        const float scale = .72f;
        const string label = "Current phase:";
        Utils.DrawBorderString(spriteBatch, label, new Vector2(panel.X + 11, panel.Y + 8),
            new Color(174, 216, 226), scale);
        float labelWidth = FontAssets.MouseText.Value.MeasureString(label).X * scale;
        DrawText(spriteBatch, phase, new Vector2(panel.X + 11 + labelWidth + 6, panel.Y + 8), Color.White,
            scale, panel.Width - labelWidth - 106f);

        if (!IsTimed(manager.CurrentPhase))
            return;

        int seconds = Math.Max(0, (int)Math.Ceiling(manager.RemainingTicks / 60f));
        DrawText(spriteBatch, $"{seconds / 60}:{seconds % 60:00}",
            new Vector2(panel.Right - 11, panel.Y + 8), Color.White, scale, 68f, 1f);
    }

    private static string PhaseName(RoundManager.RoundPhase phase) => phase switch
    {
        RoundManager.RoundPhase.WaitingForPlayers => "Waiting for players",
        RoundManager.RoundPhase.VotingOrEndScreen => "Voting",
        RoundManager.RoundPhase.Generating => "Preparing arena",
        RoundManager.RoundPhase.FreezeCountdown => "Starting round",
        RoundManager.RoundPhase.Playing => "Round in progress",
        _ => "Waiting"
    };

    private static bool IsTimed(RoundManager.RoundPhase phase) =>
        phase is RoundManager.RoundPhase.VotingOrEndScreen
            or RoundManager.RoundPhase.FreezeCountdown
            or RoundManager.RoundPhase.Playing;

    private static void DrawText(SpriteBatch spriteBatch, string text, Vector2 position, Color color,
        float scale, float maxWidth, float anchor = 0f)
    {
        float measured = FontAssets.MouseText.Value.MeasureString(text).X * scale;
        if (measured > maxWidth)
            scale *= maxWidth / measured;
        Utils.DrawBorderString(spriteBatch, text, position, color, scale, anchor);
    }
}

internal sealed class ArenaTeamBalancePanel : UIPanel
{
    internal ArenaTeamBalancePanel()
    {
        SetPadding(0f);
        BackgroundColor = new Color(20, 27, 62) * .95f;
        BorderColor = new Color(78, 104, 190) * .8f;
    }

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        base.DrawSelf(spriteBatch);
        Rectangle panel = GetDimensions().ToRectangle();
        Player[] active = Main.player.Where(player => player?.active == true).ToArray();
        DrawPlayers(spriteBatch, active, panel.X + 10, panel.Y + 9, panel.Width - 20);
    }

    private static void DrawPlayers(SpriteBatch spriteBatch, Player[] players,
        int x, int y, float maxWidth)
    {
        List<(string Text, Color Color)> spans = [("Players: ", Color.White)];
        if (players.Length == 0)
            spans.Add(("None", Color.Gray));
        else
            for (int i = 0; i < players.Length; i++)
            {
                if (i > 0)
                    spans.Add((", ", Color.White));

                Team team = (Team)players[i].team;
                Color color = team != Team.None && (int)team < Main.teamColor.Length
                    ? Main.teamColor[(int)team]
                    : Color.Gray;
                spans.Add((players[i].name, color));
            }

        const float baseScale = .64f;
        float unscaledWidth = spans.Sum(span => FontAssets.MouseText.Value.MeasureString(span.Text).X);
        float scale = baseScale;
        float width = unscaledWidth * scale;
        if (width > maxWidth)
            scale *= maxWidth / width;

        Vector2 position = new(x, y);
        foreach ((string text, Color color) in spans)
        {
            Utils.DrawBorderString(spriteBatch, text, position, color, scale);
            position.X += FontAssets.MouseText.Value.MeasureString(text).X * scale;
        }
    }
}

internal sealed class ArenaGameCommandButton : UIPanel
{
    private readonly Func<string> label;
    private readonly Func<string> tooltip;
    private readonly Func<bool> enabled;
    private readonly Func<bool> danger;
    private readonly Action action;
    private readonly Func<AdminUIIcon> icon;

    internal ArenaGameCommandButton(Func<string> label, Func<string> tooltip,
        Func<bool> enabled, Func<bool> danger, Action action, Func<AdminUIIcon> icon = null)
    {
        this.label = label;
        this.tooltip = tooltip;
        this.enabled = enabled;
        this.danger = danger;
        this.action = action;
        this.icon = icon;
        SetPadding(0f);
    }

    public override void LeftClick(UIMouseEvent evt)
    {
        base.LeftClick(evt);
        if (!Enabled)
            return;

        SoundEngine.PlaySound(SoundID.MenuTick);
        action?.Invoke();
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        if (!IsMouseHovering)
            return;

        Main.LocalPlayer.mouseInterface = true;
        string value = tooltip?.Invoke();
        if (!string.IsNullOrWhiteSpace(value))
            Main.instance.MouseText(value);
    }

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        bool active = Enabled;
        bool hovered = active && IsMouseHovering;
        bool destructive = active && (danger?.Invoke() ?? false);
        BackgroundColor = !active
            ? new Color(45, 45, 55) * .72f
            : destructive
                ? hovered ? new Color(170, 45, 60) : new Color(120, 35, 45)
                : hovered ? new Color(73, 94, 171) : new Color(55, 74, 140);
        BorderColor = hovered ? Color.Yellow : Color.Black;
        base.DrawSelf(spriteBatch);

        Rectangle panel = GetDimensions().ToRectangle();
        string text = label?.Invoke() ?? "";
        float scale = .84f;
        Vector2 size = FontAssets.MouseText.Value.MeasureString(text) * scale;
        bool hasIcon = icon is not null;
        const float iconSize = 22f;
        const float iconGap = 6f;
        float iconSpace = hasIcon ? iconSize + iconGap : 0f;
        float textWidth = Math.Max(1f, panel.Width - 14f - iconSpace);
        if (size.X > textWidth)
        {
            scale *= textWidth / size.X;
            size = FontAssets.MouseText.Value.MeasureString(text) * scale;
        }

        float contentWidth = size.X + iconSpace;
        float x = panel.Center.X - contentWidth * .5f;
        Color contentColor = active ? Color.White : Color.Gray;
        if (hasIcon)
        {
            VanillaAdminIcons.DrawFitted(spriteBatch, icon(),
                new Rectangle((int)x, panel.Center.Y - (int)(iconSize * .5f), (int)iconSize, (int)iconSize),
                contentColor, allowUpscale: true);
            x += iconSpace;
        }

        Utils.DrawBorderString(spriteBatch, text,
            new Vector2(x, panel.Center.Y - size.Y / 2f + 3f),
            contentColor, scale);
    }

    private bool Enabled => enabled?.Invoke() ?? true;
}
