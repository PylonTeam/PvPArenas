using Microsoft.Xna.Framework;
using System;

namespace PvPArenas.Common.Game;

internal readonly record struct LoadoutPreviewArgs(int Width, int Height, int Top, int OptionCount, int InventoryRows,
    int EquipmentColumns, LoadoutUISettings Settings);

internal readonly record struct LoadoutPreviewCard(Rectangle Bounds, Rectangle IconBox, Rectangle NameBox, Rectangle SelectionLine);

internal readonly record struct LoadoutPreviewLayout(Rectangle Panel, Rectangle TitleBox, Rectangle BossNameBox,
    Rectangle TimerBox, Rectangle ProgressBox, Rectangle SelectorBox, Rectangle PortraitBox, Rectangle StatsBox,
    Rectangle InventoryBox, Rectangle EquipmentBox, float Scale, int OptionCount, int Columns, int Rows, int GridRows,
    int EquipmentColumns, LoadoutUISettings Settings);

/// <summary>Logical UI regions shared by loadout drawing, hit testing and offline validation.</summary>
internal static class LoadoutPreviewGeometry
{
    // These describe Terraria's inventory and equipment ordering, rather than visual preferences.
    internal const int InventoryColumns = 10;
    internal const int EquipmentRows = 3;

    internal static int MaximumGridRows(int[] inventoryCounts, bool sandbox)
    {
        int rows = sandbox ? 5 : EquipmentRows;
        foreach (int count in inventoryCounts ?? [])
            rows = Math.Max(rows, (Math.Clamp(count, 10, 58) + InventoryColumns - 1) / InventoryColumns);
        return rows;
    }

