using Microsoft.Xna.Framework;

namespace PvPArenas.Common.Game;

internal readonly record struct LoadoutUISettings
{
    // Panel
    public int BackgroundPanelWidth { get; init; }
    public int BackgroundPanelHeight { get; init; }
    public int BackgroundPanelXOffset { get; init; }
    public int BackgroundPanelYOffset { get; init; }
    public int PanelPadding { get; init; }
    public int PanelTop { get; init; }
    public int PanelMinimumTop { get; init; }
    public int PanelScreenMargin { get; init; }
    public int PanelBottomScreenMargin { get; init; }
    public int PanelCornerRadius { get; init; }

    // Header
    public int HeaderHeight { get; init; }
    public int LoadoutTitleYPadding { get; init; }
    public int LoadoutTitleTextXOffset { get; init; }
    public int LoadoutTitleTextYOffset { get; init; }
    public int LoadoutTitleHeight { get; init; }
    public float LoadoutTitleTextScale { get; init; }
    public int TitleTimerGap { get; init; }
    public int ProgressBarYOffset { get; init; }
    public int ProgressBarHeight { get; init; }
    public int HeaderBottomPadding { get; init; }

    // Timer
    public int TimerWidth { get; init; }
    public int TimerHeight { get; init; }
    public int TimerXOffset { get; init; }
    public int TimerYOffset { get; init; }
    public float TimerTextScale { get; init; }
    public float TimerTextYOffset { get; init; }

    // Loadout options
    public int LoadoutPanelOptionColumns { get; init; }
    public int LoadoutPanelOptionWidth { get; init; }
    public int LoadoutPanelOptionHeight { get; init; }
    public int LoadoutPanelOptionGap { get; init; }
    public int LoadoutPanelOptionsBottomPadding { get; init; }
    public int LoadoutPanelOptionCornerRadius { get; init; }
    public float LoadoutPanelOptionTextScale { get; init; }
    public int LoadoutPanelOptionTextXOffset { get; init; }
    public int LoadoutPanelOptionTextYOffset { get; init; }
    public int LoadoutPanelOptionTextPadding { get; init; }
    public int LoadoutPanelOptionTextHeight { get; init; }
    public float LoadoutPanelOptionIconScale { get; init; }
    public int LoadoutPanelOptionIconSize { get; init; }
    public int LoadoutPanelOptionIconXOffset { get; init; }
    public int LoadoutPanelOptionIconYOffset { get; init; }
    public int LoadoutPanelOptionIconPadding { get; init; }
    public int LoadoutPanelOptionUnderlinePadding { get; init; }
    public int LoadoutPanelOptionUnderlineYOffset { get; init; }
    public int LoadoutPanelOptionUnderlineHeight { get; init; }

    // Inventory
    public int InventorySlotSize { get; init; }
    public int InventorySlotStep { get; init; }
    public int InventoryXOffset { get; init; }
    public int InventoryYOffset { get; init; }
    public int InventorySlotCornerRadius { get; init; }
    public float InventoryItemScale { get; init; }
    public int InventoryItemPadding { get; init; }
    public float InventoryStackTextScale { get; init; }
    public float InventoryHotbarTextScale { get; init; }
    public int InventoryLabelPadding { get; init; }
    public float InventoryLabelHeightFraction { get; init; }
    public int InventoryStackTextXOffset { get; init; }
    public int InventoryStackTextYOffset { get; init; }
    public int InventoryHotbarTextXOffset { get; init; }
    public int InventoryHotbarTextYOffset { get; init; }

    // Equipment
    public int InventoryEquipmentGap { get; init; }
    public int EquipmentSlotSize { get; init; }
    public int EquipmentSlotStep { get; init; }
    public int EquipmentXOffset { get; init; }
    public int EquipmentYOffset { get; init; }
    public int EquipmentSlotCornerRadius { get; init; }
    public float EquipmentItemScale { get; init; }
    public int EquipmentItemPadding { get; init; }

    // Player
    public int PlayerPreviewWidth { get; init; }
    public int PlayerPreviewMinimumBodyHeight { get; init; }
    public int PlayerInventoryGap { get; init; }
    public int PlayerPreviewXOffset { get; init; }
    public int PlayerPreviewYOffset { get; init; }
    public float PlayerPreviewScale { get; init; }
    public int PlayerPreviewBottomPadding { get; init; }
    public int PlayerPreviewCornerRadius { get; init; }
    public int PlayerPreviewAnimationPadding { get; init; }
    public int PlayerSpriteHeight { get; init; }

    // Health and mana
    public int HealthManaHeight { get; init; }
    public int PlayerStatsGap { get; init; }
    public int HealthManaXPadding { get; init; }
    public int HealthIconSize { get; init; }
    public int ManaIconSize { get; init; }
    public float HealthTextScale { get; init; }
    public float ManaTextScale { get; init; }
    public int HealthManaIconTextGap { get; init; }
    public int HealthManaTextPadding { get; init; }
    public float HealthManaTextYOffset { get; init; }
    public int HealthManaCornerRadius { get; init; }

    // Style
    public Color PanelFillColor { get; init; }
    public Color PanelBorderColor { get; init; }
    public Color CardFillColor { get; init; }
    public Color CardHoverColor { get; init; }
    public Color TitleColor { get; init; }
    public Color AccentColor { get; init; }
    public Color ProgressColor { get; init; }
    public Color TextColor { get; init; }
    public Color TimerTextColor { get; init; }
    public float SelectionFillStrength { get; init; }
    public float Opacity { get; init; }
    public float ItemOpacity { get; init; }
    public float ProgressTrackOpacity { get; init; }

    // Animation
    public float OpeningDurationSeconds { get; init; }
    public float HoverAnimationSpeed { get; init; }
    public float InputEnableOpacity { get; init; }
    public int PlayerJumpDuration { get; init; }
    public float PlayerJumpHeight { get; init; }
    public int PlayerWalkFrameTicks { get; init; }

}

