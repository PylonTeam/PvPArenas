using Microsoft.Xna.Framework;
using System;

namespace PvPArenas.Common.Game;

internal readonly record struct ArenaVoteLayoutArgs(int Width, int Height, int ChoiceCount, ArenaVoteUISettings Settings);

internal readonly record struct ArenaVoteAnimationState(float Opening, float ResultTransition, float Closing, bool Visible, bool Interactive);

internal readonly record struct ArenaVoteUpdateArgs(uint BallotId, bool Active, int Winner, float Seconds, ArenaVoteUISettings Settings);

internal readonly record struct ArenaVoteUISettings
{
    // Panel
    public int BackgroundPanelWidth { get; init; }
    public int BackgroundPanelHeight { get; init; }
    public int PanelPadding { get; init; }
    public int PanelPreferredTop { get; init; }
    public int PanelXOffset { get; init; }
    public int PanelYOffset { get; init; }
    public int PanelScreenMargin { get; init; }
    public int PanelReservedScreenHeight { get; init; }
    public int PanelCornerRadius { get; init; }
    public int CompactWidthThreshold { get; init; }
    public int Columns { get; init; }

    // Header
    public float HeaderTextScale { get; init; }
    public float CompactHeaderTextScale { get; init; }
    public int HeaderTextXOffset { get; init; }
    public int HeaderTextYOffset { get; init; }
    public int CompactHeaderTextYOffset { get; init; }
    public int HeaderTextPadding { get; init; }

    // Subtitle
    public float SubtitleTextScale { get; init; }
    public float CompactSubtitleTextScale { get; init; }
    public int SubtitleTextXOffset { get; init; }
    public int SubtitleTextYOffset { get; init; }
    public int CompactSubtitleTextYOffset { get; init; }
    public int SubtitleTextPadding { get; init; }

    // Progress
    public int ProgressXPadding { get; init; }
    public int ProgressYOffsetFromOptions { get; init; }
    public int ProgressHeight { get; init; }
    public float ProgressTrackOpacity { get; init; }

    // Options
    public int OptionsTop { get; init; }
    public int CompactOptionsTop { get; init; }
    public int OptionWidth { get; init; }
    public int OptionHeight { get; init; }
    public int OptionMinimumHeight { get; init; }
    public int OptionGap { get; init; }
    public int OptionCornerRadius { get; init; }
    public int CompactOptionThreshold { get; init; }
    public int OptionTextXOffset { get; init; }
    public int CompactOptionTextXOffset { get; init; }
    public int OptionTextYOffset { get; init; }
    public int CompactOptionTextYOffset { get; init; }
    public float OptionTextScale { get; init; }
    public float CompactOptionTextScale { get; init; }
    public int UnavailableTextMinimumHeight { get; init; }
    public int UnavailableTextXOffset { get; init; }
    public int UnavailableTextYOffset { get; init; }
    public float UnavailableTextScale { get; init; }
    public int UnavailableTextRightPadding { get; init; }

    // Boss icons
    public int BossIconSize { get; init; }
    public int CompactBossIconSize { get; init; }
    public int BossIconXOffset { get; init; }
    public int BossIconYOffset { get; init; }
    public int CompactBossIconYOffset { get; init; }
    public float BossIconScale { get; init; }
    public float BossIconPadding { get; init; }
    public float UnavailableBossIconOpacity { get; init; }

    // Votes
    public int VoteTextRightPadding { get; init; }
    public float VoteTextWidth { get; init; }
    public int VoteTextReservedWidth { get; init; }
    public int VoteTextYOffset { get; init; }
    public float VoteTextScaleMultiplier { get; init; }

    // Selection
    public int SelectionLineXPadding { get; init; }
    public int SelectionLineYOffset { get; init; }
    public int CompactSelectionLineYOffset { get; init; }
    public int SelectionLineHeight { get; init; }
    public float SelectionFillStrength { get; init; }

    // Voter heads
    public int VoterHeadsTop { get; init; }
    public int VoterHeadMaximumSize { get; init; }
    public int VoterHeadMinimumSize { get; init; }
    public int VoterHeadBottomPadding { get; init; }
    public int VoterHeadXPadding { get; init; }
    public int VoterHeadGap { get; init; }
    public float VoterHeadScale { get; init; }
    public float VoterHeadXOffset { get; init; }
    public float VoterHeadYOffset { get; init; }
    public int CenteredVoterHeadsXOffset { get; init; }

    // Overflow
    public float OverflowTextScale { get; init; }
    public float OverflowTextXOffset { get; init; }
    public float OverflowTextYOffset { get; init; }
    public int OverflowTextWidthPadding { get; init; }

    // Winner panel
    public int WinnerPanelWidth { get; init; }
    public int WinnerPanelHeight { get; init; }
    public int WinnerPanelXOffset { get; init; }
    public int WinnerPanelYOffset { get; init; }
    public int WinnerOptionTop { get; init; }

    // Style
    public Color PanelFillColor { get; init; }
    public Color PanelBorderColor { get; init; }
    public Color OptionFillColor { get; init; }
    public Color OptionHoverColor { get; init; }
    public Color TitleColor { get; init; }
    public Color AccentColor { get; init; }
    public Color ProgressColor { get; init; }
    public Color ProgressTrackColor { get; init; }
    public Color TextColor { get; init; }
    public Color UnavailableTextColor { get; init; }
    public Color OverflowTextColor { get; init; }
    public Color BossIconColor { get; init; }
    public float Opacity { get; init; }

    // Animation
    public float OpeningDurationSeconds { get; init; }
    public float WinnerTransitionSeconds { get; init; }
    public float HoverAnimationSpeed { get; init; }
    public float ClosingVerticalFraction { get; init; }
    public float ClosingHorizontalFraction { get; init; }
    public float ClosingTargetHeight { get; init; }
    public int ClosingLineXPadding { get; init; }
    public int ClosingLineHeight { get; init; }

}

