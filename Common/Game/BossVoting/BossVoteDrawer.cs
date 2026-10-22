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
    private const int Padding = 16, ChoiceGap = 12, ChoiceHeight = 88;
    private const int ChoicesTop = 90, ResultTop = 60, HeadsTop = 46;
    private static float opacity = 1f;

    internal static Rectangle ActivePanel()
    {
        int width = Math.Max(1, Math.Min(500, Main.screenWidth - 12));
        int rows = (FightPresets.Count + Columns(width) - 1) / Columns(width);
        int height = Math.Max(1, Math.Min(ChoicesTop + rows * ChoiceHeight + (rows - 1) * ChoiceGap + Padding,
            Main.screenHeight - 54));
        int top = Math.Max(6, Math.Min(80, Main.screenHeight - height - 6));
        return new Rectangle((Main.screenWidth - width) / 2, top, width, height);
    }

    private static int Columns(int width) => width < 400 ? 1 : 2;

    private static int CardsTop(Rectangle panel)
    {
        int rows = (FightPresets.Count + Columns(panel.Width) - 1) / Columns(panel.Width);
        return Math.Min(ChoicesTop, Math.Max(66, panel.Height - Padding - (rows - 1) * ChoiceGap - rows * 32));
    }

    internal static Rectangle ChoiceBox(Rectangle panel, int index)
    {
        int columns = Columns(panel.Width), rows = (FightPresets.Count + columns - 1) / columns;
        int width = Math.Max(1, (panel.Width - Padding * 2 - ChoiceGap * (columns - 1)) / columns);
        int top = CardsTop(panel);
        int height = Math.Max(1, Math.Min(ChoiceHeight, (panel.Height - top - Padding - (rows - 1) * ChoiceGap) / rows));
        int left = panel.X + Padding + index % columns * (width + ChoiceGap);
        if (index / columns == rows - 1 && FightPresets.Count % columns == 1)
            left = panel.Center.X - width / 2;
        return new Rectangle(left, panel.Y + top + index / columns * (height + ChoiceGap), width, height);
    }

    internal static Rectangle ResultPanel(Rectangle panel)
    {
        int width = Math.Min(panel.Width, Math.Max(300, ChoiceBox(panel, 0).Width + Padding * 2));
        return new(panel.Center.X - width / 2, panel.Y, width,
            Math.Max(1, Math.Min(ResultTop + ChoiceHeight + Padding, Main.screenHeight - panel.Y - 6)));
    }

    internal static Rectangle ResultBox(Rectangle panel, int index)
    {
        Rectangle box = ChoiceBox(panel, index);
        Rectangle result = ResultPanel(panel);
        box.X = result.Center.X - box.Width / 2;
        box.Y = result.Y + ResultTop;
        box.Height = Math.Max(1, Math.Min(ChoiceHeight, result.Height - ResultTop - Padding));
        return box;
    }

    internal static int VoterHeadSize(Rectangle box)
    {
        int size = Math.Min(36, box.Height - HeadsTop - 8);
        return size >= 20 ? size : 0;
    }

    internal static Rectangle SelectionLine(Rectangle box) =>
        new(box.X + 12, box.Y + (box.Height < 48 ? 30 : 36), box.Width - 24, 2);

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
        Rectangle activePanel = ActivePanel();
        float transition = Ease(presentation.ResultTransition);
        Rectangle panel = presentation.Complete ? Blend(activePanel, ResultPanel(activePanel), transition) : activePanel;
        float opening = Ease(presentation.Opening), closing = presentation.Closing;
        float height = MathHelper.Lerp(panel.Height, 2f, Ease(Math.Clamp(closing / .8f, 0f, 1f)));
        float width = panel.Width * (1f - Ease(Math.Clamp((closing - .8f) / .2f, 0f, 1f)));
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
        opacity = opening * (1f - Ease(closing));
        try
        {
            Point mouse = new(Main.mouseX, Main.mouseY);
            bool interactive = presentation.Interactive && vote.Active && !PlayerInput.IgnoreMouseInterface;
            if (panel.Contains(mouse)) Main.LocalPlayer.mouseInterface = true;
            DrawPanel(panel, PanelFill, PanelEdge, 10);
            bool compactHeader = CardsTop(activePanel) < ChoicesTop && !presentation.Complete;
            Header(Label(presentation.Complete ? "CompleteHeader" : "ActiveHeader"),
                new Vector2(panel.Center.X, panel.Y + (compactHeader ? 3 : 8)), panel.Width - Padding * 2,
                compactHeader ? .5f : .65f);
            if (!presentation.Complete)
            {
                Text(Label("ChooseBoss"), new Vector2(panel.Center.X, panel.Y + (compactHeader ? 29 : 45)),
                    Color.White, compactHeader ? .8f : .95f, panel.Width - Padding * 2);
                Rectangle track = new(panel.X + Padding, panel.Y + CardsTop(activePanel) - 15, panel.Width - Padding * 2, 3);
                float progress = Math.Clamp(manager.RemainingTicks / (float)Math.Max(1, vote.DurationTicks), 0f, 1f);
                Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, track, Color.Black * (.3f * opacity));
                Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value,
                    new Rectangle(track.X, track.Y, (int)(track.Width * progress), track.Height), Progress * opacity);
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
                Rectangle box = ChoiceBox(activePanel, index);
                if (resultChoice)
                    box = Blend(box, ResultBox(activePanel, index), transition);
                opacity = panelOpacity * (presentation.Complete && !resultChoice ? 1f - transition : 1f);
                DrawChoice(vote, box, index, mouse, interactive, hover[index] * (1f - transition), resultChoice);
                DrawVoterHeads(box, vote.VotersFor(index), mouse, heads, headRenderer,
                    closing == 0f && (!presentation.Complete || resultChoice && transition >= 1f),
                    resultChoice ? transition : 0f);
                opacity = panelOpacity;
            }
            if (closing > 0f)
                Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value,
                    new Rectangle(panel.X + 10, panel.Center.Y, panel.Width - 20, 2),
                    Accent * (Ease(Math.Clamp(closing / .8f, 0f, 1f)) * opacity));
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
        bool interactive, float hover, bool resultChoice)
    {
        bool available = FightPresets.IsAvailable(index);
        interactive &= available;
        if (!available) hover = 0f;
        bool selected = resultChoice || vote.LocalVote == index;
        bool compact = box.Height < 48;
        int titleY = compact ? 5 : 9, iconSize = compact ? 24 : 28, nameLeft = compact ? 36 : 44;
        float textScale = compact ? .8f : .9f;
        Color fill = ChoiceFill(hover, selected ? 1f : 0f);
        DrawPanel(box, fill, Color.Transparent, 8);
        if (selected)
            Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, SelectionLine(box), Accent * opacity);
        DrawBossHead(FightPresets.BossType(index), new Rectangle(box.X + 9, box.Y + (compact ? 3 : 6), iconSize, iconSize),
            opacity * (available ? 1f : .4f));
        Text(Lang.GetNPCNameValue(FightPresets.BossType(index)), new Vector2(box.X + nameLeft, box.Y + titleY),
            !available ? Color.Gray : selected ? Accent : Color.White, textScale, box.Width - nameLeft - 56, 0f);
        Text(available ? vote.VoteCount(index).ToString() : "—", new Vector2(box.Right - 12, box.Y + titleY),
            Accent, textScale, 32f, 1f);
        if (!available && box.Height >= 68)
            Text(Label("MapUnavailable"), new Vector2(box.X + 12, box.Y + 49), Color.Gray, .7f, box.Width - 24, 0f);
        if (!interactive || !box.Contains(mouse) || !Main.mouseLeft || !Main.mouseLeftRelease) return;
        Main.mouseLeftRelease = false;
        SoundEngine.PlaySound(SoundID.MenuTick);
        BossVoteSystem.RequestVote(index);
    }

    private static void DrawVoterHeads(Rectangle box, IReadOnlyList<byte> voters, Point mouse,
        Dictionary<int, Player> heads, MapHeadRenderer headRenderer, bool tooltips, float centered)
    {
        int size = VoterHeadSize(box);
        if (size == 0) return;
        int capacity = Math.Max(1, (box.Width - 24) / size);
        int visible = Math.Min(voters.Count, voters.Count > capacity ? capacity - 1 : capacity);
        int cells = visible + (voters.Count > visible ? 1 : 0);
        int left = (int)MathF.Round(MathHelper.Lerp(box.X + 12, box.Center.X - cells * size / 2, centered));
        string NameOf(int id) => heads.TryGetValue(id, out Player player) ? player.name : "";
        for (int i = 0; i < visible; i++)
        {
            int id = voters[i];
            Rectangle tile = new(left + i * size, box.Y + HeadsTop, size, size);
            if (heads.TryGetValue(id, out Player player))
                headRenderer.DrawPlayerHead(Main.Camera, player,
                    tile.Center.ToVector2() - new Vector2(2f), opacity, size / 40f,
                    Main.teamColor[Math.Clamp(player.team, 0, Main.teamColor.Length - 1)]);
            if (tooltips && tile.Contains(mouse)) Main.instance.MouseText(NameOf(id));
        }
        if (voters.Count <= visible) return;
        Rectangle more = new(left + visible * size, box.Y + HeadsTop, size, size);
        Text($"+{voters.Count - visible}", more.Center.ToVector2() - new Vector2(0f, 8f), Color.Silver, .72f, size);
        if (tooltips && more.Contains(mouse))
            Main.instance.MouseText(string.Join(", ", voters.Skip(visible).Select(id => NameOf(id))));
    }

    internal static void DrawBossHead(int type, Rectangle box, float opacity = 1f)
    {
        int head = type >= 0 && type < NPCID.Sets.BossHeadTextures.Length
            ? NPCID.Sets.BossHeadTextures[type]
            : -1;
        if (head >= 0 && head < TextureAssets.NpcHeadBoss.Length)
        {
            Texture2D texture = TextureAssets.NpcHeadBoss[head].Value;
            float scale = Math.Min((box.Width - 8f) / texture.Width, (box.Height - 8f) / texture.Height);
            Main.spriteBatch.Draw(texture, box.Center.ToVector2(), null, Color.White * opacity, 0f,
                texture.Size() / 2f, scale, SpriteEffects.None, 0f);
            return;
        }

        if (type <= 0 || type >= TextureAssets.Npc.Length)
            return;

        Main.instance.LoadNPC(type);
        Texture2D npc = TextureAssets.Npc[type].Value;
        Rectangle source = new(0, 0, npc.Width, npc.Height / Math.Max(1, Main.npcFrameCount[type]));
        float fallbackScale = Math.Min((box.Width - 8f) / source.Width, (box.Height - 8f) / source.Height);
        Main.spriteBatch.Draw(npc, box.Center.ToVector2(), source, Color.White * opacity, 0f,
            source.Size() / 2f, fallbackScale, SpriteEffects.None, 0f);
    }


    private static void DrawPanel(Rectangle rectangle, Color fill, Color edge, int corner)
    {
        Utils.DrawSplicedPanel(Main.spriteBatch, PanelBackground, rectangle.X, rectangle.Y,
            rectangle.Width, rectangle.Height, corner, corner, corner, corner, fill * opacity);
        Utils.DrawSplicedPanel(Main.spriteBatch, PanelBorder, rectangle.X, rectangle.Y,
            rectangle.Width, rectangle.Height, corner, corner, corner, corner, edge * opacity);
    }

    private static void Header(string value, Vector2 position, float maxWidth, float maxScale)
    {
        float scale = Math.Min(maxScale, maxWidth / Math.Max(1f, FontAssets.DeathText.Value.MeasureString(value).X));
        Utils.DrawBorderStringBig(Main.spriteBatch, value, position, Title * opacity, scale, .5f, 0f);
    }

    private static void Text(string value, Vector2 position, Color color, float scale, float maxWidth, float anchor = .5f)
    {
        float width = FontAssets.MouseText.Value.MeasureString(value).X * scale;
        if (width > maxWidth) scale *= maxWidth / width;
        Utils.DrawBorderString(Main.spriteBatch, value, position, color * opacity, scale, anchor);
    }
}
