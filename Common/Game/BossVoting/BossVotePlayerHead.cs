namespace PvPArenas.Common.Game.BossVoting;

/// <summary>A drawing-only snapshot of the player's current head appearance.</summary>
internal static class BossVotePlayerHead
{
    internal static Player Create(Player player)
    {
        // SerializedClone loses the network ID and its deserializer recolors the
        // local player. Head-only drawing needs current visuals, not save data.
        // This shallow copy must never be updated or passed through PlayerFrame.
        Player headPlayer = (Player)player.Clone();
        headPlayer.active = true;
        headPlayer.dead = false;
        headPlayer.ghost = false;
        headPlayer.invis = false;
        headPlayer.immuneAlpha = 0;
        headPlayer.headPosition = Vector2.Zero;
        headPlayer.headVelocity = Vector2.Zero;
        headPlayer.headRotation = 0f;
        headPlayer.fullRotation = 0f;
        headPlayer.fullRotationOrigin = Vector2.Zero;
        headPlayer.gfxOffY = 0f;
        headPlayer.gravDir = 1f;
        headPlayer.socialIgnoreLight = true;
        headPlayer.isDisplayDollOrInanimate = true;

        return headPlayer;
    }
}