/// <summary>
/// Edit Create's method-body values with C# Hot Reload; callers take a new snapshot each frame.
/// Do not move these values into constants or cached field initializers. Dimensions use logical
/// UI pixels. Zero background width fills the viewport between margins; zero background height
/// fits the option rows. Zero option width fills its column. Winner width is a minimum and must
/// still contain its option plus padding; zero winner height fits its contents.
/// The authoritative 114-tick result budget and its hold/close split remain in BossVotePresentation.
/// </summary>
internal static class ArenaVoteUILayout
{
    internal static ArenaVoteUISettings Create() => new()
    {
        // Panel
        BackgroundPanelWidth = 500,
        BackgroundPanelHeight = 0, // Automatic height from the option rows.
        PanelPadding = 16,
        PanelPreferredTop = 80,
        PanelXOffset = 0,
        PanelYOffset = 0,
        PanelScreenMargin = 6,
        PanelReservedScreenHeight = 54,
        PanelCornerRadius = 10,
        CompactWidthThreshold = 400,
        Columns = 2,

        // Header
        HeaderTextScale = .65f,
        CompactHeaderTextScale = .5f,
        HeaderTextXOffset = 0,
        HeaderTextYOffset = 8,
        CompactHeaderTextYOffset = 3,
        HeaderTextPadding = 16,

        // Subtitle
        SubtitleTextScale = .95f,
        CompactSubtitleTextScale = .8f,
        SubtitleTextXOffset = 0,
        SubtitleTextYOffset = 45,
        CompactSubtitleTextYOffset = 29,
        SubtitleTextPadding = 16,

        // Progress
        ProgressXPadding = 16,
        ProgressYOffsetFromOptions = 15,
        ProgressHeight = 3,
        ProgressTrackOpacity = .3f,

        // Options
        OptionsTop = 90,
        CompactOptionsTop = 66,
        OptionWidth = 0, // Fill each column; a positive value caps the card width.
        OptionHeight = 88,
        OptionMinimumHeight = 32,
        OptionGap = 12,
        OptionCornerRadius = 8,
        CompactOptionThreshold = 48,
        OptionTextXOffset = 44,
        CompactOptionTextXOffset = 36,
        OptionTextYOffset = 9,
        CompactOptionTextYOffset = 5,
        OptionTextScale = .9f,
        CompactOptionTextScale = .8f,
        UnavailableTextMinimumHeight = 68,
        UnavailableTextXOffset = 12,
        UnavailableTextYOffset = 49,
        UnavailableTextScale = .7f,
        UnavailableTextRightPadding = 12,

        // Boss icons
        BossIconSize = 28,
        CompactBossIconSize = 24,
        BossIconXOffset = 9,
        BossIconYOffset = 6,
        CompactBossIconYOffset = 3,
        BossIconScale = 1f,
        BossIconPadding = 8f,
        UnavailableBossIconOpacity = .4f,

        // Votes
        VoteTextRightPadding = 12,
        VoteTextWidth = 32f,
        VoteTextReservedWidth = 56,
        VoteTextYOffset = 0,
        VoteTextScaleMultiplier = 1f,

        // Selection
        SelectionLineXPadding = 12,
        SelectionLineYOffset = 36,
        CompactSelectionLineYOffset = 30,
        SelectionLineHeight = 2,
        SelectionFillStrength = .14f,

        // Voter heads
        VoterHeadsTop = 46,
        VoterHeadMaximumSize = 36,
        VoterHeadMinimumSize = 20,
        VoterHeadBottomPadding = 8,
        VoterHeadXPadding = 12,
        VoterHeadGap = 0,
        VoterHeadScale = 1f,
        VoterHeadXOffset = -2f,
        VoterHeadYOffset = -2f,
        CenteredVoterHeadsXOffset = 0,

        // Overflow
        OverflowTextScale = .72f,
        OverflowTextXOffset = 0f,
        OverflowTextYOffset = -8f,
        OverflowTextWidthPadding = 0,

        // Winner panel
        WinnerPanelWidth = 300, // Minimum; the chosen option and its padding must also fit.
        WinnerPanelHeight = 0, // Automatic height from WinnerOptionTop, option height and padding.
        WinnerPanelXOffset = 0,
        WinnerPanelYOffset = 0,
        WinnerOptionTop = 60,

        // Style
        PanelFillColor = new(25, 34, 66),
        PanelBorderColor = new(81, 99, 151),
        OptionFillColor = new(33, 44, 78),
        OptionHoverColor = new(52, 67, 108),
        TitleColor = new(246, 216, 72),
        AccentColor = new(153, 218, 158),
        ProgressColor = new(149, 175, 220),
        ProgressTrackColor = Color.Black,
        TextColor = Color.White,
        UnavailableTextColor = Color.Gray,
        OverflowTextColor = Color.Silver,
        BossIconColor = Color.White,
        Opacity = 1f,

        // Animation
        OpeningDurationSeconds = .2f,
        WinnerTransitionSeconds = .2f,
        HoverAnimationSpeed = 16f,
        ClosingVerticalFraction = .8f,
        ClosingHorizontalFraction = .2f,
        ClosingTargetHeight = 2f,
        ClosingLineXPadding = 10,
        ClosingLineHeight = 2,

    };

