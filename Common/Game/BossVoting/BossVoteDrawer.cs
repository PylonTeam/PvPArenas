using PvPArenas.Common.Game.LoadoutSelector;
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;

namespace PvPArenas.Common.Game.BossVoting;

/// <summary>ErkySSC's vote presentation adapted to four permanent arena choices.</summary>
internal static class BossVoteDrawer
{
    private static readonly Color Yellow = new(246, 216, 72);
    private static readonly Color Accent = new(153, 218, 158);
    private static Texture2D PanelBackground => Main.Assets.Request<Texture2D>("Images/UI/PanelBackground").Value;
    private static Texture2D PanelBorder => Main.Assets.Request<Texture2D>("Images/UI/PanelBorder").Value;
    private static readonly RasterizerState ClipRasterizer = new() { CullMode = CullMode.None, ScissorTestEnable = true };
    private static float opacity = 1f;

    internal static Rectangle ActivePanel()
    {
        int width = Math.Min(452, Main.screenWidth - 12);
        int top = Math.Max(6, Math.Min(172, Main.screenHeight - 277 - 6));
        return new Rectangle((Main.screenWidth - width) / 2, top, width, Math.Min(277, Main.screenHeight - top - 4));
    }

    internal static Rectangle ChoiceBox(Rectangle panel, int index) =>
        new(panel.X + 12 + index % 2 * (panel.Width - 18) / 2,
            panel.Y + 105 + index / 2 * 83, (panel.Width - 30) / 2, 77);

    private static float Ease(float t) => t * t * (3f - 2f * t);
    private static string Label(string key) => Language.GetTextValue("Mods.PvPArenas.Voting." + key);

