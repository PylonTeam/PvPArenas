// Retired Arrange items implementation; retained for reference only.
// using Microsoft.Xna.Framework;
// using Microsoft.Xna.Framework.Graphics;
// using PvPArenas.Common.UI;
// using PvPArenas.Core.Configs;
// using PvPFramework.Core.Utilities;
// using System;
// using System.Collections.Generic;
// using Terraria;
// using Terraria.Audio;
// using Terraria.GameContent;
// using Terraria.GameInput;
// using Terraria.ID;
// using Terraria.Localization;
// using Terraria.ModLoader;
// using Terraria.ModLoader.Config;
// using Terraria.UI;
//
// namespace PvPArenas._Deprecated.LoadoutSelector;
//
// /// <summary>
// /// Shows the selected arena loadout below the scoreline during the freeze countdown.
// /// Supports any number of named loadout options configured by the selected fight preset.
// /// </summary>
// internal static class LoadoutPreviewDrawer
// {
//     private readonly record struct SlotEntry(
//         Item Item,
//         bool Equip,
//         int? HotbarNumber,
//         int InventoryIndex = -1,
//         SandboxSlot? Sandbox = null);
//
//     // Equipment slot order, matching how Rebuild adds them. The null entry is the
//     // empty gap between the accessories and the hook/mount column.
//     private static readonly SandboxSlotKind?[] EquipKinds =
//     [
//         SandboxSlotKind.Head, SandboxSlotKind.Body, SandboxSlotKind.Legs,
//         SandboxSlotKind.Accessory1, SandboxSlotKind.Accessory2, SandboxSlotKind.Accessory3,
//         SandboxSlotKind.Accessory4, SandboxSlotKind.Accessory5,
//         null,
//         SandboxSlotKind.GrapplingHook, SandboxSlotKind.Mount
//     ];
//
//     private const int InventoryColumns = 10;
//     private const int EquipmentRows = 3;
//     private const int SlotStep = 40;
//     private const int SlotSize = 36;
//     private const int EquipmentPhysicsKeyOffset = 1000;
//
//     private const int HeaderHeight = 76;
//     private const int PreviewWidth = 96;
//     private const int SidePadding = 14;
//     private const int PreviewInventoryGap = 12;
//     private const int InventoryEquipmentGap = 10;
//
//     private const int MaxLoadoutColumns = 4;
//     private const int LoadoutCardHeight = 62;
//     private const int LoadoutCardMaxWidth = 180;
//     private const int LoadoutSelectorBottomPadding = 8;
//     private const int CardGap = 8;
//
//     private const int EditButtonAreaHeight = 42;
//
//     private static bool editMode;
//     private static int heldItemIndex = -1;
//     private static Item heldItem = new();
//     private static List<int> editOrder = [];
//     private static BossFightPreset currentPreset;
//     private static int editingLoadoutIndex = -1;
//
//     private const int PreviewJumpDuration = 32;
//     private const float PreviewJumpHeight = 30f;
//
//     private static Texture2D PanelBackground =>
//         Main.Assets.Request<Texture2D>(
//             "Images/UI/PanelBackground").Value;
//
//     private static Texture2D PanelBorder =>
//         Main.Assets.Request<Texture2D>(
//             "Images/UI/PanelBorder").Value;
//
//     private static readonly RasterizerState ClipRasterizer = new() { CullMode = CullMode.None, ScissorTestEnable = true };
//     private static readonly List<SlotEntry> inventorySlots = [];
//     private static readonly List<SlotEntry> equipmentSlots = [];
//
//     private static Player previewPlayer;
//
//     private static int cachedPresetIndex = -1;
//     private static int cachedDisplayLoadoutIndex = -1;
//     private static int cachedMaxHealth = 100;
//     private static int cachedMaxMana = 20;
//     private static int layoutRows;
//     private static bool cacheValid;
//
//     private static int localLoadoutIndex;
//     private static int hoveredLoadoutIndex = -1;
//
//     private static int previewJumpTicks;
//     private static bool previewJumpWasDown;
//
//     private static float openingAge, alpha;
//     private static float[] hoverAmounts = [], selectionAmounts = [];
//     private static readonly float[] slotHoverAmounts = new float[50 + EquipKinds.Length];
//     private static int hoveredSlot = -1;
//     private static bool editButtonHovered;
//     private static float editHoverAmount;
//
//     private static string Label(string key) => Language.GetTextValue("Mods.PvPArenas.LoadoutSelector." + key);
//
//     internal static void Update(float seconds)
//     {
//         RoundManager manager = ModContent.GetInstance<RoundManager>();
//         if (!manager.TryGetSelectedPreset(out BossFightPreset preset)) return;
//         int count = preset.Loadouts?.Count ?? 0;
//         if (hoverAmounts.Length != count)
//         {
//             hoverAmounts = new float[count];
//             selectionAmounts = new float[count];
//             layoutRows = 0;
//         }
//
//         openingAge += Math.Max(0f, seconds);
//         alpha = ArenaUIStyle.Ease(Math.Clamp(openingAge / .2f, 0f, 1f));
//         float blend = ArenaUIStyle.HoverBlend(seconds);
//         int selected = NormalizeLoadoutIndex(preset, Main.LocalPlayer.GetModPlayer<ArenaPlayer>().SelectedLoadoutIndex);
//         for (int i = 0; i < count; i++)
//         {
//             hoverAmounts[i] = MathHelper.Lerp(hoverAmounts[i], hoveredLoadoutIndex == i && !editMode ? 1f : 0f, blend);
//             selectionAmounts[i] = MathHelper.Lerp(selectionAmounts[i], selected == i ? 1f : 0f, blend);
//         }
//         editHoverAmount = MathHelper.Lerp(editHoverAmount, editButtonHovered ? 1f : 0f, blend);
//         for (int i = 0; i < slotHoverAmounts.Length; i++)
//             slotHoverAmounts[i] = MathHelper.Lerp(slotHoverAmounts[i], hoveredSlot == i ? 1f : 0f, blend);
//     }
//
//     internal static void Reset()
//     {
//         StopEditing(save: false);
//         SandboxLoadoutEditor.Close();
//         currentPreset = null;
//         cachedPresetIndex = cachedDisplayLoadoutIndex = hoveredLoadoutIndex = hoveredSlot = -1;
//         cacheValid = false;
//         previewPlayer = null;
//         layoutRows = 0;
//         previewJumpTicks = 0;
//         previewJumpWasDown = editButtonHovered = false;
//         openingAge = alpha = editHoverAmount = 0f;
//         Array.Clear(hoverAmounts);
//         Array.Clear(selectionAmounts);
//         Array.Clear(slotHoverAmounts);
//     }
//
//     private static int DisplayLoadoutIndex =>
//         hoveredLoadoutIndex >= 0
//             ? hoveredLoadoutIndex
//             : localLoadoutIndex;
//
//     private static int GetGridRowCount(BossFightPreset preset)
//     {
//         // Every choice uses the same geometry, so previews cannot move their own hitboxes.
//         if (layoutRows > 0) return layoutRows;
//         int rows = preset.IsSandbox() ? 5 : EquipmentRows;
//         for (int i = 0; i < (preset.Loadouts?.Count ?? 0); i++)
//         {
//             Loadout loadout = ArenaPlayer.ResolveBaseLoadout(preset, i);
//             int slots = Math.Clamp(loadout.Inventory?.Count ?? 0, 10, 50);
//             rows = Math.Max(rows, (slots + InventoryColumns - 1) / InventoryColumns);
//         }
//         return layoutRows = rows;
//     }
//
//     public static void Draw(int top)
//     {
//         RoundManager manager =
//             ModContent.GetInstance<RoundManager>();
//
//         if (!manager.TryGetSelectedPreset(
//                 out BossFightPreset preset))
//         {
//             return;
//         }
//         currentPreset = preset;
//
//         int optionCount = preset.Loadouts?.Count ?? 0;
//         bool showSelector = optionCount > 1;
//
//         Point mouse = PlayerInput.IgnoreMouseInterface || alpha < .95f
//             ? new Point(int.MinValue, int.MinValue)
//             : new Point(Main.mouseX, Main.mouseY);
//         editButtonHovered = false;
//         hoveredSlot = -1;
//
//         // While picking an item, the whole loadout body is replaced by the picker.
//         if (preset.IsSandbox() && SandboxLoadoutEditor.IsOpen)
//         {
//             SandboxLoadoutEditor.DrawOverlay(top, alpha);
//             return;
//         }
//
//         ArenaPlayer localArenaPlayer =
//             Main.LocalPlayer.GetModPlayer<ArenaPlayer>();
//
//         int playerSelectedIndex =
//             NormalizeLoadoutIndex(
//                 preset,
//                 localArenaPlayer.SelectedLoadoutIndex);
//
//         if (manager.SelectedPresetIndex != cachedPresetIndex)
//         {
//             StopEditing(save: false);
//             layoutRows = 0;
//
//             localLoadoutIndex = playerSelectedIndex;
//             hoveredLoadoutIndex = -1;
//             InvalidateCache();
//         }
//         else if (!editMode &&
//          hoveredLoadoutIndex < 0 &&
//          localLoadoutIndex != playerSelectedIndex)
//         {
//             // Handles resets between consecutive rounds that happen to use
//             // the same preset index.
//             localLoadoutIndex = playerSelectedIndex;
//             InvalidateCache();
//         }
//
//         if (!showSelector)
//             hoveredLoadoutIndex = -1;
//
//         int gridRows = GetGridRowCount(preset);
//         int designWidth = GetDesignWidth((EquipKinds.Length + EquipmentRows - 1) / EquipmentRows);
//         int designHeight = GetDesignHeight(optionCount, gridRows);
//         float scale = CalculateScale(top, designWidth, designHeight);
//         int S(float value) => Math.Max(1, (int)MathF.Round(value * scale));
//         Rectangle panel = new((Main.screenWidth - S(designWidth)) / 2, top, S(designWidth), S(designHeight));
//
//         hoveredLoadoutIndex = !editMode && showSelector
//             ? GetHoveredLoadoutIndex(preset, panel, panel.Y + S(HeaderHeight), S, mouse)
//             : -1;
//         EnsureRebuilt(manager.SelectedPresetIndex, preset);
//
//         // Reveal from the center with voting's timing; preserve clipping through the player renderer.
//         GraphicsDevice device = Main.spriteBatch.GraphicsDevice;
//         Rectangle oldClip = device.ScissorRectangle;
//         RasterizerState oldRasterizer = device.RasterizerState;
//         BlendState oldBlend = device.BlendState;
//         SamplerState oldSampler = device.SamplerStates[0];
//         int revealHeight = Math.Max(1, (int)MathF.Round(panel.Height * alpha));
//         Rectangle clip = new(panel.X, panel.Center.Y - revealHeight / 2, panel.Width, revealHeight);
//         Vector2 first = Vector2.Transform(clip.TopLeft(), Main.UIScaleMatrix);
//         Vector2 last = Vector2.Transform(clip.BottomRight(), Main.UIScaleMatrix);
//         Rectangle screenClip = new((int)first.X, (int)first.Y,
//             Math.Max(1, (int)(last.X - first.X)), Math.Max(1, (int)(last.Y - first.Y)));
//         Main.spriteBatch.End();
//         Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp,
//             DepthStencilState.None, ClipRasterizer, null, Main.UIScaleMatrix);
//         device.ScissorRectangle = Rectangle.Intersect(oldRasterizer.ScissorTestEnable ? oldClip : device.Viewport.Bounds, screenClip);
//         try
//         {
//             DrawPanel(
//                 panel,
//                 ArenaUIStyle.PanelFill,
//                 ArenaUIStyle.PanelEdge,
//                 S(10));
//
//             string title = Label("Header");
//             Utils.DrawBorderStringBig(
//                 Main.spriteBatch,
//                 title,
//                 new Vector2(
//                     panel.Center.X,
//                     panel.Y + S(4)),
//                 ArenaUIStyle.Title * alpha,
//                 Math.Min(.63f * scale, (panel.Width - S(28)) / Math.Max(1f, FontAssets.DeathText.Value.MeasureString(title).X)),
//                 .5f,
//                 0f);
//
//             string bossName =
//                 preset.IsSandbox()
//                     ? Label("Sandbox")
//                     : Lang.GetNPCNameValue(
//                         preset.Boss?.Type ?? NPCID.None);
//
//             Text(
//                 bossName,
//                 new Vector2(
//                     panel.X + S(15),
//                     panel.Y + S(38)),
//                 Color.White,
//                 .94f * scale,
//                 panel.Width - S(110), 0f);
//
//             int seconds = Math.Max(0, (manager.RemainingTicks + 59) / 60);
//             Text($"{seconds / 60}:{seconds % 60:00}", new Vector2(panel.Right - S(15), panel.Y + S(40)),
//                 Color.Silver, .7f * scale, S(76), 1f);
//             Rectangle track = new(panel.X + S(15), panel.Y + S(64), panel.Width - S(30), S(3));
//             float progress = Math.Clamp(manager.RemainingTicks /
//                 (60f * Math.Max(1, ModContent.GetInstance<ServerConfig>().FreezeCountdownSeconds)), 0f, 1f);
//             Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, track, Color.Black * (.3f * alpha));
//             Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value,
//                 new Rectangle(track.X, track.Y, (int)(track.Width * progress), track.Height), ArenaUIStyle.Progress * alpha);
//
//             if (!PlayerInput.IgnoreMouseInterface && panel.Contains(Main.mouseX, Main.mouseY))
//                 Main.LocalPlayer.mouseInterface = true;
//
//             int contentTop =
//                 panel.Y + S(HeaderHeight);
//
//             if (showSelector)
//             {
//                 DrawLoadoutCards(
//                     preset,
//                     panel,
//                     contentTop,
//                     S,
//                     scale,
//                     mouse);
//
//                 contentTop +=
//                     S(GetLoadoutSelectorHeight(optionCount));
//             }
//
//             int gridHeight =
//                 gridRows * S(SlotStep);
//
//             Rectangle previewBox = new(
//                 panel.X + S(SidePadding),
//                 contentTop,
//                 S(PreviewWidth),
//                 gridHeight);
//
//             DrawPreviewBox(
//                 previewBox,
//                 mouse);
//
//             DrawLoadoutStats(previewBox, scale, S);
//
//             int inventoryOriginX =
//                 previewBox.Right
//                 + S(PreviewInventoryGap);
//
//             DrawInventorySlots(
//                 inventoryOriginX,
//                 contentTop,
//                 scale,
//                 mouse,
//                 S);
//
//             if (equipmentSlots.Count > 0)
//             {
//                 int equipmentOriginX =
//                     inventoryOriginX
//                     + InventoryColumns * S(SlotStep)
//                     + S(InventoryEquipmentGap);
//
//                 DrawEquipmentSlots(
//                     equipmentOriginX,
//                     contentTop,
//                     EquipmentRows,
//                     scale,
//                     mouse,
//                     S);
//             }
//
//             // Edit loadout button. Sandbox loadouts are edited directly by clicking
//             // slots, so they skip the reorder-style edit button entirely.
//             if (!preset.IsSandbox())
//             {
//                 int gridBottom =
//                     contentTop +
//                     gridRows * S(SlotStep);
//
//                 Rectangle editButton = new(
//                     panel.Center.X - S(82),
//                     gridBottom + S(7),
//                     S(164),
//                     S(28));
//
//                 DrawEditButton(
//                     editButton,
//                     preset,
//                     scale,
//                     mouse,
//                     S);
//             }
//         }
//         finally
//         {
//             Main.spriteBatch.End();
//             Main.spriteBatch.Begin(SpriteSortMode.Deferred, oldBlend, oldSampler,
//                 DepthStencilState.None, oldRasterizer, null, Main.UIScaleMatrix);
//             device.ScissorRectangle = oldClip;
//         }
//         // Cursor items must remain visible when dragged beyond the panel.
//         DrawHeldItem(scale);
//     }
//
//     private static void DrawEditButton(
//     Rectangle button,
//     BossFightPreset preset,
//     float scale,
//     Point mouse,
//     Func<float, int> S)
//     {
//         bool hovered = editButtonHovered = button.Contains(mouse);
//
//         DrawPanel(
//             button,
//             ArenaUIStyle.ChoiceFill(editHoverAmount, editMode ? 1f : 0f),
//             Color.Transparent,
//             S(8));
//
//         Text(
//             Label(editMode ? "Done" : "ArrangeItems"),
//             new Vector2(
//                 button.Center.X,
//                 button.Y + S(6)),
//             editMode ? ArenaUIStyle.Accent : Color.White,
//             .78f * scale,
//             button.Width - S(12));
//
//         if (!hovered)
//             return;
//
//         Main.LocalPlayer.mouseInterface = true;
//
//         if (!Main.mouseLeft || !Main.mouseLeftRelease)
//             return;
//
//         Main.mouseLeftRelease = false;
//         SoundEngine.PlaySound(SoundID.MenuTick);
//
//         if (editMode)
//             StopEditing(save: true);
//         else
//             StartEditing(preset);
//     }
//
//     private static void StartEditing(BossFightPreset preset)
//     {
//         currentPreset = preset;
//         hoveredLoadoutIndex = -1;
//         ClearHeldItem();
//
//         editingLoadoutIndex =
//             NormalizeLoadoutIndex(
//                 preset,
//                 localLoadoutIndex);
//
//         Loadout loadout =
//             ArenaPlayer.ResolveBaseLoadout(
//                 preset,
//                 editingLoadoutIndex);
//
//         editOrder =
//             LocalLoadoutOrder.GetOrder(
//                 preset,
//                 editingLoadoutIndex,
//                 loadout);
//
//         editMode = true;
//         LoadoutItemPhysics.Reset();
//         InvalidateCache();
//
//         Log.Chat(
//             $"Started. loadout={editingLoadoutIndex}, " +
//             $"slots={editOrder.Count}.");
//     }
//
//     private static void StopEditing(bool save)
//     {
//         if (!editMode)
//             return;
//
//         if (save)
//         {
//             if (heldItemIndex >= 0)
//             {
//                 int emptySlot =
//                     editOrder.IndexOf(-1);
//
//                 if (emptySlot >= 0)
//                 {
//                     editOrder[emptySlot] =
//                         heldItemIndex;
//
//                     Log.Chat(
//                         $"Returned held item " +
//                         $"{heldItemIndex} to slot {emptySlot}.");
//                 }
//                 else
//                 {
//                     Log.Chat(
//                         $"No empty slot for held item " +
//                         $"{heldItemIndex}.");
//                 }
//             }
//
//             SaveEditing();
//         }
//
//         editMode = false;
//         editingLoadoutIndex = -1;
//         ClearHeldItem();
//         editOrder.Clear();
//         LoadoutItemPhysics.Reset();
//         InvalidateCache();
//     }
//
//     private static void SaveEditing()
//     {
//         if (currentPreset == null ||
//             editingLoadoutIndex < 0)
//         {
//             return;
//         }
//
//         Loadout loadout =
//             ArenaPlayer.ResolveBaseLoadout(
//                 currentPreset,
//                 editingLoadoutIndex);
//
//         LocalLoadoutOrder.SetOrder(
//             currentPreset,
//             editingLoadoutIndex,
//             loadout,
//             editOrder);
//
//         ArenaPlayer.RequestLoadoutSelect(
//             editingLoadoutIndex);
//     }
//     private static void ClearHeldItem()
//     {
//         heldItemIndex = -1;
//         heldItem.TurnToAir();
//     }
//
//     private static Item GetOriginalItem(int originalIndex)
//     {
//         if (originalIndex < 0 ||
//             currentPreset == null ||
//             editingLoadoutIndex < 0)
//         {
//             return new Item();
//         }
//
//         Loadout loadout =
//             ArenaPlayer.ResolveBaseLoadout(
//                 currentPreset,
//                 editingLoadoutIndex);
//
//         LoadoutItem entry =
//             LocalLoadoutOrder.ItemAt(
//                 loadout,
//                 originalIndex);
//
//         return MakeItem(
//             entry?.Item?.Type ?? ItemID.None,
//             entry?.Stack ?? 1);
//     }
//     private static void HandleEditClick(
//     Rectangle cell,
//     SlotEntry entry,
//     Point mouse)
//     {
//         if (!editMode ||
//             entry.InventoryIndex < 0 ||
//             entry.InventoryIndex >= editOrder.Count ||
//             !cell.Contains(mouse))
//         {
//             return;
//         }
//
//         Main.LocalPlayer.mouseInterface = true;
//
//         if (!Main.mouseLeft ||
//             !Main.mouseLeftRelease)
//         {
//             return;
//         }
//
//         Main.mouseLeftRelease = false;
//
//         int slotIndex = entry.InventoryIndex;
//         int clickedItemIndex = editOrder[slotIndex];
//
//         if (heldItemIndex < 0 &&
//             clickedItemIndex < 0)
//         {
//             Log.Chat(
//                 $"Clicked empty slot {slotIndex}.");
//
//             return;
//         }
//
//         int previousHeldIndex = heldItemIndex;
//
//         LoadoutItemPhysics.SwapWithCursor(slotIndex);
//
//         // Resolve and cache the item being picked up before changing the order.
//         Item nextHeldItem = clickedItemIndex >= 0
//             ? entry.Item.Clone()
//             : new Item();
//
//         // Fall back to the original preset item if the displayed slot happened
//         // to be stale for this frame.
//         if (clickedItemIndex >= 0 &&
//             nextHeldItem.IsAir)
//         {
//             nextHeldItem =
//                 GetOriginalItem(clickedItemIndex);
//         }
//
//         // Put the previously held item into the clicked slot.
//         editOrder[slotIndex] =
//             previousHeldIndex;
//
//         // Pick up the item that was in the clicked slot.
//         heldItemIndex =
//             clickedItemIndex;
//
//         heldItem =
//             nextHeldItem;
//
//         if (heldItemIndex < 0)
//             heldItem.TurnToAir();
//
//         Log.Chat(
//             $"Slot {slotIndex}: " +
//             $"placed={previousHeldIndex}, " +
//             $"pickedUp={heldItemIndex}, " +
//             $"visual={heldItem.type}:{heldItem.Name}.");
//
//         InvalidateCache();
//
//         // Moving into an empty slot completes the operation.
//         if (heldItemIndex < 0)
//             SaveEditing();
//     }
//
//     private static void DrawHeldItem(float scale)
//     {
//         if (!editMode ||
//             heldItemIndex < 0 ||
//             heldItem == null ||
//             heldItem.IsAir)
//         {
//             return;
//         }
//
//         Main.LocalPlayer.mouseInterface = true;
//
//         LoadoutItemPhysics.DrawCursorItem(
//             heldItem,
//             Main.MouseScreen + new Vector2(28f, 28f),
//             scale,
//             40f,
//             alpha);
//     }
//
//     private static int GetDesignWidth(
//         int equipmentColumns)
//     {
//         return SidePadding * 2
//             + PreviewWidth
//             + PreviewInventoryGap
//             + InventoryColumns * SlotStep
//             + (equipmentColumns > 0
//                 ? InventoryEquipmentGap
//                     + equipmentColumns * SlotStep
//                 : 0);
//     }
//
//     private static int GetDesignHeight(
//         int optionCount,
//         int inventoryRows)
//     {
//         return HeaderHeight
//             + GetLoadoutSelectorHeight(optionCount)
//             + inventoryRows * SlotStep
//             + SidePadding
//             + EditButtonAreaHeight; // Extra space for the edit loadout button
//     }
//
//     private static float CalculateScale(
//         int top,
//         int designWidth,
//         int designHeight)
//     {
//         return Math.Min(
//             1f,
//             Math.Min(
//                 (Main.screenWidth - 12f)
//                     / designWidth,
//                 (Main.screenHeight - top - 2f)
//                     / designHeight));
//     }
//
//     private static int GetLoadoutSelectorRows(
//         int optionCount)
//     {
//         if (optionCount <= 1)
//             return 0;
//
//         return (
//             optionCount
//             + MaxLoadoutColumns
//             - 1)
//             / MaxLoadoutColumns;
//     }
//
//     private static int GetLoadoutSelectorHeight(
//         int optionCount)
//     {
//         int rows =
//             GetLoadoutSelectorRows(optionCount);
//
//         if (rows == 0)
//             return 0;
//
//         return rows * LoadoutCardHeight
//             + (rows - 1) * CardGap
//             + LoadoutSelectorBottomPadding;
//     }
//
//     private static Rectangle GetLoadoutCardRectangle(
//         Rectangle panel,
//         int top,
//         int optionCount,
//         int index,
//         Func<float, int> S)
//     {
//         int columns =
//             Math.Min(
//                 MaxLoadoutColumns,
//                 optionCount);
//
//         int gap = S(CardGap);
//         int side = S(SidePadding);
//         int cardHeight = S(LoadoutCardHeight);
//
//         int availableWidth =
//             panel.Width
//             - side * 2
//             - gap * (columns - 1);
//
//         int cardWidth = Math.Min(
//             availableWidth / columns,
//             S(LoadoutCardMaxWidth));
//
//         int row = index / columns;
//         int column = index % columns;
//
//         int firstIndexInRow =
//             row * columns;
//
//         int optionsInRow =
//             Math.Min(
//                 columns,
//                 optionCount - firstIndexInRow);
//
//         int rowWidth =
//             optionsInRow * cardWidth
//             + (optionsInRow - 1) * gap;
//
//         int rowX =
//             panel.Center.X - rowWidth / 2;
//
//         return new Rectangle(
//             rowX + column * (cardWidth + gap),
//             top + row * (cardHeight + gap),
//             cardWidth,
//             cardHeight);
//     }
//
//     private static int GetHoveredLoadoutIndex(
//         BossFightPreset preset,
//         Rectangle panel,
//         int top,
//         Func<float, int> S,
//         Point mouse)
//     {
//         int optionCount =
//             preset.Loadouts?.Count ?? 0;
//
//         for (int i = 0; i < optionCount; i++)
//         {
//             Rectangle card =
//                 GetLoadoutCardRectangle(
//                     panel,
//                     top,
//                     optionCount,
//                     i,
//                     S);
//
//             if (card.Contains(mouse))
//                 return i;
//         }
//
//         return -1;
//     }
//
//     private static void DrawLoadoutCards(
//         BossFightPreset preset,
//         Rectangle panel,
//         int top,
//         Func<float, int> S,
//         float scale,
//         Point mouse)
//     {
//         int optionCount =
//             preset.Loadouts?.Count ?? 0;
//
//         for (int i = 0; i < optionCount; i++)
//         {
//             ArenaLoadoutOption option =
//                 preset.Loadouts[i];
//
//             Rectangle card =
//                 GetLoadoutCardRectangle(
//                     panel,
//                     top,
//                     optionCount,
//                     i,
//                     S);
//
//             bool hovered =
//                 hoveredLoadoutIndex == i;
//
//             bool selected =
//                 localLoadoutIndex == i;
//
//             float hover = i < hoverAmounts.Length ? hoverAmounts[i] : 0f;
//             float selection = i < selectionAmounts.Length ? selectionAmounts[i] : selected ? 1f : 0f;
//             DrawPanel(card, ArenaUIStyle.ChoiceFill(hover, selection), Color.Transparent, S(8));
//             Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value,
//                 new Rectangle(card.X + S(10), card.Bottom - S(5), card.Width - S(20), S(2)),
//                 ArenaUIStyle.Accent * (selection * alpha));
//
//             if (hovered)
//             {
//                 Main.LocalPlayer.mouseInterface = true;
//
//                 if (!editMode &&
//                     Main.mouseLeft &&
//                     Main.mouseLeftRelease &&
//                     !selected)
//                 {
//                     Main.mouseLeftRelease = false;
//                     SoundEngine.PlaySound(SoundID.MenuTick);
//
//                     localLoadoutIndex = i;
//
//                     ArenaPlayer.RequestLoadoutSelect(i);
//
//                     InvalidateCache();
//                 }
//             }
//
//             Rectangle iconBox = new(
//                 card.Center.X - S(16),
//                 card.Y + S(5),
//                 S(32),
//                 S(32));
//
//             Item icon =
//                 MakeItem(GetLoadoutIconType(option));
//
//             if (!icon.IsAir)
//             {
//                 ItemSlot.DrawItemIcon(
//                     icon,
//                     31,
//                     Main.spriteBatch,
//                     iconBox.Center.ToVector2(),
//                     scale,
//                     iconBox.Width - S(6),
//                     Color.White * alpha);
//             }
//
//             string name =
//                 string.IsNullOrWhiteSpace(option?.Name)
//                     ? Language.GetTextValue("Mods.PvPArenas.LoadoutSelector.UnnamedLoadout", i + 1)
//                     : option.Name;
//
//             Color nameColor = Color.Lerp(Color.White, ArenaUIStyle.Accent, selection);
//
//             Text(
//                 name,
//                 new Vector2(
//                     card.Center.X,
//                     card.Y + S(39)),
//                 nameColor,
//                 .78f * scale,
//                 card.Width - S(8));
//         }
//     }
//
//     private static int GetLoadoutIconType(
//         ArenaLoadoutOption option)
//     {
//         int helmet =
//             option?.Loadout
//                 ?.Armor
//                 ?.Head
//                 ?.Type
//             ?? ItemID.None;
//
//         if (helmet > ItemID.None)
//             return helmet;
//
//         List<LoadoutItem> inventory =
//             option?.Loadout?.Inventory;
//
//         if (inventory == null)
//             return ItemID.None;
//
//         foreach (LoadoutItem entry in inventory)
//         {
//             int type =
//                 entry?.Item?.Type
//                 ?? ItemID.None;
//
//             if (type > ItemID.None)
//                 return type;
//         }
//
//         return ItemID.None;
//     }
//
//     private static int NormalizeLoadoutIndex(
//         BossFightPreset preset,
//         int index)
//     {
//         int count =
//             preset?.Loadouts?.Count ?? 0;
//
//         if (count == 0)
//             return 0;
//
//         return index >= 0 && index < count
//             ? index
//             : 0;
//     }
//
//     private static void EnsureRebuilt(
//         int presetIndex,
//         BossFightPreset preset)
//     {
//         int displayIndex =
//             NormalizeLoadoutIndex(
//                 preset,
//                 DisplayLoadoutIndex);
//
//         if (cacheValid &&
//             presetIndex == cachedPresetIndex &&
//             displayIndex ==
//                 cachedDisplayLoadoutIndex &&
//             previewPlayer != null)
//         {
//             return;
//         }
//
//         Rebuild(
//             presetIndex,
//             preset,
//             displayIndex);
//     }
//
//     private static void Rebuild(
//         int presetIndex,
//         BossFightPreset preset,
//         int loadoutIndex)
//     {
//         cachedPresetIndex = presetIndex;
//         cachedDisplayLoadoutIndex = loadoutIndex;
//         cacheValid = true;
//
//         Loadout baseLoadout =
//     ArenaPlayer.ResolveBaseLoadout(
//         preset,
//         loadoutIndex);
//
//         Loadout loadout =
//             editMode &&
//             loadoutIndex == editingLoadoutIndex
//                 ? LocalLoadoutOrder.Apply(
//                     baseLoadout,
//                     editOrder)
//                 : LocalLoadoutOrder.Apply(
//                     preset,
//                     loadoutIndex,
//                     baseLoadout);
//
//         equipmentSlots.Clear();
//         inventorySlots.Clear();
//
//         AddEquipment(loadout.Armor?.Head);
//         AddEquipment(loadout.Armor?.Body);
//         AddEquipment(loadout.Armor?.Legs);
//
//         AddEquipment(
//             loadout.Accessories?.Accessory1);
//
//         AddEquipment(
//             loadout.Accessories?.Accessory2);
//
//         AddEquipment(
//             loadout.Accessories?.Accessory3);
//
//         AddEquipment(
//             loadout.Accessories?.Accessory4);
//
//         AddEquipment(
//             loadout.Accessories?.Accessory5);
//
//         // Empty gap slot between the accessories and the hook/mount column.
//         AddEquipmentSpacer();
//
//         AddEquipment(
//             loadout.Equipment?.GrapplingHook);
//
//         AddEquipment(
//             loadout.Equipment?.Mount);
//
//         int inventoryCount =
//             Math.Min(
//                 loadout.Inventory?.Count ?? 0,
//                 50);
//
//         // Sandbox always shows the full editable grid; other presets grow to fit.
//         int minimumSlots = preset.IsSandbox()
//             ? LocalSandboxLoadouts.InventorySlots
//             : 10;
//
//         int displayedInventoryCount =
//             Math.Max(
//                 minimumSlots,
//                 inventoryCount);
//
//         for (int i = 0;
//              i < displayedInventoryCount;
//              i++)
//         {
//             LoadoutItem entry =
//                 i < inventoryCount
//                     ? loadout.Inventory[i]
//                     : null;
//
//             inventorySlots.Add(
//                 new SlotEntry(
//                     MakeItem(
//                         entry?.Item?.Type
//                             ?? ItemID.None,
//                         entry?.Stack ?? 1),
//                     false,
//                     i < 10
//                         ? i == 9
//                             ? 10
//                             : i + 1
//                         : null,
//                     i,
//                     preset.IsSandbox()
//                         ? new SandboxSlot(SandboxSlotKind.Inventory, i)
//                         : null));
//         }
//
//         if (preset.IsSandbox())
//         {
//             for (int i = 0; i < equipmentSlots.Count && i < EquipKinds.Length; i++)
//                 if (EquipKinds[i] is SandboxSlotKind kind)
//                     equipmentSlots[i] = equipmentSlots[i] with
//                     {
//                         Sandbox = new SandboxSlot(kind)
//                     };
//         }
//
//         cachedMaxHealth = preset.MaxHealth;
//         cachedMaxMana = preset.MaxMana;
//
//         previewPlayer =
//             BuildPreviewPlayer(loadout);
//     }
//
//     private static void AddEquipment(
//         ItemDefinition definition)
//     {
//         equipmentSlots.Add(
//             new SlotEntry(
//                 MakeItem(
//                     definition?.Type
//                     ?? ItemID.None),
//                 true,
//                 null));
//     }
//
//     private static void AddEquipmentSpacer()
//     {
//         equipmentSlots.Add(
//             new SlotEntry(
//                 new Item(),
//                 true,
//                 null));
//     }
//
//     private static Item MakeItem(
//         int type,
//         int stack = 1)
//     {
//         Item item = new();
//
//         if (type > ItemID.None)
//         {
//             item.SetDefaults(type);
//             item.stack = Math.Max(1, stack);
//         }
//
//         return item;
//     }
//
//     #region Preview player
//     private static Player BuildPreviewPlayer(
//         Loadout loadout)
//     {
//         // A bare "new Player()" is not renderable: the draw path depends on
//         // deep state (dye vars, cloned appearance, frame setup) that only a
//         // fully-initialized player has. Cloning the local player like vanilla's
//         // UICharacter does gives us that for free; we then strip its gear and
//         // apply the loadout so only the loadout armor is visible.
//         Player preview =
//             (Main.LocalPlayer ?? new Player()).SerializedClone();
//
//         preview.active = true;
//         preview.dead = false;
//         preview.ghost = false;
//         preview.direction = 1;
//         preview.isDisplayDollOrInanimate = true;
//         preview.selectedItem = 0;
//
//         for (int i = 0; i < preview.inventory.Length; i++)
//             preview.inventory[i] = new Item();
//
//         for (int i = 0; i < preview.armor.Length; i++)
//             preview.armor[i] = new Item();
//
//         SetPreviewEquipment(
//             preview,
//             0,
//             loadout.Armor?.Head);
//
//         SetPreviewEquipment(
//             preview,
//             1,
//             loadout.Armor?.Body);
//
//         SetPreviewEquipment(
//             preview,
//             2,
//             loadout.Armor?.Legs);
//
//         SetPreviewEquipment(
//             preview,
//             3,
//             loadout.Accessories?.Accessory1);
//
//         SetPreviewEquipment(
//             preview,
//             4,
//             loadout.Accessories?.Accessory2);
//
//         SetPreviewEquipment(
//             preview,
//             5,
//             loadout.Accessories?.Accessory3);
//
//         SetPreviewEquipment(
//             preview,
//             6,
//             loadout.Accessories?.Accessory4);
//
//         SetPreviewEquipment(
//             preview,
//             7,
//             loadout.Accessories?.Accessory5);
//
//         using (new Main.CurrentPlayerOverride(preview))
//         {
//             preview.ResetEffects();
//             preview.ResetVisibleAccessories();
//             preview.UpdateDyes();
//             preview.DisplayDollUpdate();
//             preview.PlayerFrame();
//         }
//
//         return preview;
//     }
//
//     private static void SetPreviewEquipment(
//         Player preview,
//         int slot,
//         ItemDefinition definition)
//     {
//         if (preview?.armor == null ||
//             slot < 0 ||
//             slot >= preview.armor.Length)
//         {
//             return;
//         }
//
//         preview.armor[slot].SetDefaults(
//             definition?.Type
//             ?? ItemID.None);
//     }
//
//     private static void DrawPreviewBox(
//         Rectangle box,
//         Point mouse)
//     {
//         SpriteBatch spriteBatch =
//             Main.spriteBatch;
//
//         DrawPanel(box, ArenaUIStyle.CardFill, Color.Transparent, 8);
//
//         if (previewPlayer == null)
//             return;
//
//         bool hovered = box.Contains(mouse);
//
//         if (hovered)
//             Main.LocalPlayer.mouseInterface = true;
//
//         Vector2 drawPosition =
//             Main.screenPosition
//             + new Vector2(
//                 box.Center.X
//                     - previewPlayer.width / 2f,
//                 box.Center.Y
//                     - previewPlayer.height / 2f);
//
//         previewPlayer.velocity = Vector2.Zero;
//         previewPlayer.active = true;
//         previewPlayer.dead = false;
//         previewPlayer.ghost = false;
//         previewPlayer.isDisplayDollOrInanimate = true;
//
//         float jumpOffset =
//             ApplyPreviewAnimation(hovered);
//
//         drawPosition.Y -= jumpOffset;
//         previewPlayer.position = drawPosition;
//
//         bool wasMenu = Main.gameMenu;
//
//         UISpriteBatchHelper.Restart(
//             spriteBatch,
//             BlendState.AlphaBlend,
//             SamplerState.PointClamp,
//             sortMode: SpriteSortMode.Immediate);
//
//         try
//         {
//             // gameMenu forces full-bright lighting (the panel may sit over
//             // unlit tiles); the player override makes the renderer treat the
//             // preview as the active player, both required for it to appear.
//             Main.gameMenu = true;
//
//             using (new Main.CurrentPlayerOverride(previewPlayer))
//             {
//                 previewPlayer.PlayerFrame();
//
//                 Main.PlayerRenderer.DrawPlayer(
//                     Main.Camera,
//                     previewPlayer,
//                     drawPosition,
//                     0f,
//                     Vector2.Zero,
//                     0f,
//                     1f);
//             }
//         }
//         finally
//         {
//             Main.gameMenu = wasMenu;
//
//             UISpriteBatchHelper.Restart(
//                 spriteBatch,
//                 BlendState.AlphaBlend,
//                 SamplerState.LinearClamp,
//                 sortMode: SpriteSortMode.Deferred);
//         }
//     }
//
//     private static float ApplyPreviewAnimation(
//         bool hovered)
//     {
//         bool jumpDown =
//             PlayerInput.Triggers.Current.Jump;
//
//         bool jumpPressed =
//             jumpDown &&
//             !previewJumpWasDown;
//
//         previewJumpWasDown = jumpDown;
//
//         previewPlayer.SetCompositeArmFront(
//             false,
//             Player.CompositeArmStretchAmount.Full,
//             0f);
//
//         previewPlayer.SetCompositeArmBack(
//             false,
//             Player.CompositeArmStretchAmount.Full,
//             0f);
//
//         previewPlayer.direction = 1;
//
//         if (jumpPressed &&
//             previewJumpTicks <= 0)
//         {
//             previewJumpTicks =
//                 PreviewJumpDuration;
//         }
//
//         if (previewJumpTicks > 0)
//         {
//             float progress =
//                 1f
//                 - previewJumpTicks
//                 / (float)PreviewJumpDuration;
//
//             previewJumpTicks--;
//
//             SetPreviewBodyFrame(5);
//
//             return MathF.Sin(
//                 progress * MathF.PI)
//                 * PreviewJumpHeight;
//         }
//
//         if (!hovered)
//         {
//             SetPreviewBodyFrame(0);
//             return 0f;
//         }
//
//         int frame =
//             7
//             + (int)(
//                 Main.GameUpdateCount
//                 / 5UL
//                 % 13UL);
//
//         SetPreviewBodyFrame(frame);
//         previewPlayer.WingFrame(false);
//
//         return 0f;
//     }
//
//     private static void SetPreviewBodyFrame(
//         int frame)
//     {
//         previewPlayer.bodyFrame.Y =
//             frame
//             * previewPlayer.bodyFrame.Height;
//
//         previewPlayer.legFrame.Y =
//             frame
//             * previewPlayer.legFrame.Height;
//
//         previewPlayer.headFrame.Y = 0;
//     }
//     #endregion
//
//     private static void DrawInventorySlots(
//         int originX,
//         int originY,
//         float scale,
//         Point mouse,
//         Func<float, int> S)
//     {
//         for (int i = 0;
//              i < inventorySlots.Count;
//              i++)
//         {
//             int column =
//                 i % InventoryColumns;
//
//             int row =
//                 i / InventoryColumns;
//
//             Rectangle cell = new(
//                 originX
//                     + column * S(SlotStep),
//                 originY
//                     + row * S(SlotStep),
//                 S(SlotSize),
//                 S(SlotSize));
//
//             DrawSlot(
//                 cell,
//                 inventorySlots[i],
//                 scale,
//                 mouse,
//                 i);
//         }
//     }
//
//     private static void DrawEquipmentSlots(
//         int originX,
//         int originY,
//         int rowCount,
//         float scale,
//         Point mouse,
//         Func<float, int> S)
//     {
//         for (int i = 0;
//              i < equipmentSlots.Count;
//              i++)
//         {
//             int column =
//                 i / rowCount;
//
//             int row =
//                 i % rowCount;
//
//             Rectangle cell = new(
//                 originX
//                     + column * S(SlotStep),
//                 originY
//                     + row * S(SlotStep),
//                 S(SlotSize),
//                 S(SlotSize));
//
//             DrawSlot(
//                 cell,
//                 equipmentSlots[i],
//                 scale,
//                 mouse,
//                 EquipmentPhysicsKeyOffset + i);
//         }
//     }
//
//     private static void DrawSlot(
//         Rectangle cell,
//         SlotEntry entry,
//         float uiScale,
//         Point mouse,
//         int physicsKey)
//     {
//         int hoverKey = physicsKey >= EquipmentPhysicsKeyOffset ? 50 + physicsKey - EquipmentPhysicsKeyOffset : physicsKey;
//         if (cell.Contains(mouse)) hoveredSlot = hoverKey;
//         DrawPanel(cell, ArenaUIStyle.ChoiceFill(slotHoverAmounts[hoverKey], 0f), Color.Transparent,
//             Math.Max(2, (int)MathF.Round(6f * uiScale)));
//
//         if (!entry.Item.IsAir)
//         {
//             if (editMode)
//             {
//                 LoadoutItemPhysics.DrawSlotItem(
//                     entry.Item,
//                     cell.Center.ToVector2(),
//                     uiScale * .85f,
//                     cell.Width - 8f,
//                     alpha,
//                     physicsKey,
//                     cell.Contains(mouse));
//             }
//             else
//             {
//                 ItemSlot.DrawItemIcon(
//                     entry.Item,
//                     31,
//                     Main.spriteBatch,
//                     cell.Center.ToVector2(),
//                     uiScale * .85f,
//                     cell.Width - 8f,
//                     Color.White * alpha);
//             }
//         }
//
//         float textScale =
//             .62f * Math.Max(.75f, uiScale);
//
//         if (entry.HotbarNumber.HasValue)
//         {
//             string number =
//                 entry.HotbarNumber.Value == 10
//                     ? "0"
//                     : entry.HotbarNumber.Value
//                         .ToString();
//
//             Utils.DrawBorderString(
//                 Main.spriteBatch,
//                 number,
//                 new Vector2(
//                     cell.X + 4,
//                     cell.Y + 2),
//                 Color.White * alpha,
//                 textScale);
//         }
//
//         if (entry.Item.stack > 1)
//         {
//             Utils.DrawBorderString(
//                 Main.spriteBatch,
//                 entry.Item.stack.ToString(),
//                 new Vector2(
//                     cell.X + 5,
//                     cell.Bottom
//                         - 16f
//                         * Math.Max(
//                             .75f,
//                             uiScale)),
//                 Color.White * alpha,
//                 textScale);
//         }
//
//         if (!entry.Item.IsAir &&
//             cell.Contains(mouse))
//         {
//             Main.LocalPlayer.mouseInterface = true;
//             Main.HoverItem = entry.Item.Clone();
//             Main.hoverItemName = entry.Item.Name;
//         }
//
//         if (currentPreset?.IsSandbox() == true && entry.Sandbox.HasValue)
//         {
//             SandboxLoadoutEditor.HandleSlotClick(
//                 currentPreset,
//                 cachedDisplayLoadoutIndex,
//                 entry.Sandbox.Value,
//                 cell,
//                 mouse);
//
//             return;
//         }
//
//         HandleEditClick(cell, entry, mouse);
//     }
//
//     // Max life / max mana of the shown loadout, drawn as a small icon + number strip
//     // across the bottom of the preview portrait.
//     private static void DrawLoadoutStats(
//         Rectangle previewBox,
//         float scale,
//         Func<float, int> S)
//     {
//         int height = S(20);
//         Rectangle strip = new(
//             previewBox.X + S(3),
//             previewBox.Bottom - height - S(3),
//             previewBox.Width - S(6),
//             height);
//
//         Main.spriteBatch.Draw(
//             TextureAssets.MagicPixel.Value,
//             strip,
//             ArenaUIStyle.PanelFill * (.85f * alpha));
//
//         int half = strip.Width / 2;
//         DrawStat(
//             TextureAssets.Heart.Value,
//             cachedMaxHealth,
//             new Rectangle(strip.X, strip.Y, half, strip.Height),
//             scale,
//             S);
//
//         DrawStat(
//             TextureAssets.Mana.Value,
//             cachedMaxMana,
//             new Rectangle(strip.X + half, strip.Y, strip.Width - half, strip.Height),
//             scale,
//             S);
//     }
//
//     private static void DrawStat(
//         Texture2D icon,
//         int value,
//         Rectangle area,
//         float scale,
//         Func<float, int> S)
//     {
//         float iconScale = (area.Height - S(4)) / (float)icon.Height;
//         float iconWidth = icon.Width * iconScale;
//         string text = value.ToString();
//         float textScale = .7f * scale;
//         Vector2 textSize =
//             FontAssets.MouseText.Value.MeasureString(text) * textScale;
//         float gap = S(3);
//         float startX =
//             area.Center.X - (iconWidth + gap + textSize.X) / 2f;
//
//         Main.spriteBatch.Draw(
//             icon,
//             new Vector2(startX + iconWidth / 2f, area.Center.Y),
//             null,
//             Color.White * alpha,
//             0f,
//             icon.Size() / 2f,
//             iconScale,
//             SpriteEffects.None,
//             0f);
//
//         Utils.DrawBorderString(
//             Main.spriteBatch,
//             text,
//             new Vector2(
//                 startX + iconWidth + gap,
//                 area.Center.Y - textSize.Y / 2f),
//             Color.White * alpha,
//             textScale);
//     }
//
//     private static void DrawPanel(
//         Rectangle rectangle,
//         Color fill,
//         Color edge,
//         int corner)
//     {
//         Utils.DrawSplicedPanel(
//             Main.spriteBatch,
//             PanelBackground,
//             rectangle.X,
//             rectangle.Y,
//             rectangle.Width,
//             rectangle.Height,
//             corner,
//             corner,
//             corner,
//             corner,
//             fill * alpha);
//
//         Utils.DrawSplicedPanel(
//             Main.spriteBatch,
//             PanelBorder,
//             rectangle.X,
//             rectangle.Y,
//             rectangle.Width,
//             rectangle.Height,
//             corner,
//             corner,
//             corner,
//             corner,
//             edge * alpha);
//     }
//
//     private static void Text(
//         string value,
//         Vector2 position,
//         Color color,
//         float scale,
//         float maxWidth,
//         float anchor = .5f)
//     {
//         float width =
//             FontAssets.MouseText.Value
//                 .MeasureString(value).X
//             * scale;
//
//         if (width > maxWidth)
//             scale *= maxWidth / width;
//
//         Utils.DrawBorderString(
//             Main.spriteBatch,
//             value,
//             position,
//             color * alpha,
//             scale,
//             anchor);
//     }
//
//     private static void InvalidateCache()
//     {
//         cacheValid = false;
//     }
//
//     /// <summary>Forces the next draw to rebuild cached slots (used after a sandbox slot changes).</summary>
//     internal static void Invalidate()
//     {
//         layoutRows = 0;
//         InvalidateCache();
//     }
// }