    internal static LoadoutPreviewLayout Calculate(LoadoutPreviewArgs args)
    {
        LoadoutUISettings settings = args.Settings;
        int width = args.Width, height = args.Height;
        int optionCount = Math.Max(0, args.OptionCount);
        int inventoryRows = Math.Clamp(args.InventoryRows, EquipmentRows, 6);
        int equipmentColumns = Math.Clamp(args.EquipmentColumns, 0, 4);
        int padding = Math.Max(0, settings.PanelPadding);
        int previewWidth = Math.Max(1, settings.PlayerPreviewWidth);
        int inventoryStep = Math.Max(Math.Max(1, settings.InventorySlotSize), settings.InventorySlotStep);
        int equipmentStep = Math.Max(Math.Max(1, settings.EquipmentSlotSize), settings.EquipmentSlotStep);
        int inventoryWidth = InventoryColumns * inventoryStep;
        int equipmentWidth = equipmentColumns * equipmentStep;
        int previewGap = Math.Max(0, settings.PlayerInventoryGap);
        int equipmentGap = equipmentColumns > 0 ? Math.Max(0, settings.InventoryEquipmentGap) : 0;
        int bodyWidth = previewWidth + previewGap + inventoryWidth + equipmentGap + equipmentWidth;
        int minimumWidth = padding * 2 + bodyWidth + Math.Max(0, settings.PlayerPreviewXOffset)
            + Math.Max(0, settings.InventoryXOffset) + Math.Max(0, settings.EquipmentXOffset);
        int designWidth = Math.Max(minimumWidth, settings.BackgroundPanelWidth);
        int bodyX = padding + (designWidth - minimumWidth) / 2;

        int titleY = Math.Max(0, settings.LoadoutTitleYPadding) + settings.LoadoutTitleTextYOffset;
        int titleHeight = Math.Max(1, settings.LoadoutTitleHeight);
        int timerY = Math.Max(0, settings.LoadoutTitleYPadding) + settings.TimerYOffset;
        int timerHeight = Math.Max(1, settings.TimerHeight);
        int timerWidth = Math.Max(1, settings.TimerWidth);
        int progressY = Math.Max(0, settings.ProgressBarYOffset);
        int progressHeight = Math.Max(1, settings.ProgressBarHeight);
        int headerHeight = Math.Max(settings.HeaderHeight, Math.Max(titleY + titleHeight, timerY + timerHeight));
        headerHeight = Math.Max(headerHeight, progressY + progressHeight + Math.Max(0, settings.HeaderBottomPadding));

        int columns = optionCount > 1 ? Math.Min(Math.Clamp(settings.LoadoutPanelOptionColumns, 1, 10), optionCount) : 0;
        int rows = columns > 0 ? (optionCount + columns - 1) / columns : 0;
        int cardHeight = OptionHeight(settings);
        int cardGap = Math.Max(0, settings.LoadoutPanelOptionGap);
        int selectorHeight = rows > 0 ? rows * cardHeight + (rows - 1) * cardGap
            + Math.Max(0, settings.LoadoutPanelOptionsBottomPadding) : 0;
        int statsHeight = Math.Max(1, settings.HealthManaHeight);
        int statsGap = Math.Max(0, settings.PlayerStatsGap);
        int bodyHeight = Math.Max(settings.PlayerPreviewMinimumBodyHeight,
            Math.Max(inventoryRows * inventoryStep + Math.Max(0, settings.InventoryYOffset),
                EquipmentRows * equipmentStep + Math.Max(0, settings.EquipmentYOffset)));
        bodyHeight = Math.Max(bodyHeight, statsHeight + statsGap + 1 + Math.Max(0, settings.PlayerPreviewYOffset));
        int designHeight = Math.Max(settings.BackgroundPanelHeight, headerHeight + selectorHeight + bodyHeight + padding);
        bodyHeight = designHeight - headerHeight - selectorHeight - padding;

        int margin = Math.Max(0, settings.PanelScreenMargin);
        int bottomMargin = Math.Max(0, settings.PanelBottomScreenMargin);
        float widthScale = Math.Min(1f, Math.Max(1, width - margin * 2) / (float)designWidth);
        if (settings.BackgroundPanelWidth > 0)
            widthScale = Math.Min(widthScale, settings.BackgroundPanelWidth / (float)designWidth);
        if (settings.BackgroundPanelHeight > 0)
            widthScale = Math.Min(widthScale, settings.BackgroundPanelHeight / (float)designHeight);
        int preferredTop = Math.Clamp(args.Top + settings.BackgroundPanelYOffset, 0, Math.Max(0, height - 1));
        int minimumTop = Math.Min(Math.Max(0, settings.PanelMinimumTop), preferredTop);
        int actualTop = Math.Clamp(height - bottomMargin - (int)MathF.Round(designHeight * widthScale), minimumTop, preferredTop);
        float scale = Math.Max(.001f, Math.Min(widthScale, Math.Max(1, height - actualTop - bottomMargin) / (float)designHeight));
        int S(float value) => Math.Max(1, (int)MathF.Round(value * scale));
        int panelWidth = S(designWidth);
        int panelX = Math.Clamp((width - panelWidth) / 2 + (int)MathF.Round(settings.BackgroundPanelXOffset * scale),
            margin, Math.Max(margin, width - margin - panelWidth));
        Rectangle panel = new(panelX, actualTop, panelWidth, S(designHeight));
        Rectangle Box(int x, int y, int w, int h) => new(panel.X + (int)MathF.Round(x * scale),
            panel.Y + (int)MathF.Round(y * scale), S(w), S(h));
        int bodyY = headerHeight + selectorHeight;
        int inventoryX = bodyX + previewWidth + previewGap;
        Rectangle selector = rows > 0 ? Box(padding, headerHeight, designWidth - padding * 2,
            selectorHeight - Math.Max(0, settings.LoadoutPanelOptionsBottomPadding)) : Rectangle.Empty;
        if (rows > 0)
            selector.Height = (int)MathF.Round((rows - 1) * (cardHeight + cardGap) * scale) + S(cardHeight);
        Rectangle equipment = equipmentColumns > 0 ? Box(inventoryX + inventoryWidth + equipmentGap + settings.EquipmentXOffset,
            bodyY + settings.EquipmentYOffset, equipmentWidth, EquipmentRows * equipmentStep) : Rectangle.Empty;
        return new(panel,
            Box(padding + settings.LoadoutTitleTextXOffset, titleY,
                Math.Max(1, designWidth - padding * 2 - timerWidth - Math.Max(0, settings.TitleTimerGap)), titleHeight),
            Rectangle.Empty,
            Box(designWidth - padding - timerWidth + settings.TimerXOffset, timerY, timerWidth, timerHeight),
            Box(padding, progressY, designWidth - padding * 2, progressHeight), selector,
            Box(bodyX + settings.PlayerPreviewXOffset, bodyY + settings.PlayerPreviewYOffset,
                previewWidth, bodyHeight - statsHeight - statsGap - Math.Max(0, settings.PlayerPreviewYOffset)),
            Box(bodyX, bodyY + bodyHeight - statsHeight, previewWidth, statsHeight),
            Box(inventoryX + settings.InventoryXOffset, bodyY + settings.InventoryYOffset, inventoryWidth, inventoryRows * inventoryStep),
            equipment, scale, optionCount, columns, rows, inventoryRows, equipmentColumns, settings);
    }

    private static int OptionHeight(LoadoutUISettings settings) => Math.Max(1, Math.Max(settings.LoadoutPanelOptionHeight,
        Math.Max(settings.LoadoutPanelOptionIconYOffset + Math.Max(1, settings.LoadoutPanelOptionIconSize),
            Math.Max(settings.LoadoutPanelOptionTextYOffset + Math.Max(1, settings.LoadoutPanelOptionTextHeight),
                settings.LoadoutPanelOptionUnderlineYOffset + Math.Max(1, settings.LoadoutPanelOptionUnderlineHeight)))));