    internal static Color ChoiceFill(ArenaVoteUISettings settings, float hover, bool selected) =>
        Color.Lerp(Color.Lerp(settings.OptionFillColor, settings.OptionHoverColor, hover),
            settings.AccentColor, settings.SelectionFillStrength * (selected ? 1f : 0f));
}

/// <summary>Pure geometry shared by current drawing, hit testing and headless layout proofs.</summary>
internal static class ArenaVoteGeometry
{
    private static int Columns(int width, ArenaVoteUISettings ui) =>
        width < ui.CompactWidthThreshold ? 1 : Math.Max(1, ui.Columns);

    internal static Rectangle ActivePanel(ArenaVoteLayoutArgs args)
    {
        ArenaVoteUISettings ui = args.Settings;
        int margin = Math.Max(0, ui.PanelScreenMargin);
        int width = Math.Max(1, Math.Min(ui.BackgroundPanelWidth > 0 ? ui.BackgroundPanelWidth : args.Width - margin * 2, args.Width - margin * 2));
        int columns = Columns(width, ui), rows = (args.ChoiceCount + columns - 1) / columns;
        int autoHeight = ui.OptionsTop + rows * ui.OptionHeight + Math.Max(0, rows - 1) * ui.OptionGap + ui.PanelPadding;
        int height = Math.Max(1, Math.Min(ui.BackgroundPanelHeight > 0 ? ui.BackgroundPanelHeight : autoHeight,
            args.Height - ui.PanelReservedScreenHeight));
        int top = Math.Max(margin, Math.Min(ui.PanelPreferredTop + ui.PanelYOffset, args.Height - height - margin));
        return new((args.Width - width) / 2 + ui.PanelXOffset, top, width, height);
    }

