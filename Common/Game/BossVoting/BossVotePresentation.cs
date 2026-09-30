using System;

namespace PvPArenas.Common.Game.BossVoting;

/// <summary>Uses ErkySSC's opening, winner hold, and two-stage closing timings.</summary>
internal sealed class BossVotePresentation
{
    internal const float HoldSeconds = 1.5f, CloseSeconds = .4f;
    internal const int ResultDurationTicks = 114;
    internal uint BallotId { get; private set; }
    internal int Winner { get; private set; } = -1;
    internal bool Complete => Winner >= 0;
    private bool hasBallot;
    private float age, completedAge;
    internal float Opening => Math.Clamp(age / .2f, 0f, 1f);
    internal float ResultTransition => Complete ? Math.Clamp(completedAge / .2f, 0f, 1f) : 0f;
    internal float Closing => Complete ? Math.Clamp((completedAge - HoldSeconds) / CloseSeconds, 0f, 1f) : 0f;
    internal bool Visible => hasBallot && Closing < 1f;
    internal bool Interactive => Visible && !Complete;

    internal void Update(uint ballotId, bool active, int winner, float seconds)
    {
        if (!active && winner < 0) { Reset(); return; }
        if (!hasBallot || BallotId != ballotId)
        {
            Reset();
            hasBallot = true;
            BallotId = ballotId;
        }
        if (!active && !Complete)
        {
            Winner = winner;
            completedAge = 0f;
            age = .2f;
        }
        else if (Complete)
            completedAge += Math.Max(0f, seconds);
        age += Math.Max(0f, seconds);
    }

    internal void Reset()
    {
        hasBallot = false;
        BallotId = 0;
        Winner = -1;
        age = completedAge = 0f;
    }
}
