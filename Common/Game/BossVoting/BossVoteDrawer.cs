using PvPArenas.Common.Game.LoadoutSelector;
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.Graphics.Renderers;
using Terraria.ID;
using Terraria.Localization;
using static PvPArenas.Common.UI.ArenaUIStyle;

namespace PvPArenas.Common.Game.BossVoting;

/// <summary>ErkySSC's vote presentation adapted to the permanent arena choices.</summary>
internal static class BossVoteDrawer
{
    private static Texture2D PanelBackground => Main.Assets.Request<Texture2D>("Images/UI/PanelBackground").Value;
    private static Texture2D PanelBorder => Main.Assets.Request<Texture2D>("Images/UI/PanelBorder").Value;
    private static readonly RasterizerState ClipRasterizer = new() { CullMode = CullMode.None, ScissorTestEnable = true };
    private static float opacity = 1f;

    internal static ArenaVoteLayoutArgs LayoutArgs(ArenaVoteUISettings settings) =>
        new(Main.screenWidth, Main.screenHeight, FightPresets.Count, settings);

    // Public geometry entry points stay fresh for external callers; Draw uses one shared snapshot.
    internal static Rectangle ActivePanel() => ArenaVoteGeometry.ActivePanel(LayoutArgs(ArenaVoteUILayout.Create()));
    internal static Rectangle ChoiceBox(Rectangle panel, int index) =>
        ArenaVoteGeometry.ChoiceBox(panel, index, LayoutArgs(ArenaVoteUILayout.Create()));
    internal static Rectangle ResultPanel(Rectangle panel) =>
        ArenaVoteGeometry.ResultPanel(panel, LayoutArgs(ArenaVoteUILayout.Create()));
    internal static Rectangle ResultBox(Rectangle panel, int index) =>
        ArenaVoteGeometry.ResultBox(panel, index, LayoutArgs(ArenaVoteUILayout.Create()));
    internal static int VoterHeadSize(Rectangle box) => ArenaVoteGeometry.VoterHeadSize(box, ArenaVoteUILayout.Create());
    internal static Rectangle SelectionLine(Rectangle box) => ArenaVoteGeometry.SelectionLine(box, ArenaVoteUILayout.Create());

    private static Rectangle Blend(Rectangle from, Rectangle to, float amount) => new(
        (int)MathF.Round(MathHelper.Lerp(from.X, to.X, amount)),
        (int)MathF.Round(MathHelper.Lerp(from.Y, to.Y, amount)),
        (int)MathF.Round(MathHelper.Lerp(from.Width, to.Width, amount)),
        (int)MathF.Round(MathHelper.Lerp(from.Height, to.Height, amount)));

    private static string Label(string key) => Language.GetTextValue("Mods.PvPArenas.Voting." + key);