    internal static int CardsTop(Rectangle panel, ArenaVoteLayoutArgs args)
    {
        ArenaVoteUISettings ui = args.Settings;
        int columns = Columns(panel.Width, ui), rows = (args.ChoiceCount + columns - 1) / columns;
        return Math.Min(ui.OptionsTop, Math.Max(ui.CompactOptionsTop,
            panel.Height - ui.PanelPadding - Math.Max(0, rows - 1) * ui.OptionGap - rows * ui.OptionMinimumHeight));
    }

    internal static Rectangle ChoiceBox(Rectangle panel, int index, ArenaVoteLayoutArgs args)
    {
        ArenaVoteUISettings ui = args.Settings;
        int columns = Columns(panel.Width, ui), rows = Math.Max(1, (args.ChoiceCount + columns - 1) / columns);
        int width = Math.Max(1, (panel.Width - ui.PanelPadding * 2 - ui.OptionGap * (columns - 1)) / columns);
        if (ui.OptionWidth > 0) width = Math.Min(width, ui.OptionWidth);
        int top = CardsTop(panel, args);
        int height = Math.Max(1, Math.Min(ui.OptionHeight,
            (panel.Height - top - ui.PanelPadding - (rows - 1) * ui.OptionGap) / rows));
        int left = panel.X + ui.PanelPadding + index % columns * (width + ui.OptionGap);
        if (ui.OptionWidth > 0)
        {
            int rowCount = Math.Min(columns, args.ChoiceCount - index / columns * columns);
            left = panel.Center.X - (rowCount * width + Math.Max(0, rowCount - 1) * ui.OptionGap) / 2
                + index % columns * (width + ui.OptionGap);
        }
        else if (index / columns == rows - 1 && args.ChoiceCount % columns == 1)
            left = panel.Center.X - width / 2;
        return new(left, panel.Y + top + index / columns * (height + ui.OptionGap), width, height);
    }

    internal static Rectangle ResultPanel(Rectangle panel, ArenaVoteLayoutArgs args)
    {
        ArenaVoteUISettings ui = args.Settings;
        int width = Math.Min(panel.Width, Math.Max(ui.WinnerPanelWidth, ChoiceBox(panel, 0, args).Width + ui.PanelPadding * 2));
        int autoHeight = ui.WinnerOptionTop + ui.OptionHeight + ui.PanelPadding;
        return new(panel.Center.X - width / 2 + ui.WinnerPanelXOffset, panel.Y + ui.WinnerPanelYOffset, width,
            Math.Max(1, Math.Min(ui.WinnerPanelHeight > 0 ? ui.WinnerPanelHeight : autoHeight,
                args.Height - panel.Y - ui.WinnerPanelYOffset - Math.Max(0, ui.PanelScreenMargin))));
    }

    internal static Rectangle ResultBox(Rectangle panel, int index, ArenaVoteLayoutArgs args)
    {
        Rectangle box = ChoiceBox(panel, index, args);
        Rectangle result = ResultPanel(panel, args);
        ArenaVoteUISettings ui = args.Settings;
        box.X = result.Center.X - box.Width / 2;
        box.Y = result.Y + ui.WinnerOptionTop;
        box.Height = Math.Max(1, Math.Min(ui.OptionHeight, result.Height - ui.WinnerOptionTop - ui.PanelPadding));
        return box;
    }

    internal static int VoterHeadSize(Rectangle box, ArenaVoteUISettings ui)
    {
        int size = Math.Min(ui.VoterHeadMaximumSize, box.Height - ui.VoterHeadsTop - ui.VoterHeadBottomPadding);
        return size >= Math.Max(1, ui.VoterHeadMinimumSize) ? size : 0;
    }

    internal static Rectangle SelectionLine(Rectangle box, ArenaVoteUISettings ui) =>
        new(box.X + ui.SelectionLineXPadding,
            box.Y + (box.Height < ui.CompactOptionThreshold ? ui.CompactSelectionLineYOffset : ui.SelectionLineYOffset),
            Math.Max(1, box.Width - ui.SelectionLineXPadding * 2), Math.Max(1, ui.SelectionLineHeight));
}
