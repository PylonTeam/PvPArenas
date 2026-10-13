using PvPArenas.Common.Game.LoadoutSelector;
using PvPArenas.Common.UI;
using PvPFramework.Common.EndScreen;
using System;
using System.Collections.Generic;
using Terraria.GameInput;
using Terraria.Graphics.Renderers;
using Terraria.UI;

namespace PvPArenas.Common.Game.BossVoting;

[Autoload(Side = ModSide.Client)]
internal sealed class BossVoteUISystem : ModSystem
{
    private readonly BossVotePresentation presentation = new();
    private readonly Dictionary<int, Player> heads = [];
    private readonly MapHeadRenderer headRenderer = new();
    private readonly float[] hoverAmounts = new float[FightPresets.Count];
    private uint headsBallotId;
    private volatile bool loaded;

    // Keep snapshot requests separate from live HUD/map heads of the same ID.
    public override void Load()
    {
        loaded = true;
        MapHeadRenderer renderer = headRenderer;
        Main.QueueMainThreadAction(() =>
        {
            if (loaded && !Main.ContentThatNeedsRenderTargets.Contains(renderer))
                Main.ContentThatNeedsRenderTargets.Add(renderer);
        });
    }

    public override void Unload()
    {
        loaded = false;
        MapHeadRenderer renderer = headRenderer;
        Main.QueueMainThreadAction(() =>
        {
            Main.ContentThatNeedsRenderTargets.Remove(renderer);
            renderer.Reset();
        });
        Reset();
    }

    public override void OnWorldLoad() => Reset();
    public override void OnWorldUnload()
    {
        Reset();
        headRenderer.Reset();
    }

    private void Reset()
    {
        presentation.Reset();
        heads.Clear();
        headsBallotId = 0;
        Array.Clear(hoverAmounts);
    }

    public override void UpdateUI(GameTime gameTime)
    {
        if (Main.gameMenu || ModContent.GetInstance<EndScreenSystem>().IsVisible
            || ModContent.GetInstance<RoundManager>().CurrentPhase != RoundManager.RoundPhase.VotingOrEndScreen)
        {
            Reset();
            return;
        }

        BossVoteSystem vote = ModContent.GetInstance<BossVoteSystem>();
        float seconds = (float)gameTime.ElapsedGameTime.TotalSeconds;
        presentation.Update(vote.BallotId, vote.Active, vote.Winner, seconds);
        if (!presentation.Visible)
            return;

        if (headsBallotId != vote.BallotId)
        {
            headsBallotId = vote.BallotId;
            heads.Clear();
            Array.Clear(hoverAmounts);
        }
        RefreshHeads(vote.Active);

        Rectangle panel = BossVoteDrawer.ActivePanel();
        float blend = ArenaUIStyle.HoverBlend(seconds);
        for (int i = 0; i < hoverAmounts.Length; i++)
        {
            bool hover = presentation.Interactive && !PlayerInput.IgnoreMouseInterface
                && BossVoteDrawer.ChoiceBox(panel, i).Contains(Main.mouseX, Main.mouseY);
            hoverAmounts[i] = MathHelper.Lerp(hoverAmounts[i], hover ? 1f : 0f, blend);
        }
    }

    internal void RefreshHeads(bool active)
    {
        for (int id = 0; id < Main.maxPlayers; id++)
        {
            Player player = Main.player[id];
            if (player?.active == true)
            {
                // Appearance can arrive after activation, or the slot can be reused.
                // Freeze captured voters when the completed result is displayed.
                if (active || !heads.ContainsKey(id))
                    heads[id] = BossVotePlayerHead.Create(player);
            }
            else if (active)
                heads.Remove(id);
        }
    }

    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        int index = layers.FindIndex(layer => layer.Name == "Vanilla: Mouse Text");
        if (index >= 0)
            layers.Insert(index, new LegacyGameInterfaceLayer("Arenas: Boss Voting", Draw, InterfaceScaleType.UI));
    }

    private bool Draw()
    {
        if (!Main.gameMenu && presentation.Visible && !ModContent.GetInstance<EndScreenSystem>().IsVisible)
            BossVoteDrawer.Draw(presentation, heads, hoverAmounts, headRenderer);
        return true;
    }
}