/// <summary>
/// Edit the values in Create while debugging with C# Hot Reload. This method runs again
/// each frame; constants and cached field initializers would keep their old values.
/// Dimensions are logical UI pixels. Zero panel width/height sizes to the content.
/// Larger panel dimensions add space; smaller dimensions uniformly shrink the contents
/// to fit. Screen bounds can shrink the result further while preserving its proportions.
/// </summary>
internal static class LoadoutUILayout
{
    internal static LoadoutUISettings Create() => new()
    {
        // Panel
        BackgroundPanelWidth = 764,
        BackgroundPanelHeight = 0,
        BackgroundPanelXOffset = 0,
        BackgroundPanelYOffset = 0,
        PanelPadding = 20,
        PanelTop = 80,
        PanelMinimumTop = 56,
        PanelScreenMargin = 6,
        PanelBottomScreenMargin = 12,
        PanelCornerRadius = 10,

        // Header
        HeaderHeight = 80,
        LoadoutTitleYPadding = 8,
        LoadoutTitleTextXOffset = 0,
        LoadoutTitleTextYOffset = 0,
        LoadoutTitleHeight = 48,
        LoadoutTitleTextScale = .65f,
        TitleTimerGap = 16,
        ProgressBarYOffset = 62,
        ProgressBarHeight = 3,
        HeaderBottomPadding = 15,

        // Timer
        TimerWidth = 80,
        TimerHeight = 48,
        TimerXOffset = 0,
        TimerYOffset = 0,
        TimerTextScale = 1f,
        TimerTextYOffset = 0f,

        // Loadout options
        LoadoutPanelOptionColumns = 3,
        LoadoutPanelOptionWidth = 196,
        LoadoutPanelOptionHeight = 76,
        LoadoutPanelOptionGap = 12,
        LoadoutPanelOptionsBottomPadding = 20,
        LoadoutPanelOptionCornerRadius = 8,
        LoadoutPanelOptionTextScale = .90f,
        LoadoutPanelOptionTextXOffset = 0,
        LoadoutPanelOptionTextYOffset = 40,
        LoadoutPanelOptionTextPadding = 12,
        LoadoutPanelOptionTextHeight = 26,
        LoadoutPanelOptionIconScale = 1f,
        LoadoutPanelOptionIconSize = 32,
        LoadoutPanelOptionIconXOffset = 0,
        LoadoutPanelOptionIconYOffset = 4,
        LoadoutPanelOptionIconPadding = 6,
        LoadoutPanelOptionUnderlinePadding = 12,
        LoadoutPanelOptionUnderlineYOffset = 68,
        LoadoutPanelOptionUnderlineHeight = 2,

        // Inventory
        InventorySlotSize = 36,
        InventorySlotStep = 40,
        InventoryXOffset = 0,
        InventoryYOffset = 0,
        InventorySlotCornerRadius = 6,
        InventoryItemScale = .85f,
        InventoryItemPadding = 8,
        InventoryStackTextScale = .62f,
        InventoryHotbarTextScale = .62f,
        InventoryLabelPadding = 2,
        InventoryLabelHeightFraction = 2f / 3f,
        InventoryStackTextXOffset = 0,
        InventoryStackTextYOffset = 0,
        InventoryHotbarTextXOffset = 0,
        InventoryHotbarTextYOffset = 0,

        // Equipment
        InventoryEquipmentGap = 20,
        EquipmentSlotSize = 36,
        EquipmentSlotStep = 40,
        EquipmentXOffset = 0,
        EquipmentYOffset = 0,
        EquipmentSlotCornerRadius = 6,
        EquipmentItemScale = .85f,
        EquipmentItemPadding = 8,

        // Player
        PlayerPreviewWidth = 128,
        PlayerPreviewMinimumBodyHeight = 152,
        PlayerInventoryGap = 16,
        PlayerPreviewXOffset = 0,
        PlayerPreviewYOffset = 0,
        PlayerPreviewScale = 1f,
        PlayerPreviewBottomPadding = 4,
        PlayerPreviewCornerRadius = 8,
        PlayerPreviewAnimationPadding = 8,
        PlayerSpriteHeight = 56,

        // Health and mana
        HealthManaHeight = 28,
        PlayerStatsGap = 8,
        HealthManaXPadding = 4,
        HealthIconSize = 18,
        ManaIconSize = 18,
        HealthTextScale = .88f,
        ManaTextScale = .90f,
        HealthManaIconTextGap = 4,
        HealthManaTextPadding = 4,
        HealthManaTextYOffset = 0f,
        HealthManaCornerRadius = 6,

        // Style
        PanelFillColor = new(25, 34, 66),
        PanelBorderColor = new(81, 99, 151),
        CardFillColor = new(33, 44, 78),
        CardHoverColor = new(52, 67, 108),
        TitleColor = new(246, 216, 72),
        AccentColor = new(153, 218, 158),
        ProgressColor = new(149, 175, 220),
        TextColor = Color.White,
        TimerTextColor = Color.Silver,
        SelectionFillStrength = .14f,
        Opacity = 1f,
        ItemOpacity = 1f,
        ProgressTrackOpacity = .30f,

        // Animation
        OpeningDurationSeconds = .20f,
        HoverAnimationSpeed = 16f,
        InputEnableOpacity = .95f,
        PlayerJumpDuration = 32,
        PlayerJumpHeight = 30f,
        PlayerWalkFrameTicks = 5,

    };

    internal static Color ChoiceFill(LoadoutUISettings settings, float hover, float selection) =>
        Color.Lerp(Color.Lerp(settings.CardFillColor, settings.CardHoverColor, hover),
            settings.AccentColor, settings.SelectionFillStrength * selection);
}