    internal static LoadoutPreviewCard Card(LoadoutPreviewLayout layout, int index)
    {
        if (layout.Columns == 0 || index < 0 || index >= layout.OptionCount)
            return default;
        LoadoutUISettings settings = layout.Settings;
        int S(float value) => Math.Max(1, (int)MathF.Round(value * layout.Scale));
        int row = index / layout.Columns;
        int count = Math.Min(layout.Columns, layout.OptionCount - row * layout.Columns);
        int gap = Math.Max(0, (int)MathF.Round(settings.LoadoutPanelOptionGap * layout.Scale));
        int width = Math.Max(1, Math.Min(S(settings.LoadoutPanelOptionWidth),
            (layout.Panel.Width - Math.Max(0, (int)MathF.Round(settings.PanelPadding * layout.Scale)) * 2
                - gap * (layout.Columns - 1)) / layout.Columns));
        int rowWidth = count * width + (count - 1) * gap;
        int cardHeight = OptionHeight(settings);
        Rectangle card = new(layout.Panel.Center.X - rowWidth / 2 + index % layout.Columns * (width + gap),
            layout.SelectorBox.Y + (int)MathF.Round(row * (cardHeight + Math.Max(0, settings.LoadoutPanelOptionGap)) * layout.Scale),
            width, S(cardHeight));
        int iconSize = S(settings.LoadoutPanelOptionIconSize);
        int textPadding = Math.Max(0, (int)MathF.Round(settings.LoadoutPanelOptionTextPadding * layout.Scale));
        int linePadding = Math.Max(0, (int)MathF.Round(settings.LoadoutPanelOptionUnderlinePadding * layout.Scale));
        return new(card,
            new(card.Center.X - iconSize / 2 + (int)MathF.Round(settings.LoadoutPanelOptionIconXOffset * layout.Scale),
                card.Y + (int)MathF.Round(settings.LoadoutPanelOptionIconYOffset * layout.Scale), iconSize, iconSize),
            new(card.X + textPadding + (int)MathF.Round(settings.LoadoutPanelOptionTextXOffset * layout.Scale),
                card.Y + (int)MathF.Round(settings.LoadoutPanelOptionTextYOffset * layout.Scale),
                Math.Max(1, card.Width - textPadding * 2), S(settings.LoadoutPanelOptionTextHeight)),
            new(card.X + linePadding, card.Y + (int)MathF.Round(settings.LoadoutPanelOptionUnderlineYOffset * layout.Scale),
                Math.Max(1, card.Width - linePadding * 2), S(settings.LoadoutPanelOptionUnderlineHeight)));
    }

    internal static string CardName(string value) => value.ReplaceLineEndings(" ");

    internal static Rectangle InventorySlot(LoadoutPreviewLayout layout, int index) => Slot(layout.InventoryBox,
        index % InventoryColumns, index / InventoryColumns, layout.Scale,
        layout.Settings.InventorySlotStep, layout.Settings.InventorySlotSize);

    internal static Rectangle EquipmentSlot(LoadoutPreviewLayout layout, int index) => Slot(layout.EquipmentBox,
        index / EquipmentRows, index % EquipmentRows, layout.Scale,
        layout.Settings.EquipmentSlotStep, layout.Settings.EquipmentSlotSize);

    private static Rectangle Slot(Rectangle area, int column, int row, float scale, int step, int size)
    {
        step = Math.Max(Math.Max(1, size), step);
        return new(area.X + (int)MathF.Round(column * step * scale), area.Y + (int)MathF.Round(row * step * scale),
            Math.Max(1, (int)MathF.Round(size * scale)), Math.Max(1, (int)MathF.Round(size * scale)));
    }

    internal static Rectangle Stat(LoadoutPreviewLayout layout, int index)
    {
        int padding = Math.Max(0, (int)MathF.Round(layout.Settings.HealthManaXPadding * layout.Scale));
        Rectangle area = layout.StatsBox;
        int half = Math.Max(1, (area.Width - padding * 2) / 2);
        return new(area.X + padding + index * half, area.Y, half, area.Height);
    }

    internal static Rectangle SlotLabelBox(Rectangle cell, bool stack, LoadoutUISettings settings, float scale)
    {
        int inset = Math.Max(1, (int)MathF.Round(settings.InventoryLabelPadding * scale));
        int height = Math.Max(1, (int)(cell.Height * Math.Clamp(settings.InventoryLabelHeightFraction, .1f, 1f)));
        int xOffset = (int)MathF.Round((stack ? settings.InventoryStackTextXOffset : settings.InventoryHotbarTextXOffset) * scale);
        int yOffset = (int)MathF.Round((stack ? settings.InventoryStackTextYOffset : settings.InventoryHotbarTextYOffset) * scale);
        return new(cell.X + inset + xOffset, (stack ? cell.Bottom - height : cell.Y + inset) + yOffset,
            Math.Max(1, cell.Width - inset * 2), Math.Max(1, height - inset));
    }

    internal static float TextScale(Vector2 measured, Rectangle area, float preferred) => Math.Max(.001f, Math.Min(preferred,
        Math.Min(area.Width / Math.Max(1f, measured.X), area.Height / Math.Max(1f, measured.Y))));

    internal static float PortraitScale(LoadoutPreviewLayout layout) => Math.Max(.001f, Math.Min(
        Math.Max(0f, layout.Settings.PlayerPreviewScale) * layout.Scale,
        Math.Max(0f, layout.PortraitBox.Height - layout.Settings.PlayerPreviewAnimationPadding * layout.Scale)
            / Math.Max(1f, layout.Settings.PlayerSpriteHeight + layout.Settings.PlayerJumpHeight)));
}
