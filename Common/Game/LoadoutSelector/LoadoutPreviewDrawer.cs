using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PvPArenas.Common.UI;
using PvPArenas.Core.Configs;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;
using Terraria.UI;
using static PvPArenas.Common.Game.LoadoutPreviewGeometry;

namespace PvPArenas.Common.Game.LoadoutSelector;

/// <summary>
/// Shows the selected arena loadout below the scoreline during the freeze countdown.
/// Supports any number of named loadout options configured by the selected fight preset.
/// </summary>
internal static class LoadoutPreviewDrawer
{
    private readonly record struct SlotEntry(
        Item Item,
        bool Equip,
        int? HotbarNumber,
        SandboxSlot? Sandbox = null);

    // Equipment slot order, matching how Rebuild adds them. The null entry is the
    // empty gap between the accessories and the hook/mount column.
    private static readonly SandboxSlotKind?[] EquipKinds =
    [
        SandboxSlotKind.Head, SandboxSlotKind.Body, SandboxSlotKind.Legs,
        SandboxSlotKind.Accessory1, SandboxSlotKind.Accessory2, SandboxSlotKind.Accessory3,
        SandboxSlotKind.Accessory4, SandboxSlotKind.Accessory5,
        null,
        SandboxSlotKind.GrapplingHook, SandboxSlotKind.Mount
    ];

    private const int EquipmentSlotKeyOffset = 1000;

    private static BossFightPreset currentPreset;

    private static LoadoutUISettings drawSettings;
    private static float Opacity => alpha * Math.Clamp(drawSettings.Opacity, 0f, 1f);

    private static Texture2D PanelBackground =>
        Main.Assets.Request<Texture2D>(
            "Images/UI/PanelBackground").Value;

    private static Texture2D PanelBorder =>
        Main.Assets.Request<Texture2D>(
            "Images/UI/PanelBorder").Value;

    private static readonly RasterizerState ClipRasterizer = new() { CullMode = CullMode.None, ScissorTestEnable = true };
    private static readonly List<SlotEntry> inventorySlots = [];
    private static readonly List<SlotEntry> equipmentSlots = [];

    private static Player previewPlayer;

    private static int cachedPresetIndex = -1;
    private static int cachedDisplayLoadoutIndex = -1;
    private static int cachedMaxHealth = 100;
    private static int cachedMaxMana = 20;
    private static int layoutRows;
    private static bool cacheValid;

    private static int localLoadoutIndex;
    private static int hoveredLoadoutIndex = -1;

    private static int previewJumpTicks;
    private static bool previewJumpWasDown;

    private static float openingAge, alpha;
    private static float[] hoverAmounts = [], selectionAmounts = [];
    private static readonly float[] slotHoverAmounts = new float[LoadoutSlotLayout.SlotCount + EquipKinds.Length];
    private static int hoveredSlot = -1;

    private static string Label(string key) => Language.GetTextValue("Mods.PvPArenas.LoadoutSelector." + key);

    internal static void Update(float seconds)
    {
        LoadoutUISettings ui = LoadoutUILayout.Create();
        RoundManager manager = ModContent.GetInstance<RoundManager>();
        if (!manager.TryGetSelectedPreset(out BossFightPreset preset)) return;
        int count = preset.Loadouts?.Count ?? 0;
        if (hoverAmounts.Length != count)
        {
            hoverAmounts = new float[count];
            selectionAmounts = new float[count];
            layoutRows = 0;
        }

        openingAge += Math.Max(0f, seconds);
        alpha = ArenaUIStyle.Ease(Math.Clamp(openingAge / Math.Max(.001f, ui.OpeningDurationSeconds), 0f, 1f));
        float blend = 1f - MathF.Exp(-Math.Max(0f, ui.HoverAnimationSpeed) * Math.Max(0f, seconds));
        int selected = NormalizeLoadoutIndex(preset, Main.LocalPlayer.GetModPlayer<ArenaPlayer>().SelectedLoadoutIndex);
        for (int i = 0; i < count; i++)
        {
            hoverAmounts[i] = MathHelper.Lerp(hoverAmounts[i], hoveredLoadoutIndex == i ? 1f : 0f, blend);
            selectionAmounts[i] = MathHelper.Lerp(selectionAmounts[i], selected == i ? 1f : 0f, blend);
        }
        for (int i = 0; i < slotHoverAmounts.Length; i++)
            slotHoverAmounts[i] = MathHelper.Lerp(slotHoverAmounts[i], hoveredSlot == i ? 1f : 0f, blend);
    }