    internal static void Draw(BossVotePresentation presentation, Dictionary<int, Player> heads, float[] hover,
        MapHeadRenderer headRenderer)
    {
        BossVoteSystem vote = ModContent.GetInstance<BossVoteSystem>();
        RoundManager manager = ModContent.GetInstance<RoundManager>();
        ArenaVoteUISettings ui = ArenaVoteUILayout.Create();
        ArenaVoteLayoutArgs layout = LayoutArgs(ui);
        ArenaVoteAnimationState animation = presentation.Animation(ui);
        Rectangle activePanel = ArenaVoteGeometry.ActivePanel(layout);
        float transition = Ease(animation.ResultTransition);
        Rectangle panel = presentation.Complete ? Blend(activePanel, ArenaVoteGeometry.ResultPanel(activePanel, layout), transition) : activePanel;
        float opening = Ease(animation.Opening), closing = animation.Closing;
        float verticalFraction = Math.Max(.001f, ui.ClosingVerticalFraction);
        float height = MathHelper.Lerp(panel.Height, ui.ClosingTargetHeight,
            Ease(Math.Clamp(closing / verticalFraction, 0f, 1f)));
        float width = panel.Width * (1f - Ease(Math.Clamp((closing - verticalFraction)
            / Math.Max(.001f, ui.ClosingHorizontalFraction), 0f, 1f)));
        Rectangle clip = new(panel.Center.X - (int)width / 2, panel.Center.Y - (int)(height * opening) / 2,
            Math.Max(1, (int)width), Math.Max(1, (int)(height * opening)));
        GraphicsDevice device = Main.spriteBatch.GraphicsDevice;
        Rectangle oldClip = device.ScissorRectangle;
        RasterizerState oldRasterizer = device.RasterizerState;
        BlendState oldBlend = device.BlendState;
        SamplerState oldSampler = device.SamplerStates[0];
        Vector2 first = Vector2.Transform(clip.TopLeft(), Main.UIScaleMatrix);
        Vector2 last = Vector2.Transform(clip.BottomRight(), Main.UIScaleMatrix);
        Rectangle screenClip = new((int)first.X, (int)first.Y,
            Math.Max(1, (int)(last.X - first.X)), Math.Max(1, (int)(last.Y - first.Y)));
        Main.spriteBatch.End();
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp,
            DepthStencilState.None, ClipRasterizer, null, Main.UIScaleMatrix);
        device.ScissorRectangle = Rectangle.Intersect(oldRasterizer.ScissorTestEnable ? oldClip : device.Viewport.Bounds, screenClip);
        opacity = opening * (1f - Ease(closing)) * Math.Clamp(ui.Opacity, 0f, 1f);
        try
        {
            Point mouse = new(Main.mouseX, Main.mouseY);
            bool interactive = animation.Interactive && vote.Active && !PlayerInput.IgnoreMouseInterface;
            if (panel.Contains(mouse)) Main.LocalPlayer.mouseInterface = true;
            DrawPanel(panel, ui.PanelFillColor, ui.PanelBorderColor, Math.Max(1, ui.PanelCornerRadius));
            bool compactHeader = ArenaVoteGeometry.CardsTop(activePanel, layout) < ui.OptionsTop && !presentation.Complete;
            Header(Label(presentation.Complete ? "CompleteHeader" : "ActiveHeader"),
                new Vector2(panel.Center.X + ui.HeaderTextXOffset,
                    panel.Y + (compactHeader ? ui.CompactHeaderTextYOffset : ui.HeaderTextYOffset)),
                panel.Width - ui.HeaderTextPadding * 2,
                compactHeader ? ui.CompactHeaderTextScale : ui.HeaderTextScale, ui.TitleColor);
            if (!presentation.Complete)
            {
                Text(Label("ChooseBoss"), new Vector2(panel.Center.X + ui.SubtitleTextXOffset,
                    panel.Y + (compactHeader ? ui.CompactSubtitleTextYOffset : ui.SubtitleTextYOffset)),
                    ui.TextColor, compactHeader ? ui.CompactSubtitleTextScale : ui.SubtitleTextScale,
                    panel.Width - ui.SubtitleTextPadding * 2);
                Rectangle track = new(panel.X + ui.ProgressXPadding,
                    panel.Y + ArenaVoteGeometry.CardsTop(activePanel, layout) - ui.ProgressYOffsetFromOptions,
                    Math.Max(1, panel.Width - ui.ProgressXPadding * 2), Math.Max(1, ui.ProgressHeight));
                float progress = Math.Clamp(manager.RemainingTicks / (float)Math.Max(1, vote.DurationTicks), 0f, 1f);
                Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, track, ui.ProgressTrackColor * (ui.ProgressTrackOpacity * opacity));
                Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value,
                    new Rectangle(track.X, track.Y, (int)(track.Width * progress), track.Height), ui.ProgressColor * opacity);
            }

            float panelOpacity = opacity;
            // The winning tile draws last so fading choices cannot cover it.
            for (int step = 0; step < FightPresets.Count; step++)
            {
                int index = presentation.Complete
                    ? step == FightPresets.Count - 1 ? presentation.Winner : step >= presentation.Winner ? step + 1 : step
                    : step;
                bool resultChoice = presentation.Complete && index == presentation.Winner;
                if (presentation.Complete && !resultChoice && transition >= 1f) continue;
                Rectangle box = ArenaVoteGeometry.ChoiceBox(activePanel, index, layout);
                if (resultChoice)
                    box = Blend(box, ArenaVoteGeometry.ResultBox(activePanel, index, layout), transition);
                opacity = panelOpacity * (presentation.Complete && !resultChoice ? 1f - transition : 1f);
                DrawChoice(vote, box, index, mouse, interactive, hover[index] * (1f - transition), resultChoice, ui);
                DrawVoterHeads(box, vote.VotersFor(index), mouse, heads, headRenderer,
                    closing == 0f && (!presentation.Complete || resultChoice && transition >= 1f),
                    resultChoice ? transition : 0f, ui);
                opacity = panelOpacity;
            }
            if (closing > 0f)
                Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value,
                    new Rectangle(panel.X + ui.ClosingLineXPadding, panel.Center.Y,
                        Math.Max(1, panel.Width - ui.ClosingLineXPadding * 2), Math.Max(1, ui.ClosingLineHeight)),
                    ui.AccentColor * (Ease(Math.Clamp(closing / verticalFraction, 0f, 1f)) * opacity));
        }
        finally
        {
            opacity = 1f;
            Main.spriteBatch.End();
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, oldBlend, oldSampler,
                DepthStencilState.None, oldRasterizer, null, Main.UIScaleMatrix);
            device.ScissorRectangle = oldClip;
        }
    }

    private static void DrawChoice(BossVoteSystem vote, Rectangle box, int index, Point mouse,
        bool interactive, float hover, bool resultChoice, ArenaVoteUISettings ui)
    {
        bool available = FightPresets.IsAvailable(index);
        interactive &= available;
        if (!available) hover = 0f;
        bool selected = resultChoice || vote.LocalVote == index;
        bool compact = box.Height < ui.CompactOptionThreshold;
        int titleY = compact ? ui.CompactOptionTextYOffset : ui.OptionTextYOffset;
        int iconSize = compact ? ui.CompactBossIconSize : ui.BossIconSize;
        int nameLeft = compact ? ui.CompactOptionTextXOffset : ui.OptionTextXOffset;
        float textScale = compact ? ui.CompactOptionTextScale : ui.OptionTextScale;
        Color fill = ArenaVoteUILayout.ChoiceFill(ui, hover, selected);
        DrawPanel(box, fill, Color.Transparent, Math.Max(1, ui.OptionCornerRadius));
        if (selected)
            Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, ArenaVoteGeometry.SelectionLine(box, ui), ui.AccentColor * opacity);
        DrawBossHead(FightPresets.BossType(index),
            new Rectangle(box.X + ui.BossIconXOffset, box.Y + (compact ? ui.CompactBossIconYOffset : ui.BossIconYOffset), iconSize, iconSize),
            opacity * (available ? 1f : ui.UnavailableBossIconOpacity), ui.BossIconPadding, ui.BossIconScale, ui.BossIconColor);
        Text(Lang.GetNPCNameValue(FightPresets.BossType(index)), new Vector2(box.X + nameLeft, box.Y + titleY),
            !available ? ui.UnavailableTextColor : selected ? ui.AccentColor : ui.TextColor, textScale, box.Width - nameLeft - ui.VoteTextReservedWidth, 0f);
        Text(available ? vote.VoteCount(index).ToString() : "—",
            new Vector2(box.Right - ui.VoteTextRightPadding, box.Y + titleY + ui.VoteTextYOffset),
            ui.AccentColor, textScale * ui.VoteTextScaleMultiplier, ui.VoteTextWidth, 1f);
        if (!available && box.Height >= ui.UnavailableTextMinimumHeight)
            Text(Label("MapUnavailable"), new Vector2(box.X + ui.UnavailableTextXOffset, box.Y + ui.UnavailableTextYOffset),
                ui.UnavailableTextColor, ui.UnavailableTextScale, box.Width - ui.UnavailableTextXOffset - ui.UnavailableTextRightPadding, 0f);
        if (!interactive || !box.Contains(mouse) || !Main.mouseLeft || !Main.mouseLeftRelease) return;
        Main.mouseLeftRelease = false;
        SoundEngine.PlaySound(SoundID.MenuTick);
        BossVoteSystem.RequestVote(index);
    }

    private static void DrawVoterHeads(Rectangle box, IReadOnlyList<byte> voters, Point mouse,
        Dictionary<int, Player> heads, MapHeadRenderer headRenderer, bool tooltips, float centered, ArenaVoteUISettings ui)
    {
        int size = ArenaVoteGeometry.VoterHeadSize(box, ui);
        if (size == 0) return;
        int gap = Math.Max(0, ui.VoterHeadGap), step = size + gap;
        int capacity = Math.Max(1, (box.Width - ui.VoterHeadXPadding * 2 + gap) / step);
        int visible = Math.Min(voters.Count, voters.Count > capacity ? capacity - 1 : capacity);
        int cells = visible + (voters.Count > visible ? 1 : 0);
        int left = (int)MathF.Round(MathHelper.Lerp(box.X + ui.VoterHeadXPadding,
            box.Center.X - (cells * step - gap) / 2 + ui.CenteredVoterHeadsXOffset, centered));
        string NameOf(int id) => heads.TryGetValue(id, out Player player) ? player.name : "";
        for (int i = 0; i < visible; i++)
        {
            int id = voters[i];
            Rectangle tile = new(left + i * step, box.Y + ui.VoterHeadsTop, size, size);
            if (heads.TryGetValue(id, out Player player))
                headRenderer.DrawPlayerHead(Main.Camera, player,
                    tile.Center.ToVector2() + new Vector2(ui.VoterHeadXOffset, ui.VoterHeadYOffset), opacity, size / 40f * ui.VoterHeadScale,
                    Main.teamColor[Math.Clamp(player.team, 0, Main.teamColor.Length - 1)]);
            if (tooltips && tile.Contains(mouse)) Main.instance.MouseText(NameOf(id));
        }
        if (voters.Count <= visible) return;
        Rectangle more = new(left + visible * step, box.Y + ui.VoterHeadsTop, size, size);
        Text($"+{voters.Count - visible}",
            more.Center.ToVector2() + new Vector2(ui.OverflowTextXOffset, ui.OverflowTextYOffset),
            ui.OverflowTextColor, ui.OverflowTextScale, Math.Max(1, size - ui.OverflowTextWidthPadding * 2));
        if (tooltips && more.Contains(mouse))
            Main.instance.MouseText(string.Join(", ", voters.Skip(visible).Select(id => NameOf(id))));
    }

    internal static void DrawBossHead(int type, Rectangle box, float opacity = 1f, float padding = 8f, float iconScale = 1f, Color? color = null)
    {
        int head = type >= 0 && type < NPCID.Sets.BossHeadTextures.Length
            ? NPCID.Sets.BossHeadTextures[type]
            : -1;
        if (head >= 0 && head < TextureAssets.NpcHeadBoss.Length)
        {
            Texture2D texture = TextureAssets.NpcHeadBoss[head].Value;
            float scale = Math.Max(0f, Math.Min((box.Width - padding) / texture.Width, (box.Height - padding) / texture.Height) * iconScale);
            Main.spriteBatch.Draw(texture, box.Center.ToVector2(), null, (color ?? Color.White) * opacity, 0f,
                texture.Size() / 2f, scale, SpriteEffects.None, 0f);
            return;
        }

        if (type <= 0 || type >= TextureAssets.Npc.Length)
            return;

        Main.instance.LoadNPC(type);
        Texture2D npc = TextureAssets.Npc[type].Value;
        Rectangle source = new(0, 0, npc.Width, npc.Height / Math.Max(1, Main.npcFrameCount[type]));
        float fallbackScale = Math.Max(0f, Math.Min((box.Width - padding) / source.Width, (box.Height - padding) / source.Height) * iconScale);
        Main.spriteBatch.Draw(npc, box.Center.ToVector2(), source, (color ?? Color.White) * opacity, 0f,
            source.Size() / 2f, fallbackScale, SpriteEffects.None, 0f);
    }


    private static void DrawPanel(Rectangle rectangle, Color fill, Color edge, int corner)
    {
        Utils.DrawSplicedPanel(Main.spriteBatch, PanelBackground, rectangle.X, rectangle.Y,
            rectangle.Width, rectangle.Height, corner, corner, corner, corner, fill * opacity);
        Utils.DrawSplicedPanel(Main.spriteBatch, PanelBorder, rectangle.X, rectangle.Y,
            rectangle.Width, rectangle.Height, corner, corner, corner, corner, edge * opacity);
    }

    private static void Header(string value, Vector2 position, float maxWidth, float maxScale, Color color)
    {
        float scale = Math.Min(maxScale, maxWidth / Math.Max(1f, FontAssets.DeathText.Value.MeasureString(value).X));
        Utils.DrawBorderStringBig(Main.spriteBatch, value, position, color * opacity, scale, .5f, 0f);
    }

    private static void Text(string value, Vector2 position, Color color, float scale, float maxWidth, float anchor = .5f)
    {
        float width = FontAssets.MouseText.Value.MeasureString(value).X * scale;
        if (width > maxWidth) scale *= maxWidth / width;
        Utils.DrawBorderString(Main.spriteBatch, value, position, color * opacity, scale, anchor);
    }
}
