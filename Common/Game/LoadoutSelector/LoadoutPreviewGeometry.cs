using Microsoft.Xna.Framework;
using System;

namespace PvPArenas.Common.Game;

/// <summary>Logical UI regions shared by loadout drawing, hit testing and offline validation.</summary>
internal static class LoadoutPreviewGeometry
{
    internal const int DesignWidth = 748;
    internal const int Padding = 20;
    internal const int HeaderHeight = 112;
    internal const int PreviewWidth = 112;
    internal const int PreviewInventoryGap = 16;
    internal const int InventoryEquipmentGap = 20;
    internal const int InventoryColumns = 10;
    internal const int EquipmentRows = 3;
    internal const int SlotStep = 40;
    internal const int SlotSize = 36;
    internal const int MaxColumns = 3;
    internal const int CardWidth = 196;
    internal const int CardHeight = 76;
    internal const int CardGap = 12;
    internal const int SelectorBottomPadding = 20;
    internal const int MinimumBodyHeight = 152;
    internal const int StatsHeight = 28;
    internal const int PortraitStatsGap = 8;
    internal const float TitleScale = .65f;
    internal const float BossNameScale = .95f;
    internal const float TimerScale = .82f;
    internal const float CardNameScale = .90f;
    internal const float StatTextScale = .78f;
    internal const float SlotTextScale = .62f;

    internal static int MaximumGridRows(int[] inventoryCounts, bool sandbox)
    {
        int rows = sandbox ? 5 : EquipmentRows;
        foreach (int count in inventoryCounts ?? [])
            rows = Math.Max(rows, (Math.Clamp(count, 10, 58) + InventoryColumns - 1) / InventoryColumns);
        return rows;
    }

    internal static LoadoutPreviewLayout Calculate(int width, int height, int top, int optionCount, int inventoryRows, int equipmentColumns)
    {
        optionCount = Math.Max(0, optionCount);
        inventoryRows = Math.Clamp(inventoryRows, EquipmentRows, 6);
        equipmentColumns = Math.Clamp(equipmentColumns, 0, 4);
        int columns = optionCount > 1 ? Math.Min(MaxColumns, optionCount) : 0;
        int rows = columns > 0 ? (optionCount + columns - 1) / columns : 0;
        int selectorHeight = rows > 0 ? rows * CardHeight + (rows - 1) * CardGap + SelectorBottomPadding : 0;
        int bodyHeight = Math.Max(MinimumBodyHeight, inventoryRows * SlotStep);
        int designWidth = Padding * 2 + PreviewWidth + PreviewInventoryGap + InventoryColumns * SlotStep
            + (equipmentColumns > 0 ? InventoryEquipmentGap + equipmentColumns * SlotStep : 0);
        int designHeight = HeaderHeight + selectorHeight + bodyHeight + Padding;
        float widthScale = Math.Min(1f, Math.Max(1, width - 12) / (float)designWidth);
        int preferredTop = Math.Clamp(top, 0, Math.Max(0, height - 1));
        int minimumTop = Math.Min(56, preferredTop);
        int actualTop = Math.Clamp(height - 12 - (int)MathF.Round(designHeight * widthScale), minimumTop, preferredTop);
        float scale = Math.Max(.001f, Math.Min(widthScale, Math.Max(1, height - actualTop - 12) / (float)designHeight));
        int S(float value) => Math.Max(1, (int)MathF.Round(value * scale));
        Rectangle panel = new((width - S(designWidth)) / 2, actualTop, S(designWidth), S(designHeight));
        Rectangle Box(int x, int y, int w, int h) => new(panel.X + (int)MathF.Round(x * scale), panel.Y + (int)MathF.Round(y * scale), S(w), S(h));
        int bodyY = HeaderHeight + selectorHeight;
        int inventoryX = Padding + PreviewWidth + PreviewInventoryGap;
        Rectangle selector = rows > 0 ? Box(Padding, HeaderHeight, designWidth - Padding * 2, selectorHeight - SelectorBottomPadding) : Rectangle.Empty;
        Rectangle equipment = equipmentColumns > 0 ? Box(inventoryX + InventoryColumns * SlotStep + InventoryEquipmentGap,
            bodyY, equipmentColumns * SlotStep, EquipmentRows * SlotStep) : Rectangle.Empty;
        return new(panel, Box(Padding, 8, designWidth - Padding * 2, 48),
            Box(Padding, 62, designWidth - Padding * 2 - 96, 28), Box(designWidth - Padding - 80, 63, 80, 26),
            Box(Padding, 94, designWidth - Padding * 2, 3), selector,
            Box(Padding, bodyY, PreviewWidth, bodyHeight - StatsHeight - PortraitStatsGap),
            Box(Padding, bodyY + bodyHeight - StatsHeight, PreviewWidth, StatsHeight),
            Box(inventoryX, bodyY, InventoryColumns * SlotStep, inventoryRows * SlotStep), equipment,
            scale, optionCount, columns, rows, inventoryRows, equipmentColumns);
    }