    internal static void Draw(BossVotePresentation presentation, Dictionary<int, Player> heads, float[] hover)
    {
        BossVoteSystem vote = ModContent.GetInstance<BossVoteSystem>();
        RoundManager manager = ModContent.GetInstance<RoundManager>();
        Rectangle panel = ActivePanel();
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
            DrawPanel(panel, new Color(25, 34, 66), new Color(81, 99, 151), 10);
            Header(Label(presentation.Complete ? "CompleteHeader" : "ActiveHeader"),
                new Vector2(panel.Center.X, panel.Y + 5), panel.Width - 28);
            Text(presentation.Complete ? Lang.GetNPCNameValue(FightPresets.BossType(presentation.Winner)) : Label("ChooseBoss"),
                new Vector2(panel.X + 15, panel.Y + 38), Color.White, .94f, panel.Width - 30, 0f);
            Text(Language.GetTextValue("Mods.PvPArenas.Voting.VoteCount", vote.TotalVotes),
                new Vector2(panel.X + 15, panel.Y + 68), Color.Silver, .7f, panel.Width - 110, 0f);
            int seconds = Math.Max(0, (manager.RemainingTicks + 59) / 60);
            string timer = presentation.Complete ? Label("Selected") : $"{seconds / 60}:{seconds % 60:00}";
            Text(timer, new Vector2(panel.Right - 15, panel.Y + 68),
                presentation.Complete ? Accent : Color.Silver, .7f, 76f, 1f);
            Rectangle track = new(panel.X + 15, panel.Y + 91, panel.Width - 30, 3);
            float progress = presentation.Complete ? 0f
                : Math.Clamp(manager.RemainingTicks / (float)Math.Max(1, vote.DurationTicks), 0f, 1f);
            Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, track, Color.Black * (.3f * opacity));
            Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value,
                new Rectangle(track.X, track.Y, (int)(track.Width * progress), track.Height),
                new Color(149, 175, 220) * opacity);

            float transition = Ease(presentation.ResultTransition), panelOpacity = opacity;
            // The winning tile draws last so fading choices cannot cover it.
            for (int step = 0; step < FightPresets.Count; step++)
            {
                int index = presentation.Complete
                    ? step == FightPresets.Count - 1 ? presentation.Winner : step >= presentation.Winner ? step + 1 : step
                    : step;
                bool resultChoice = presentation.Complete && index == presentation.Winner;
                if (presentation.Complete && !resultChoice && transition >= 1f) continue;
                Rectangle box = ChoiceBox(panel, index);
                if (resultChoice)
                {
                    box.X = (int)MathF.Round(MathHelper.Lerp(box.X, panel.Center.X - box.Width / 2, transition));
                    box.Y = (int)MathF.Round(MathHelper.Lerp(box.Y, panel.Y + 146, transition));
                }
                opacity = panelOpacity * (presentation.Complete && !resultChoice ? 1f - transition : 1f);
                DrawChoice(vote, box, index, mouse, interactive, hover[index] * (1f - transition), resultChoice);
                DrawVoterHeads(box, vote.VotersFor(index), mouse, heads,
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
        bool selected = resultChoice || vote.LocalVote == index;
        Color fill = Color.Lerp(new Color(33, 44, 78), new Color(52, 67, 108), hover);
        if (selected) fill = Color.Lerp(fill, Accent, .14f);
        DrawPanel(box, fill, Color.Transparent, 8);
        if (selected)
            Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value,
                new Rectangle(box.X + 10, box.Y + 28, box.Width - 20, 2), Accent * opacity);
        DrawBossHead(FightPresets.BossType(index), new Rectangle(box.X + 7, box.Y + 2, 28, 28), opacity);
        Text(Lang.GetNPCNameValue(FightPresets.BossType(index)), new Vector2(box.X + 38, box.Y + 8),
            selected ? Accent : Color.White, .82f, box.Width - 82, 0f);
        Text(vote.VoteCount(index).ToString(), new Vector2(box.Right - 11, box.Y + 8), Accent, .82f, 35f, 1f);
        if (!interactive || !box.Contains(mouse) || !Main.mouseLeft || !Main.mouseLeftRelease) return;
        Main.mouseLeftRelease = false;
        SoundEngine.PlaySound(SoundID.MenuTick);
        BossVoteSystem.RequestVote(index);
    }

    private static void DrawVoterHeads(Rectangle box, IReadOnlyList<byte> voters, Point mouse,
        Dictionary<int, Player> heads, bool tooltips, float centered)
    {
        const int size = 40;
        int capacity = Math.Max(1, (box.Width - 16) / size);
        int visible = Math.Min(voters.Count, voters.Count > capacity ? capacity - 1 : capacity);
        int cells = visible + (voters.Count > visible ? 1 : 0);
        int left = (int)MathF.Round(MathHelper.Lerp(box.X + 8, box.Center.X - cells * size / 2, centered));
        string NameOf(int id) => heads.TryGetValue(id, out Player player) ? player.name : "";
        for (int i = 0; i < visible; i++)
        {
            int id = voters[i];
            Rectangle tile = new(left + i * size, box.Y + 33, size, size);
            if (heads.TryGetValue(id, out Player player))
                Main.MapPlayerRenderer.DrawPlayerHead(Main.Camera, player,
                    tile.Center.ToVector2() - new Vector2(2f), opacity, 1f,
                    Main.teamColor[Math.Clamp(player.team, 0, Main.teamColor.Length - 1)]);
            if (tooltips && tile.Contains(mouse)) Main.instance.MouseText(NameOf(id));
        }
        if (voters.Count <= visible) return;
        Rectangle more = new(left + visible * size, box.Y + 33, size, size);
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

    private static void Header(string value, Vector2 position, float maxWidth)
    {
        float scale = Math.Min(.63f, maxWidth / Math.Max(1f, FontAssets.DeathText.Value.MeasureString(value).X));
        Utils.DrawBorderStringBig(Main.spriteBatch, value, position, Yellow * opacity, scale, .5f, 0f);
    }

    private static void Text(string value, Vector2 position, Color color, float scale, float maxWidth, float anchor = .5f)
    {
        float width = FontAssets.MouseText.Value.MeasureString(value).X * scale;
        if (width > maxWidth) scale *= maxWidth / width;
        Utils.DrawBorderString(Main.spriteBatch, value, position, color * opacity, scale, anchor);
    }
}