    internal static void Reset()
    {
        SandboxLoadoutEditor.Close();
        currentPreset = null;
        cachedPresetIndex = cachedDisplayLoadoutIndex = hoveredLoadoutIndex = hoveredSlot = -1;
        cacheValid = false;
        previewPlayer = null;
        layoutRows = 0;
        previewJumpTicks = 0;
        previewJumpWasDown = false;
        openingAge = alpha = 0f;
        Array.Clear(hoverAmounts);
        Array.Clear(selectionAmounts);
        Array.Clear(slotHoverAmounts);
    }

    private static int DisplayLoadoutIndex =>
        hoveredLoadoutIndex >= 0
            ? hoveredLoadoutIndex
            : localLoadoutIndex;

    private static int GetGridRowCount(BossFightPreset preset)
    {
        // Every choice uses the same geometry, so previews cannot move their own hitboxes.
        if (layoutRows > 0) return layoutRows;
        int[] counts = new int[preset.Loadouts?.Count ?? 0];
        for (int i = 0; i < counts.Length; i++)
        {
            Loadout loadout = ArenaPlayer.ResolveLoadout(preset, i);
            counts[i] = loadout.Inventory?.Count ?? 0;
        }
        return layoutRows = MaximumGridRows(counts, preset.IsSandbox());
    }