    internal static LoadoutPreviewCard Card(LoadoutPreviewLayout layout, int index)
    {
        if (layout.Columns == 0 || index < 0 || index >= layout.OptionCount)
            return default;
        int S(float value) => Math.Max(1, (int)MathF.Round(value * layout.Scale));
        int row = index / layout.Columns;
        int count = Math.Min(layout.Columns, layout.OptionCount - row * layout.Columns);
        int width = S(CardWidth), gap = S(CardGap);
        int rowWidth = count * width + (count - 1) * gap;
        Rectangle card = new(layout.Panel.Center.X - rowWidth / 2 + index % layout.Columns * (width + gap),
            layout.SelectorBox.Y + (int)MathF.Round(row * (CardHeight + CardGap) * layout.Scale), width, S(CardHeight));
        return new(card, new(card.Center.X - S(16), card.Y + S(4), S(32), S(32)),
            new(card.X + S(12), card.Y + S(40), card.Width - S(24), S(26)),
            new(card.X + S(12), card.Y + S(68), card.Width - S(24), S(2)));
    }

    internal static Rectangle InventorySlot(LoadoutPreviewLayout layout, int index) => Slot(layout.InventoryBox,
        index % InventoryColumns, index / InventoryColumns, layout.Scale);

    internal static Rectangle EquipmentSlot(LoadoutPreviewLayout layout, int index) => Slot(layout.EquipmentBox,
        index / EquipmentRows, index % EquipmentRows, layout.Scale);

    private static Rectangle Slot(Rectangle area, int column, int row, float scale) => new(
        area.X + (int)MathF.Round(column * SlotStep * scale), area.Y + (int)MathF.Round(row * SlotStep * scale),
        Math.Max(1, (int)MathF.Round(SlotSize * scale)), Math.Max(1, (int)MathF.Round(SlotSize * scale)));

    internal static Rectangle Stat(LoadoutPreviewLayout layout, int index)
    {
        int padding = Math.Max(1, (int)MathF.Round(4 * layout.Scale));
        Rectangle area = layout.StatsBox;
        int half = (area.Width - padding * 2) / 2;
        return new(area.X + padding + index * half, area.Y, half, area.Height);
    }

    internal static Rectangle SlotLabelBox(Rectangle cell, bool stack)
    {
        int inset = Math.Max(1, (int)MathF.Round(cell.Width / 18f));
        int height = Math.Max(1, cell.Height / 2);
        return new(cell.X + inset, stack ? cell.Bottom - height : cell.Y + inset,
            Math.Max(1, cell.Width - inset * 2), Math.Max(1, height - inset));
    }

    internal static float TextScale(Vector2 measured, Rectangle area, float preferred) => Math.Min(preferred,
        Math.Min(area.Width / Math.Max(1f, measured.X), area.Height / Math.Max(1f, measured.Y)));

    internal static float PortraitScale(LoadoutPreviewLayout layout) => Math.Min(layout.Scale,
        Math.Max(0f, layout.PortraitBox.Height - 8f * layout.Scale) / 86f);
}

internal readonly record struct LoadoutPreviewLayout(Rectangle Panel, Rectangle TitleBox, Rectangle BossNameBox,
    Rectangle TimerBox, Rectangle ProgressBox, Rectangle SelectorBox, Rectangle PortraitBox, Rectangle StatsBox,
    Rectangle InventoryBox, Rectangle EquipmentBox, float Scale, int OptionCount, int Columns, int Rows, int GridRows, int EquipmentColumns);

internal readonly record struct LoadoutPreviewCard(Rectangle Bounds, Rectangle IconBox, Rectangle NameBox, Rectangle SelectionLine);
