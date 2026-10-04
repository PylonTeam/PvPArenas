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

        // Mouse text is reset during drawing; submit it on every rendered frame.
        if (IsMouseHovering)
        {
            string value = tooltip?.Invoke();
            if (!string.IsNullOrWhiteSpace(value))
                Main.instance.MouseText(value);
        }
    }

    private bool Enabled => enabled?.Invoke() ?? true;
}