    public static void Draw()
    {
        drawSettings = LoadoutUILayout.Create();
        int top = drawSettings.PanelTop;
        RoundManager manager =
            ModContent.GetInstance<RoundManager>();

        if (!manager.TryGetSelectedPreset(
                out BossFightPreset preset))
        {
            return;
        }
        currentPreset = preset;

        int optionCount = preset.Loadouts?.Count ?? 0;
        bool showSelector = optionCount > 1;

        Point mouse = PlayerInput.IgnoreMouseInterface || alpha < drawSettings.InputEnableOpacity
            ? new Point(int.MinValue, int.MinValue)
            : new Point(Main.mouseX, Main.mouseY);
        hoveredSlot = -1;

        // While picking an item, the whole loadout body is replaced by the picker.
        if (preset.IsSandbox() && SandboxLoadoutEditor.IsOpen)
        {
            SandboxLoadoutEditor.DrawOverlay(top, alpha);
            return;
        }

        ArenaPlayer localArenaPlayer =
            Main.LocalPlayer.GetModPlayer<ArenaPlayer>();

        int playerSelectedIndex =
            NormalizeLoadoutIndex(
                preset,
                localArenaPlayer.SelectedLoadoutIndex);

        if (manager.SelectedPresetIndex != cachedPresetIndex)
        {
            layoutRows = 0;

            localLoadoutIndex = playerSelectedIndex;
            hoveredLoadoutIndex = -1;
            InvalidateCache();
        }
        else if (hoveredLoadoutIndex < 0 && localLoadoutIndex != playerSelectedIndex)
        {
            // Handles resets between consecutive rounds that happen to use
            // the same preset index.
            localLoadoutIndex = playerSelectedIndex;
            InvalidateCache();
        }

        if (!showSelector)
            hoveredLoadoutIndex = -1;

        int gridRows = GetGridRowCount(preset);
        // InterfaceScaleType.UI supplies logical screen/mouse coordinates, as it does for voting.
        LoadoutPreviewLayout layout = Calculate(new LoadoutPreviewArgs(
            Width: Main.screenWidth, Height: Main.screenHeight, Top: top, OptionCount: optionCount,
            InventoryRows: gridRows, EquipmentColumns: (EquipKinds.Length + EquipmentRows - 1) / EquipmentRows,
            Settings: drawSettings));
        float scale = layout.Scale;
        int S(float value) => Math.Max(1, (int)MathF.Round(value * scale));
        Rectangle panel = layout.Panel;

        hoveredLoadoutIndex = showSelector
            ? GetHoveredLoadoutIndex(layout, mouse)
            : -1;
        EnsureRebuilt(manager.SelectedPresetIndex, preset);

        // Reveal from the center with voting's timing; preserve clipping through the player renderer.
        GraphicsDevice device = Main.spriteBatch.GraphicsDevice;
        Rectangle oldClip = device.ScissorRectangle;
        RasterizerState oldRasterizer = device.RasterizerState;
        BlendState oldBlend = device.BlendState;
        SamplerState oldSampler = device.SamplerStates[0];
        int revealHeight = Math.Max(1, (int)MathF.Round(panel.Height * alpha));
        Rectangle clip = new(panel.X, panel.Center.Y - revealHeight / 2, panel.Width, revealHeight);
        Vector2 first = Vector2.Transform(clip.TopLeft(), Main.UIScaleMatrix);
        Vector2 last = Vector2.Transform(clip.BottomRight(), Main.UIScaleMatrix);
        Rectangle screenClip = new((int)first.X, (int)first.Y,
            Math.Max(1, (int)(last.X - first.X)), Math.Max(1, (int)(last.Y - first.Y)));
        Main.spriteBatch.End();
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp,
            DepthStencilState.None, ClipRasterizer, null, Main.UIScaleMatrix);
        device.ScissorRectangle = Rectangle.Intersect(oldRasterizer.ScissorTestEnable ? oldClip : device.Viewport.Bounds, screenClip);
        try
        {
            DrawPanel(
                panel,
                drawSettings.PanelFillColor,
                drawSettings.PanelBorderColor,
                S(drawSettings.PanelCornerRadius));

            string title = Label("Header");
            LoadoutTextPlacement titleText = LoadoutUIText.Centered(new(
                Font: FontAssets.DeathText.Value, Text: title, Area: layout.TitleBox,
                PreferredScale: drawSettings.LoadoutTitleTextScale * scale, Offset: Vector2.Zero,
                Padding: 2f * scale));
            Utils.DrawBorderStringBig(
                Main.spriteBatch,
                title,
                titleText.Position,
                drawSettings.TitleColor * Opacity,
                titleText.Scale);

            int seconds = Math.Max(0, (manager.RemainingTicks + 59) / 60);
            string timer = $"{seconds / 60}:{seconds % 60:00}";
            LoadoutTextPlacement timerText = LoadoutUIText.Centered(new(
                Font: FontAssets.MouseText.Value, Text: timer, Area: layout.TimerBox,
                PreferredScale: drawSettings.TimerTextScale * scale,
                Offset: new Vector2(0, drawSettings.TimerTextYOffset * scale), Padding: 2f * scale));
            Utils.DrawBorderString(Main.spriteBatch, timer, timerText.Position,
                drawSettings.TimerTextColor * Opacity, timerText.Scale);
            Rectangle track = layout.ProgressBox;
            float progress = Math.Clamp(manager.RemainingTicks /
                (60f * Math.Max(1, ModContent.GetInstance<ServerConfig>().FreezeCountdownSeconds)), 0f, 1f);
            Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, track,
                Color.Black * (Math.Clamp(drawSettings.ProgressTrackOpacity, 0f, 1f) * Opacity));
            Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value,
                new Rectangle(track.X, track.Y, (int)(track.Width * progress), track.Height), drawSettings.ProgressColor * Opacity);

            if (!PlayerInput.IgnoreMouseInterface && panel.Contains(Main.mouseX, Main.mouseY))
                Main.LocalPlayer.mouseInterface = true;

            if (showSelector)
            {
                DrawLoadoutCards(
                    preset,
                    layout,
                    S,
                    scale,
                    mouse);
            }

            DrawPreviewBox(
                layout,
                mouse);

            DrawLoadoutStats(layout, S);

            DrawInventorySlots(
                layout,
                mouse);

            if (equipmentSlots.Count > 0)
            {
                DrawEquipmentSlots(
                    layout,
                    mouse);
            }
        }
        finally
        {
            Main.spriteBatch.End();
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, oldBlend, oldSampler,
                DepthStencilState.None, oldRasterizer, null, Main.UIScaleMatrix);
            device.ScissorRectangle = oldClip;
        }
    }

    private static int GetHoveredLoadoutIndex(
        LoadoutPreviewLayout layout,
        Point mouse)
    {
        for (int i = 0; i < layout.OptionCount; i++)
        {
            if (Card(layout, i).Bounds.Contains(mouse))
                return i;
        }

        return -1;
    }

    private static void DrawLoadoutCards(
        BossFightPreset preset,
        LoadoutPreviewLayout layout,
        Func<float, int> S,
        float scale,
        Point mouse)
    {
        int optionCount =
            preset.Loadouts?.Count ?? 0;

        for (int i = 0; i < optionCount; i++)
        {
            ArenaLoadoutOption option =
                preset.Loadouts[i];

            LoadoutPreviewCard regions = Card(layout, i);
            Rectangle card = regions.Bounds;

            bool hovered =
                hoveredLoadoutIndex == i;

            bool selected =
                localLoadoutIndex == i;

            float hover = i < hoverAmounts.Length ? hoverAmounts[i] : 0f;
            float selection = i < selectionAmounts.Length ? selectionAmounts[i] : selected ? 1f : 0f;
            DrawPanel(card, LoadoutUILayout.ChoiceFill(drawSettings, hover, selection), Color.Transparent,
                S(drawSettings.LoadoutPanelOptionCornerRadius));
            Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value,
                regions.SelectionLine,
                drawSettings.AccentColor * (selection * Opacity));

            if (hovered)
            {
                Main.LocalPlayer.mouseInterface = true;

                if (Main.mouseLeft &&
                    Main.mouseLeftRelease &&
                    !selected)
                {
                    Main.mouseLeftRelease = false;
                    SoundEngine.PlaySound(SoundID.MenuTick);

                    localLoadoutIndex = i;

                    ArenaPlayer.RequestLoadoutSelect(i);

                    InvalidateCache();
                }
            }

            Rectangle iconBox = regions.IconBox;

            Item icon =
                MakeItem(GetLoadoutIconType(option));

            if (!icon.IsAir)
            {
                ItemSlot.DrawItemIcon(
                    icon,
                    31,
                    Main.spriteBatch,
                    iconBox.Center.ToVector2(),
                    Math.Max(0, drawSettings.LoadoutPanelOptionIconScale) * scale,
                    Math.Max(1, iconBox.Width - S(drawSettings.LoadoutPanelOptionIconPadding)),
                    Color.White * (Opacity * Math.Clamp(drawSettings.ItemOpacity, 0f, 1f)));
            }

            string name =
                string.IsNullOrWhiteSpace(option?.Name)
                    ? Language.GetTextValue("Mods.PvPArenas.LoadoutSelector.UnnamedLoadout", i + 1)
                    : option.Name;

            Color nameColor = Color.Lerp(drawSettings.TextColor, drawSettings.AccentColor, selection);

            Text(
                CardName(name),
                new Vector2(
                    regions.NameBox.Center.X,
                    regions.NameBox.Y),
                nameColor,
                drawSettings.LoadoutPanelOptionTextScale * scale,
                regions.NameBox.Width);
        }
    }

    private static int GetLoadoutIconType(
        ArenaLoadoutOption option)
    {
        int helmet =
            option?.Loadout
                ?.Armor
                ?.Head
                ?.Type
            ?? ItemID.None;

        if (helmet > ItemID.None)
            return helmet;

        List<LoadoutItem> inventory =
            option?.Loadout?.Inventory;

        if (inventory == null)
            return ItemID.None;

        foreach (LoadoutItem entry in inventory)
        {
            int type =
                entry?.Item?.Type
                ?? ItemID.None;

            if (type > ItemID.None)
                return type;
        }

        return ItemID.None;
    }

    private static int NormalizeLoadoutIndex(
        BossFightPreset preset,
        int index)
    {
        int count =
            preset?.Loadouts?.Count ?? 0;

        if (count == 0)
            return 0;

        return index >= 0 && index < count
            ? index
            : 0;
    }

    private static void EnsureRebuilt(
        int presetIndex,
        BossFightPreset preset)
    {
        int displayIndex =
            NormalizeLoadoutIndex(
                preset,
                DisplayLoadoutIndex);

        if (cacheValid &&
            presetIndex == cachedPresetIndex &&
            displayIndex ==
                cachedDisplayLoadoutIndex &&
            previewPlayer != null)
        {
            return;
        }

        Rebuild(
            presetIndex,
            preset,
            displayIndex);
    }

    private static void Rebuild(
        int presetIndex,
        BossFightPreset preset,
        int loadoutIndex)
    {
        cachedPresetIndex = presetIndex;
        cachedDisplayLoadoutIndex = loadoutIndex;
        cacheValid = true;

        Loadout baseLoadout =
    ArenaPlayer.ResolveBaseLoadout(
        preset,
        loadoutIndex);

        Loadout loadout = preset.IsSandbox() ? baseLoadout
            : LocalLoadoutPositions.Apply(preset, loadoutIndex, baseLoadout);

        equipmentSlots.Clear();
        inventorySlots.Clear();

        AddEquipment(loadout.Armor?.Head);
        AddEquipment(loadout.Armor?.Body);
        AddEquipment(loadout.Armor?.Legs);

        AddEquipment(
            loadout.Accessories?.Accessory1);

        AddEquipment(
            loadout.Accessories?.Accessory2);

        AddEquipment(
            loadout.Accessories?.Accessory3);

        AddEquipment(
            loadout.Accessories?.Accessory4);

        AddEquipment(
            loadout.Accessories?.Accessory5);

        // Empty gap slot between the accessories and the hook/mount column.
        AddEquipmentSpacer();

        AddEquipment(
            loadout.Equipment?.GrapplingHook);

        AddEquipment(
            loadout.Equipment?.Mount);

        int inventoryCount =
            Math.Min(
                loadout.Inventory?.Count ?? 0,
                LoadoutSlotLayout.SlotCount);

        // Sandbox always shows the full editable grid; other presets grow to fit.
        int minimumSlots = preset.IsSandbox()
            ? LocalSandboxLoadouts.InventorySlots
            : 10;

        int displayedInventoryCount =
            Math.Max(
                minimumSlots,
                inventoryCount);

        for (int i = 0;
             i < displayedInventoryCount;
             i++)
        {
            LoadoutItem entry =
                i < inventoryCount
                    ? loadout.Inventory[i]
                    : null;

            inventorySlots.Add(
                new SlotEntry(
                    MakeItem(
                        entry?.Item?.Type
                            ?? ItemID.None,
                        entry?.Stack ?? 1),
                    false,
                    i < 10
                        ? i == 9
                            ? 10
                            : i + 1
                        : null,
                    preset.IsSandbox()
                        ? new SandboxSlot(SandboxSlotKind.Inventory, i)
                        : null));
        }

        if (preset.IsSandbox())
        {
            for (int i = 0; i < equipmentSlots.Count && i < EquipKinds.Length; i++)
                if (EquipKinds[i] is SandboxSlotKind kind)
                    equipmentSlots[i] = equipmentSlots[i] with
                    {
                        Sandbox = new SandboxSlot(kind)
                    };
        }

        cachedMaxHealth = preset.MaxHealth;
        cachedMaxMana = preset.MaxMana;

        previewPlayer =
            BuildPreviewPlayer(loadout);
    }

    private static void AddEquipment(
        ItemDefinition definition)
    {
        equipmentSlots.Add(
            new SlotEntry(
                MakeItem(
                    definition?.Type
                    ?? ItemID.None),
                true,
                null));
    }

    private static void AddEquipmentSpacer()
    {
        equipmentSlots.Add(
            new SlotEntry(
                new Item(),
                true,
                null));
    }

    private static Item MakeItem(
        int type,
        int stack = 1)
    {
        Item item = new();

        if (type > ItemID.None)
        {
            item.SetDefaults(type);
            item.stack = Math.Max(1, stack);
        }

        return item;
    }

    #region Preview player
    private static Player BuildPreviewPlayer(
        Loadout loadout)
    {
        // A bare "new Player()" is not renderable: the draw path depends on
        // deep state (dye vars, cloned appearance, frame setup) that only a
        // fully-initialized player has. Cloning the local player like vanilla's
        // UICharacter does gives us that for free; we then strip its gear and
        // apply the loadout so only the loadout armor is visible.
        Player preview =
            (Main.LocalPlayer ?? new Player()).SerializedClone();

        preview.active = true;
        preview.dead = false;
        preview.ghost = false;
        preview.direction = 1;
        preview.isDisplayDollOrInanimate = true;
        preview.selectedItem = 0;

        for (int i = 0; i < preview.inventory.Length; i++)
            preview.inventory[i] = new Item();

        for (int i = 0; i < preview.armor.Length; i++)
            preview.armor[i] = new Item();

        IReadOnlyList<LoadoutItem> armor = LoadoutSlotLayout.ArmorItems(loadout);
        for (int i = 0; i < armor.Count && i < preview.armor.Length; i++)
            preview.armor[i] = MakeItem(armor[i]?.Item?.Type ?? ItemID.None, armor[i]?.Stack ?? 1);

        using (new Main.CurrentPlayerOverride(preview))
        {
            preview.ResetEffects();
            preview.ResetVisibleAccessories();
            preview.UpdateDyes();
            preview.DisplayDollUpdate();
            preview.PlayerFrame();
        }

        return preview;
    }

    private static void DrawPreviewBox(
        LoadoutPreviewLayout layout,
        Point mouse)
    {
        Rectangle box = layout.PortraitBox;
        SpriteBatch spriteBatch =
            Main.spriteBatch;

        DrawPanel(box, drawSettings.CardFillColor, Color.Transparent,
            Math.Max(1, (int)MathF.Round(drawSettings.PlayerPreviewCornerRadius * layout.Scale)));

        if (previewPlayer == null)
            return;

        bool hovered = box.Contains(mouse);

        if (hovered)
            Main.LocalPlayer.mouseInterface = true;

        float drawScale = PortraitScale(layout);
        Vector2 drawPosition =
            Main.screenPosition
            + new Vector2(
                box.Center.X
                    - previewPlayer.width * drawScale / 2f,
                box.Bottom
                    - previewPlayer.height * drawScale - drawSettings.PlayerPreviewBottomPadding * layout.Scale);

        previewPlayer.velocity = Vector2.Zero;
        previewPlayer.active = true;
        previewPlayer.dead = false;
        previewPlayer.ghost = false;
        previewPlayer.isDisplayDollOrInanimate = true;
        previewPlayer.immuneAlpha = (int)MathF.Round((1f - Opacity) * 255f);

        float jumpOffset =
            ApplyPreviewAnimation(hovered);

        drawPosition.Y -= jumpOffset * drawScale;
        previewPlayer.position = drawPosition;

        bool wasMenu = Main.gameMenu;
        GraphicsDevice device = spriteBatch.GraphicsDevice;
        Rectangle oldClip = device.ScissorRectangle;
        RasterizerState oldRasterizer = device.RasterizerState;
        BlendState oldBlend = device.BlendState;
        SamplerState oldSampler = device.SamplerStates[0];
        Vector2 first = Vector2.Transform(box.TopLeft(), Main.UIScaleMatrix);
        Vector2 last = Vector2.Transform(box.BottomRight(), Main.UIScaleMatrix);
        Rectangle portraitClip = new((int)first.X, (int)first.Y,
            Math.Max(1, (int)(last.X - first.X)), Math.Max(1, (int)(last.Y - first.Y)));
        spriteBatch.End();
        spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, ClipRasterizer, null, Main.UIScaleMatrix);
        device.ScissorRectangle = Rectangle.Intersect(oldClip, portraitClip);

        try
        {
            // gameMenu forces full-bright lighting (the panel may sit over
            // unlit tiles); the player override makes the renderer treat the
            // preview as the active player, both required for it to appear.
            Main.gameMenu = true;

            using (new Main.CurrentPlayerOverride(previewPlayer))
            {
                previewPlayer.PlayerFrame();

                Main.PlayerRenderer.DrawPlayer(
                    Main.Camera,
                    previewPlayer,
                    drawPosition,
                    0f,
                    Vector2.Zero,
                    0f,
                    drawScale);
            }
        }
        finally
        {
            Main.gameMenu = wasMenu;

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, oldBlend, oldSampler,
                DepthStencilState.None, oldRasterizer, null, Main.UIScaleMatrix);
            device.ScissorRectangle = oldClip;
        }
    }

    private static float ApplyPreviewAnimation(
        bool hovered)
    {
        bool jumpDown =
            PlayerInput.Triggers.Current.Jump;

        bool jumpPressed =
            jumpDown &&
            !previewJumpWasDown;

        previewJumpWasDown = jumpDown;

        previewPlayer.SetCompositeArmFront(
            false,
            Player.CompositeArmStretchAmount.Full,
            0f);

        previewPlayer.SetCompositeArmBack(
            false,
            Player.CompositeArmStretchAmount.Full,
            0f);

        previewPlayer.direction = 1;

        if (jumpPressed &&
            previewJumpTicks <= 0)
        {
            previewJumpTicks =
                Math.Max(1, drawSettings.PlayerJumpDuration);
        }

        if (previewJumpTicks > 0)
        {
            previewJumpTicks = Math.Min(previewJumpTicks, Math.Max(1, drawSettings.PlayerJumpDuration));
            float progress =
                1f
                - previewJumpTicks
                / Math.Max(1f, drawSettings.PlayerJumpDuration);

            previewJumpTicks--;

            SetPreviewBodyFrame(5);

            return MathF.Sin(
                progress * MathF.PI)
                * drawSettings.PlayerJumpHeight;
        }

        if (!hovered)
        {
            SetPreviewBodyFrame(0);
            return 0f;
        }

        int frame =
            7
            + (int)(
                Main.GameUpdateCount
                / (ulong)Math.Max(1, drawSettings.PlayerWalkFrameTicks)
                % 13UL);

        SetPreviewBodyFrame(frame);
        previewPlayer.WingFrame(false);

        return 0f;
    }

    private static void SetPreviewBodyFrame(
        int frame)
    {
        previewPlayer.bodyFrame.Y =
            frame
            * previewPlayer.bodyFrame.Height;

        previewPlayer.legFrame.Y =
            frame
            * previewPlayer.legFrame.Height;

        previewPlayer.headFrame.Y = 0;
    }
    #endregion

    private static void DrawInventorySlots(
        LoadoutPreviewLayout layout,
        Point mouse)
    {
        for (int i = 0;
             i < inventorySlots.Count;
             i++)
        {
            Rectangle cell = InventorySlot(layout, i);

            DrawSlot(
                cell,
                inventorySlots[i],
                layout.Scale,
                mouse,
                i);
        }
    }

    private static void DrawEquipmentSlots(
        LoadoutPreviewLayout layout,
        Point mouse)
    {
        for (int i = 0;
             i < equipmentSlots.Count;
             i++)
        {
            Rectangle cell = EquipmentSlot(layout, i);

            DrawSlot(
                cell,
                equipmentSlots[i],
                layout.Scale,
                mouse,
                EquipmentSlotKeyOffset + i);
        }
    }

    private static void DrawSlot(
        Rectangle cell,
        SlotEntry entry,
        float uiScale,
        Point mouse,
        int slotKey)
    {
        int hoverKey = slotKey >= EquipmentSlotKeyOffset ? LoadoutSlotLayout.SlotCount + slotKey - EquipmentSlotKeyOffset : slotKey;
        bool equipment = entry.Equip;
        int corner = equipment ? drawSettings.EquipmentSlotCornerRadius : drawSettings.InventorySlotCornerRadius;
        if (cell.Contains(mouse)) hoveredSlot = hoverKey;
        DrawPanel(cell, LoadoutUILayout.ChoiceFill(drawSettings, slotHoverAmounts[hoverKey], 0f), Color.Transparent,
            Math.Max(1, (int)MathF.Round(corner * uiScale)));

        if (!entry.Item.IsAir)
        {
            float itemScale = equipment ? drawSettings.EquipmentItemScale : drawSettings.InventoryItemScale;
            int padding = equipment ? drawSettings.EquipmentItemPadding : drawSettings.InventoryItemPadding;
            ItemSlot.DrawItemIcon(entry.Item, 31, Main.spriteBatch,
                cell.Center.ToVector2(), uiScale * Math.Max(0f, itemScale),
                Math.Max(1f, cell.Width - Math.Max(0, padding) * uiScale),
                Color.White * (Opacity * Math.Clamp(drawSettings.ItemOpacity, 0f, 1f)));
        }

        if (entry.HotbarNumber.HasValue)
        {
            string number =
                entry.HotbarNumber.Value == 10
                    ? "0"
                    : entry.HotbarNumber.Value
                        .ToString();

            Rectangle area = SlotLabelBox(cell, false, drawSettings, uiScale);
            float textScale = TextScale(FontAssets.MouseText.Value.MeasureString(number), area,
                drawSettings.InventoryHotbarTextScale * uiScale);
            Utils.DrawBorderString(
                Main.spriteBatch,
                number,
                new Vector2(
                    area.X,
                    area.Y),
                drawSettings.TextColor * Opacity,
                textScale);
        }

        if (entry.Item.stack > 1)
        {
            string stack = entry.Item.stack.ToString();
            Rectangle area = SlotLabelBox(cell, true, drawSettings, uiScale);
            Vector2 measured = FontAssets.MouseText.Value.MeasureString(stack);
            float textScale = TextScale(measured, area, drawSettings.InventoryStackTextScale * uiScale);
            Utils.DrawBorderString(
                Main.spriteBatch,
                stack,
                new Vector2(
                    area.X,
                    area.Bottom - measured.Y * textScale),
                drawSettings.TextColor * Opacity,
                textScale);
        }

        if (!entry.Item.IsAir &&
            cell.Contains(mouse))
        {
            Main.LocalPlayer.mouseInterface = true;
            Main.HoverItem = entry.Item.Clone();
            Main.hoverItemName = entry.Item.Name;
        }

        if (currentPreset?.IsSandbox() == true && entry.Sandbox.HasValue)
        {
            SandboxLoadoutEditor.HandleSlotClick(
                currentPreset,
                cachedDisplayLoadoutIndex,
                entry.Sandbox.Value,
                cell,
                mouse);

            return;
        }

    }

    // The stats strip has its own region below the animated portrait.
    private static void DrawLoadoutStats(
        LoadoutPreviewLayout layout,
        Func<float, int> S)
    {
        DrawPanel(layout.StatsBox, drawSettings.CardFillColor, Color.Transparent, S(drawSettings.HealthManaCornerRadius));

        DrawStat(
            TextureAssets.Heart.Value,
            new LoadoutStatTextArgs(FontAssets.MouseText.Value, cachedMaxHealth.ToString(), Stat(layout, 0),
                TextureAssets.Heart.Value.Size(), drawSettings.HealthIconSize, drawSettings.HealthTextScale,
                layout.Scale, drawSettings));

        DrawStat(
            TextureAssets.Mana.Value,
            new LoadoutStatTextArgs(FontAssets.MouseText.Value, cachedMaxMana.ToString(), Stat(layout, 1),
                TextureAssets.Mana.Value.Size(), drawSettings.ManaIconSize, drawSettings.ManaTextScale,
                layout.Scale, drawSettings));
    }

    private static void DrawStat(
        Texture2D icon,
        LoadoutStatTextArgs args)
    {
        LoadoutStatPlacement placement = LoadoutUIText.Stat(args);

        Main.spriteBatch.Draw(
            icon,
            placement.IconCenter,
            null,
            Color.White * Opacity,
            0f,
            icon.Size() / 2f,
            placement.IconScale,
            SpriteEffects.None,
            0f);

        Utils.DrawBorderString(
            Main.spriteBatch,
            args.Text,
            placement.TextPosition,
            drawSettings.TextColor * Opacity,
            placement.TextScale);
    }

    private static void DrawPanel(
        Rectangle rectangle,
        Color fill,
        Color edge,
        int corner)
    {
        Utils.DrawSplicedPanel(
            Main.spriteBatch,
            PanelBackground,
            rectangle.X,
            rectangle.Y,
            rectangle.Width,
            rectangle.Height,
            corner,
            corner,
            corner,
            corner,
            fill * Opacity);

        Utils.DrawSplicedPanel(
            Main.spriteBatch,
            PanelBorder,
            rectangle.X,
            rectangle.Y,
            rectangle.Width,
            rectangle.Height,
            corner,
            corner,
            corner,
            corner,
            edge * Opacity);
    }

    private static void Text(
        string value,
        Vector2 position,
        Color color,
        float scale,
        float maxWidth,
        float anchor = .5f)
    {
        float width =
            FontAssets.MouseText.Value
                .MeasureString(value).X
            * scale;

        if (width > maxWidth)
            scale *= maxWidth / width;

        Utils.DrawBorderString(
            Main.spriteBatch,
            value,
            position,
            color * Opacity,
            scale,
            anchor);
    }

    private static void InvalidateCache()
    {
        cacheValid = false;
    }

    /// <summary>Forces the next draw to rebuild cached slots (used after a sandbox slot changes).</summary>
    internal static void Invalidate()
    {
        layoutRows = 0;
        InvalidateCache();
    }
}
