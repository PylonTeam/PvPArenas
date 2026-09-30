using PvPArenas.Common.Game.LoadoutSelector;
using PvPArenas.Common.UI;
using PvPFramework.Common.EndScreen;
using System;
using System.Collections.Generic;
using Terraria.GameInput;
using Terraria.UI;

namespace PvPArenas.Common.Game.BossVoting;

[Autoload(Side = ModSide.Client)]
internal sealed class BossVoteUISystem : ModSystem
{
    private readonly BossVotePresentation presentation = new();
    private readonly Dictionary<int, Player> heads = [];
    private readonly float[] hoverAmounts = new float[FightPresets.Count];
    private uint headsBallotId;

    public override void OnWorldLoad() => Reset();
    public override void OnWorldUnload() => Reset();

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
        for (int id = 0; id < Main.maxPlayers; id++)
            if (Main.player[id]?.active == true && !heads.ContainsKey(id))
                heads[id] = BossVotePlayerHead.Create(Main.player[id]);

        Rectangle panel = BossVoteDrawer.ActivePanel();
        float blend = ArenaUIStyle.HoverBlend(seconds);
        for (int i = 0; i < hoverAmounts.Length; i++)
        {
            bool hover = presentation.Interactive && !PlayerInput.IgnoreMouseInterface
                && BossVoteDrawer.ChoiceBox(panel, i).Contains(Main.mouseX, Main.mouseY);
            hoverAmounts[i] = MathHelper.Lerp(hoverAmounts[i], hover ? 1f : 0f, blend);
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
            BossVoteDrawer.Draw(presentation, heads, hoverAmounts);
        return true;
    }
}
