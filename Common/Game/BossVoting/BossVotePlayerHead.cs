namespace PvPArenas.Common.Game.BossVoting;

/// <summary>Local copy of ErkySSC's head snapshot preparation for the arena ballot.</summary>
internal static class BossVotePlayerHead
{
    internal static Player Create(Player player)
    {
        Player headPlayer = player.SerializedClone();
        CopyPlayerDrawAppearance(player, headPlayer);
        headPlayer.dead = false;

        if (headPlayer.ghost)
        {
            headPlayer.ghostFade = 1f;
            headPlayer.ghostDir = 1;
        }

        headPlayer.socialIgnoreLight = true;
        headPlayer.isDisplayDollOrInanimate = true;

        return headPlayer;
    }

    private static void CopyPlayerDrawAppearance(Player from, Player to)
    {
        to.head = from.head;
        to.body = from.body;
        to.legs = from.legs;

        to.cHead = from.cHead;
        to.cBody = from.cBody;
        to.cLegs = from.cLegs;

        to.face = from.face;
        to.neck = from.neck;
        to.front = from.front;
        to.back = from.back;
        to.waist = from.waist;
        to.shield = from.shield;
        to.shoe = from.shoe;
        to.balloon = from.balloon;
        to.beard = from.beard;

        to.handon = from.handon;
        to.handoff = from.handoff;

        to.wings = from.wings;
        to.wingsLogic = from.wingsLogic;
        to.wingFrame = from.wingFrame;
        to.wingFrameCounter = from.wingFrameCounter;

        to.carpet = from.carpet;
        to.carpetFrame = from.carpetFrame;

        to.shieldRaised = from.shieldRaised;
        to.shieldParryTimeLeft = from.shieldParryTimeLeft;

        to.invis = from.invis;
        to.headcovered = from.headcovered;
    }
}
