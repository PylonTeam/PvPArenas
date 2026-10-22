using System;

namespace PvPArenas.Common.Game.BossVoting;

/// <summary>Uses ErkySSC's opening, winner hold, and two-stage closing timings.</summary>
internal sealed class BossVotePresentation
{
    // The server advances after 114 ticks. Keep this hold/close split inside that shared budget.
    internal const float HoldSeconds = 1.5f, CloseSeconds = .4f;
    internal const int ResultDurationTicks = 114;
    internal uint BallotId { get; private set; }
    internal int Winner { get; private set; } = -1;
    internal bool Complete => Winner >= 0;
    private bool hasBallot;
    private float age, completedAge;
    internal float Opening => Animation(ArenaVoteUILayout.Create()).Opening;
    internal float ResultTransition => Animation(ArenaVoteUILayout.Create()).ResultTransition;
    internal float Closing => Animation(ArenaVoteUILayout.Create()).Closing;
    internal bool Visible => Animation(ArenaVoteUILayout.Create()).Visible;
    internal bool Interactive => Animation(ArenaVoteUILayout.Create()).Interactive;

    internal ArenaVoteAnimationState Animation(ArenaVoteUISettings settings)
    {
        float opening = Math.Clamp(age / Math.Max(.001f, settings.OpeningDurationSeconds), 0f, 1f);
        float transition = Complete ? Math.Clamp(completedAge /
            Math.Clamp(settings.WinnerTransitionSeconds, .001f, HoldSeconds), 0f, 1f) : 0f;
        float closing = Complete ? Math.Clamp((completedAge - HoldSeconds) / CloseSeconds, 0f, 1f) : 0f;
        bool visible = hasBallot && closing < 1f;
        return new(opening, transition, closing, visible, visible && !Complete);
    }

    internal void Update(uint ballotId, bool active, int winner, float seconds)
        => UpdateWithSettings(new(ballotId, active, winner, seconds, ArenaVoteUILayout.Create()));

    internal void UpdateWithSettings(ArenaVoteUpdateArgs args)
    {
        uint ballotId = args.BallotId;
        bool active = args.Active;
        int winner = args.Winner;
        float seconds = args.Seconds;
        ArenaVoteUISettings settings = args.Settings;
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
            age = Math.Max(.001f, settings.OpeningDurationSeconds);
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
