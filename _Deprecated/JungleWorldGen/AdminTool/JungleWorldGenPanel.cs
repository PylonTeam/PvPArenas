#if DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;
using PvPArenas.Common.AdminTools.GameManager;
using PvPArenas.Common.AdminTools.UI;
using PvPArenas.Common.Generation;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.Localization;
using Terraria.UI;

namespace PvPArenas.Common.AdminTools.JungleWorldGen;

internal sealed class JungleWorldGenPanel : UIDraggablePanel
{
    private readonly JungleWorldGenDraft draft = new();
    private readonly JungleWorldGenRecipeStore recipes = new();
    private readonly UIList passList;
    private readonly InputField searchInput;
    private readonly InputField seedInput;
    private readonly InputField recipeInput;
    private readonly List<(UIElement Element, float Top, float Height)> verticalLayout = [];
    private float layoutScale = 1f;
    private bool initialized;
    private bool dirty;
    private bool syncingInput;
    private bool ownsInputBlock;
    private bool reloadRequested;
    private bool awaitingApply;
    private int observedVersion = -1;
    private int applyVersion;
    private string filter = "";
    private string localMessage = "";
    private bool localError;

    private static float AvailableWidth => Math.Max(280f, Main.screenWidth / Math.Max(.5f, Main.UIScale) - 20f);
    private static float AvailableHeight => Math.Max(260f, Main.screenHeight / Math.Max(.5f, Main.UIScale) - 20f);
    protected override float MinResizeW => Math.Min(560f, AvailableWidth);
    protected override float MinResizeH => Math.Min(620f, AvailableHeight);
    protected override float MaxResizeW => Math.Min(1000f, AvailableWidth);
    protected override float MaxResizeH => Math.Min(1000f, AvailableHeight);
    private static JungleWorldGenStatus Status => JungleWorldGenNetHandler.ClientStatus;
    private static bool Networked => Main.netMode == NetmodeID.MultiplayerClient;
    private bool CanEdit => initialized && JungleWorldGenUISystem.CanUse && Networked && !JungleWorldGenNetHandler.Pending && !Status.Busy;
    private bool ValidSeed => draft.TryGetRecipe(out _, out _, out _);

    internal JungleWorldGenPanel() : base("Underground Jungle · DEBUG")
    {
        Width.Set(Math.Min(680f, AvailableWidth), 0f);
        Height.Set(Math.Min(700f, AvailableHeight), 0f);
        HAlign = .5f;
        VAlign = .5f;
        Content.SetPadding(10f);

        searchInput = AddInput("Search passes, categories, descriptions", 0f, 0f, 0f, 0f, 32f, text =>
        {
            filter = text;
            RebuildList();
        }, () => CanEdit);
        Content.Append(Line(() => $"{draft.Passes.Count}/{VanillaJungleProfile.Catalog.Count} passes · vanilla order · {VanillaJungleProfile.Catalog.Count(pass => pass.Required)} required" + (dirty ? " · unapplied edits" : ""), 37f, 21f, () => dirty ? new Color(255, 218, 125) : Color.LightSteelBlue));

        AddButtons(62f, 30f,
            ("All", "Enable every relevant vanilla pass.", () => CanEdit, () => Edit(() => draft.SetPreset("Vanilla"))),
            ("Minimum", "Keep only the required foundation, Jungle, walls, and liquid passes.", () => CanEdit, () => Edit(() => draft.SetPreset("Minimum"))),
            ("Invert", "Invert optional passes. Disabled prerequisites also disable their dependents.", () => CanEdit, () => Edit(draft.InvertOptional)),
            ("Reload server", "Discard local edits and request the server's current settings.", () => Networked && !JungleWorldGenNetHandler.Pending, ReloadServer));
        AddButtons(98f, 30f,
            ("Vanilla", "Default recipe: all supported underground Jungle passes.", () => CanEdit, () => Edit(() => draft.SetPreset("Vanilla"))),
            ("Caves only", "Required Jungle plus cave shaping, cave walls, and cleanup; omit optional structures and decoration.", () => CanEdit, () => Edit(() => draft.SetPreset("Caves"))),
            ("No hives", "Default recipe without Hives or Larva.", () => CanEdit, () => Edit(() => draft.SetPreset("No hives"))),
            ("Less wet detail", "Default recipe without Wet Jungle, Webs And Honey, or Water Plants. This changes wet decoration and honey conversion, not the overall amount of underground water.", () => CanEdit, () => Edit(() => draft.SetPreset("Less wet detail"))));

        UIPanel listPanel = new()
        {
            Top = { Pixels = 136f },
            Width = { Percent = 1f },
            Height = { Pixels = -380f, Percent = 1f },
            BackgroundColor = new Color(16, 22, 48),
            BorderColor = new Color(65, 84, 140)
        };
        listPanel.SetPadding(6f);
        passList = new UIList
        {
            Width = { Pixels = -23f, Percent = 1f },
            Height = { Percent = 1f },
            ListPadding = 3f,
            ManualSortMethod = _ => { }
        };
        UIScrollbar scrollbar = new() { HAlign = 1f, Width = { Pixels = 20f }, Height = { Percent = 1f } };
        passList.SetScrollbar(scrollbar);
        listPanel.Append(passList);
        listPanel.Append(scrollbar);
        Content.Append(listPanel);

        Content.Append(Button(() => draft.UseFixedSeed ? "Fixed seed" : "Random seed", "Switch between a fresh random seed and the signed integer entered here.", () => CanEdit,
            () => Edit(() => draft.UseFixedSeed = !draft.UseFixedSeed), 0f, 0f, -232f, 1f, 128f, 0f, 32f));
        seedInput = AddInput("Signed 32-bit seed", 136f, -232f, 1f, -264f, 32f, text =>
        {
            if (syncingInput) return;
            draft.SeedText = text;
            MarkDirty();
        }, () => CanEdit && draft.UseFixedSeed);
        SyncSeedInput();
        Content.Append(Button(() => "Use last seed", "Copy the most recently generated seed into this draft.", () => CanEdit && Status.LastSeed.HasValue, () => Edit(() =>
        {
            draft.UseFixedSeed = true;
            draft.SeedText = Status.LastSeed.Value.ToString(CultureInfo.InvariantCulture);
            SyncSeedInput();
        }), -120f, 1f, -232f, 1f, 120f, 0f, 32f));

        recipeInput = AddInput("Local recipe name", 0f, -192f, 1f, -264f, 32f, _ => { }, () => CanEdit);
        Content.Append(Button(() => "Browse", "Cycle saved local recipe names; right-click the name to clear it.", () => CanEdit && recipes.Recipes.Count > 0, CycleRecipe,
            -256f, 1f, -192f, 1f, 80f, 0f, 32f));
        Content.Append(Button(() => "Load", "Load this named local recipe into the draft. Apply or Generate sends it to the server.", () => CanEdit && recipes.Find(recipeInput.Text) != null, LoadRecipe,
            -168f, 1f, -192f, 1f, 80f, 0f, 32f));
        Content.Append(Button(() => "Save", "Save or replace this named recipe on this computer, including its seed mode.", () => CanEdit && ValidSeed && !string.IsNullOrWhiteSpace(recipeInput.Text), SaveRecipe,
            -80f, 1f, -192f, 1f, 80f, 0f, 32f));

        AddButtons(-152f, 36f, 1f,
            ("Apply settings", "Apply this recipe on the server without rebuilding the current map.", () => CanEdit && ValidSeed, () => SendRecipe(false)),
            ("Generate preview", "Apply this recipe and rebuild a protected, untimed Jungle preview. Players are staged safely during generation.", () => CanEdit && ValidSeed && Status.CanGenerate, () => SendRecipe(true)),
            ("Print PNG + recipe", "Export the current generated preview on the server. Prints the actual generated seed and passes, including a companion JSON recipe.", () => initialized && Networked && !JungleWorldGenNetHandler.Pending && !Status.Busy && Status.CanPrint, Print));

        Content.Append(new ProgressLine { Top = { Pixels = -108f, Percent = 1f }, Width = { Percent = 1f }, Height = { Pixels = 24f } });
        Content.Append(Line(GetMessage, -78f, 26f, MessageColor, 1f));
        Content.Append(Line(() => initialized && !string.IsNullOrEmpty(Status.ExportPath) ? "Export: " + Status.ExportPath : "Generated maps and JSON recipes are saved on the server.", -44f, 32f, () => Color.LightSteelBlue, 1f));

        if (!recipes.Load(out string error)) Message(error, true);
        foreach (UIElement element in Content.Children) verticalLayout.Add((element, element.Top.Pixels, element.Height.Pixels));
        FitToScreen();
        RebuildList();
    }

    protected override void OnClosePanelLeftClick() => ModContent.GetInstance<JungleWorldGenUISystem>().Close();
    protected override void OnRefreshPanelLeftClick() => ReloadServer();

    internal void ObserveServerStatus()
    {
        if (!JungleWorldGenNetHandler.HasStatus) return;
        int version = JungleWorldGenNetHandler.StatusVersion;
        if (version == observedVersion) return;
        observedVersion = version;
        bool ownResponse = awaitingApply && !JungleWorldGenNetHandler.Pending && version != applyVersion;
        if (!initialized || reloadRequested || ownResponse && Status.Success || !dirty && !JungleWorldGenNetHandler.Pending)
        {
            draft.Load(Status.Passes, Status.FixedSeed);
            initialized = true;
            dirty = false;
            reloadRequested = false;
            SyncSeedInput();
        }
        if (ownResponse) awaitingApply = false;
    }

    internal void ResetDraft()
    {
        ReleaseInput();
        initialized = dirty = reloadRequested = awaitingApply = false;
        observedVersion = -1;
        draft.Load(VanillaJungleProfile.Default.Passes, null);
        SyncSeedInput();
        Message("", false);
    }

    internal void ReleaseInput()
    {
        searchInput?.Release();
        seedInput?.Release();
        recipeInput?.Release();
        if (ownsInputBlock)
        {
            Main.blockInput = false;
            ownsInputBlock = false;
        }
    }

    public override void Update(GameTime gameTime)
    {
        FitToScreen();
        base.Update(gameTime);
        if (!CanEdit || !Main.hasFocus) ReleaseInput();
        else
        {
            foreach (InputField input in Inputs())
                if (input.Writing && Main.mouseLeft && !input.IsMouseHovering) input.Release();
            if (!Inputs().Any(input => input.Writing) && ownsInputBlock)
            {
                Main.blockInput = false;
                ownsInputBlock = false;
            }
        }
        ObserveServerStatus();
    }

    private void FitToScreen()
    {
        bool changed = false;
        if (Width.Pixels > AvailableWidth) { Width.Set(AvailableWidth, 0f); changed = true; }
        if (Height.Pixels > AvailableHeight) { Height.Set(AvailableHeight, 0f); changed = true; }
        if (HAlign == 0f)
        {
            float left = MathHelper.Clamp(Left.Pixels, 0f, Math.Max(0f, AvailableWidth - Width.Pixels));
            if (left != Left.Pixels) { Left.Set(left, 0f); changed = true; }
        }
        if (VAlign == 0f)
        {
            float top = MathHelper.Clamp(Top.Pixels, 0f, Math.Max(0f, AvailableHeight - Height.Pixels));
            if (top != Top.Pixels) { Top.Set(top, 0f); changed = true; }
        }
        float scale = Math.Clamp((Height.Pixels - 60f) / 640f, .35f, 1f);
        if (Math.Abs(scale - layoutScale) > .001f)
        {
            layoutScale = scale;
            foreach (var entry in verticalLayout)
            {
                entry.Element.Top.Pixels = entry.Top * scale;
                entry.Element.Height.Pixels = entry.Height * scale;
            }
            changed = true;
        }
        if (changed) Recalculate();
    }

    private IEnumerable<InputField> Inputs()
    {
        if (searchInput != null) yield return searchInput;
        if (seedInput != null) yield return seedInput;
        if (recipeInput != null) yield return recipeInput;
    }

    private void Focus(InputField input)
    {
        foreach (InputField other in Inputs()) if (other != input) other.Release();
        if (!Main.blockInput) ownsInputBlock = true;
        Main.blockInput = true;
    }

    private void RebuildList()
    {
        if (passList == null) return;
        passList.Clear();
        int index = 0;
        foreach (VanillaJunglePass pass in VanillaJungleProfile.Catalog)
        {
            index++;
            string searchable = pass.Name + " " + JungleWorldGenPassInfo.GetCategory(pass.Name) + " " + JungleWorldGenPassInfo.GetDescription(pass.Name);
            if (filter.Length > 0 && !searchable.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            passList.Add(new PassRow(index, pass, draft, () => CanEdit, () => Edit(() => draft.SetPass(pass.Name, !draft.IsSelected(pass.Name)))));
        }
        if (passList.Count == 0) passList.Add(new UIText("No matching underground Jungle passes", .7f) { Width = { Percent = 1f }, Height = { Pixels = 32f } });
    }

    private void MarkDirty()
    {
        dirty = true;
        localMessage = "";
        localError = false;
    }

    private void Edit(Action action)
    {
        action();
        MarkDirty();
    }

    private void SyncSeedInput()
    {
        syncingInput = true;
        seedInput?.SetText(draft.SeedText);
        syncingInput = false;
    }

    private void ReloadServer()
    {
        if (!Networked || JungleWorldGenNetHandler.Pending) return;
        ReleaseInput();
        reloadRequested = true;
        localMessage = "";
        JungleWorldGenNetHandler.RequestStatus();
    }

    private void SendRecipe(bool generate)
    {
        ReleaseInput();
        if (!draft.TryGetRecipe(out IReadOnlyList<string> passes, out int? seed, out string error)) { Message(error, true); return; }
        awaitingApply = true;
        applyVersion = JungleWorldGenNetHandler.StatusVersion;
        localMessage = "";
        if (generate) JungleWorldGenNetHandler.Generate(passes, seed);
        else JungleWorldGenNetHandler.Apply(passes, seed);
    }

    private void Print()
    {
        ReleaseInput();
        localMessage = "";
        JungleWorldGenNetHandler.Print();
    }

    private void CycleRecipe()
    {
        ReleaseInput();
        int index = recipes.Recipes.ToList().FindIndex(recipe => recipe.Name.Equals(recipeInput.Text, StringComparison.OrdinalIgnoreCase));
        recipeInput.SetText(recipes.Recipes[(index + 1) % recipes.Recipes.Count].Name);
    }

    private void LoadRecipe()
    {
        ReleaseInput();
        JungleWorldGenRecipe recipe = recipes.Find(recipeInput.Text);
        if (recipe == null) return;
        Edit(() => draft.Load(recipe.Passes, recipe.FixedSeed));
        SyncSeedInput();
        Message($"Loaded local recipe '{recipe.Name}' into the draft.", false);
    }

    private void SaveRecipe()
    {
        ReleaseInput();
        if (!draft.TryGetRecipe(out IReadOnlyList<string> passes, out int? seed, out string error)) { Message(error, true); return; }
        if (recipes.Save(recipeInput.Text, passes, seed, out error)) Message($"Saved local recipe '{recipeInput.Text.Trim()}'.", false);
        else Message(error, true);
    }

    private void Message(string text, bool error) { localMessage = text; localError = error; }

    private string GetMessage()
    {
        if (!Networked) return "Connect to an Arenas server to configure and generate a Jungle preview.";
        if (!string.IsNullOrEmpty(localMessage)) return localMessage;
        if (!initialized) return string.IsNullOrEmpty(Status.Message) ? "Requesting server settings…" : Status.Message;
        if (JungleWorldGenNetHandler.Pending) return "Waiting for server…";
        if (!ValidSeed) { draft.TryGetRecipe(out _, out _, out string error); return error; }
        if (!string.IsNullOrEmpty(Status.Message)) return Status.Message;
        if (!Status.CanGenerate) return "Select and start the Arenas session in Game Manager to generate a Jungle preview.";
        return dirty ? "Draft changed. Apply settings or Generate preview when ready." : "Ready. Preview uses a mirrored underground vanilla Jungle.";
    }

    private Color MessageColor() => localError || !Status.Success && !string.IsNullOrEmpty(Status.Message) || initialized && !ValidSeed ? new Color(255, 151, 139) : Color.LightSteelBlue;

    private InputField AddInput(string placeholder, float left, float top, float topPercent, float widthPixels, float height, Action<string> changed, Func<bool> enabled)
    {
        InputField field = new(placeholder, changed, enabled, Focus)
        {
            Left = { Pixels = left }, Top = { Pixels = top, Percent = topPercent },
            Width = { Pixels = widthPixels, Percent = 1f }, Height = { Pixels = height }
        };
        Content.Append(field);
        return field;
    }

    private UIElement Line(Func<string> text, float top, float height, Func<Color> color, float topPercent = 0f) => new TextLine(text, color)
    {
        Top = { Pixels = top, Percent = topPercent }, Width = { Percent = 1f }, Height = { Pixels = height }
    };

    private void AddButtons(float top, float height, params (string Label, string Tooltip, Func<bool> Enabled, Action Action)[] buttons) => AddButtons(top, height, 0f, buttons);
    private void AddButtons(float top, float height, float topPercent, params (string Label, string Tooltip, Func<bool> Enabled, Action Action)[] buttons)
    {
        const float gap = 6f;
        for (int i = 0; i < buttons.Length; i++)
        {
            var button = buttons[i];
            Content.Append(Button(() => button.Label, button.Tooltip, button.Enabled, button.Action,
                i * gap / buttons.Length, i / (float)buttons.Length, top, topPercent, -gap * (buttons.Length - 1) / buttons.Length, 1f / buttons.Length, height));
        }
    }

    private static ArenaGameCommandButton Button(Func<string> text, string tooltip, Func<bool> enabled, Action action,
        float left, float leftPercent, float top, float topPercent, float width, float widthPercent, float height) => new(text, () => tooltip, enabled, () => false, action)
    {
        Left = { Pixels = left, Percent = leftPercent }, Top = { Pixels = top, Percent = topPercent },
        Width = { Pixels = width, Percent = widthPercent }, Height = { Pixels = height }
    };

    private sealed class InputField : UIPanel
    {
        private readonly UISearchBar input;
        private readonly Func<bool> enabled;
        internal string Text { get; private set; } = "";
        internal bool Writing => input.IsWritingText;

        internal InputField(string placeholder, Action<string> changed, Func<bool> enabled, Action<InputField> focus)
        {
            this.enabled = enabled;
            SetPadding(0f);
            BackgroundColor = new Color(19, 27, 57);
            BorderColor = new Color(65, 84, 140);
            input = new UISearchBar(Language.GetOrRegister("Mods.PvPArenas.Tools.JungleWorldGen." + placeholder.Replace(" ", ""), () => placeholder), .72f)
            {
                Left = { Pixels = 8f }, Width = { Pixels = -16f, Percent = 1f }, Height = { Percent = 1f }
            };
            input.SetContents("");
            input.OnContentsChanged += text => { Text = text ?? ""; changed(Text); };
            input.OnStartTakingInput += () => focus(this);
            Append(input);
        }

        public override void LeftClick(UIMouseEvent evt)
        {
            base.LeftClick(evt);
            if (enabled() && !Writing) input.ToggleTakingText();
        }

        public override void RightClick(UIMouseEvent evt)
        {
            base.RightClick(evt);
            if (enabled()) SetText("");
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (!enabled()) Release();
            BorderColor = Writing ? Color.Yellow : enabled() ? new Color(65, 84, 140) : new Color(45, 49, 63);
        }

        internal void SetText(string text) { Text = text ?? ""; input.SetContents(Text); }
        internal void Release() { if (Writing) input.ToggleTakingText(); }
    }

    private sealed class PassRow(int order, VanillaJunglePass pass, JungleWorldGenDraft draft, Func<bool> enabled, Action toggle) : UIElement
    {
        public override void OnInitialize() { Width.Set(0f, 1f); Height.Set(39f, 0f); }
        public override void LeftClick(UIMouseEvent evt)
        {
            base.LeftClick(evt);
            if (!enabled() || pass.Required) return;
            SoundEngine.PlaySound(SoundID.MenuTick);
            toggle();
        }
        protected override void DrawSelf(SpriteBatch spriteBatch)
        {
            Rectangle row = GetDimensions().ToRectangle();
            bool selected = draft.IsSelected(pass.Name);
            Color color = enabled() ? Color.White : new Color(145, 156, 178);
            spriteBatch.Draw(TextureAssets.MagicPixel.Value, row, selected ? new Color(43, 65, 103) : IsMouseHovering ? new Color(36, 49, 86) : new Color(25, 34, 62));
            Rectangle box = new(row.X + 8, row.Y + 10, 17, 17);
            spriteBatch.Draw(TextureAssets.MagicPixel.Value, box, pass.Required ? new Color(209, 170, 80) : new Color(88, 117, 173));
            box.Inflate(-3, -3);
            spriteBatch.Draw(TextureAssets.MagicPixel.Value, box, selected ? new Color(180, 214, 247) : new Color(20, 27, 49));
            DrawText(spriteBatch, order.ToString("00"), new Vector2(row.X + 32, row.Y + 6), color * .8f, .6f, 25f);
            DrawText(spriteBatch, pass.Name, new Vector2(row.X + 57, row.Y + 4), color, .72f, row.Width - 144f);
            DrawText(spriteBatch, JungleWorldGenPassInfo.GetCategory(pass.Name), new Vector2(row.X + 57, row.Y + 23), Color.LightSteelBlue, .48f, row.Width - 144f);
            if (pass.Required) DrawText(spriteBatch, "Required", new Vector2(row.Right - 72, row.Y + 11), new Color(255, 218, 125), .57f, 70f);
            if (IsMouseHovering)
                Tooltip(pass.Name + (pass.Required ? " · Required (locked)" : selected ? " · Enabled" : " · Disabled") + "\n" + JungleWorldGenPassInfo.GetDescription(pass.Name));
        }
    }

    private sealed class TextLine(Func<string> text, Func<Color> color) : UIElement
    {
        protected override void DrawSelf(SpriteBatch spriteBatch)
        {
            Rectangle box = GetDimensions().ToRectangle();
            string value = text() ?? "";
            DrawText(spriteBatch, value, box.TopLeft() + new Vector2(2f, 3f), color(), .66f, box.Width - 4f);
            if (IsMouseHovering && value.Length > 0) Tooltip(value);
        }
    }

    private sealed class ProgressLine : UIElement
    {
        protected override void DrawSelf(SpriteBatch spriteBatch)
        {
            Rectangle box = GetDimensions().ToRectangle();
            float progress = JungleWorldGenNetHandler.HasStatus ? Math.Clamp(Status.Progress, 0f, 1f) : 0f;
            string text = JungleWorldGenNetHandler.HasStatus && Status.Busy ? $"{progress:P0} · {Status.ProgressText}" : JungleWorldGenNetHandler.HasStatus && Status.LastSeed.HasValue ? $"Last generated seed: {Status.LastSeed.Value}" : "No Jungle preview generated yet";
            DrawText(spriteBatch, text, box.TopLeft(), Color.White, .66f, box.Width);
            Rectangle bar = new(box.X, box.Bottom - 4, box.Width, 3);
            spriteBatch.Draw(TextureAssets.MagicPixel.Value, bar, new Color(41, 56, 89));
            bar.Width = (int)(bar.Width * progress);
            if (bar.Width > 0) spriteBatch.Draw(TextureAssets.MagicPixel.Value, bar, new Color(126, 186, 244));
            if (IsMouseHovering) Tooltip(text);
        }
    }

    private static void Tooltip(string text)
    {
        string[] lines = Utils.WordwrapString(text, FontAssets.MouseText.Value, 520, 24, out _);
        Main.instance.MouseText(string.Join("\n", lines.Where(line => !string.IsNullOrEmpty(line))));
    }

    private static void DrawText(SpriteBatch batch, string text, Vector2 position, Color color, float scale, float width)
    {
        string visible = text.Replace('\r', ' ').Replace('\n', ' ');
        if (FontAssets.MouseText.Value.MeasureString(visible).X * scale > width)
        {
            while (visible.Length > 0 && FontAssets.MouseText.Value.MeasureString(visible + "…").X * scale > width) visible = visible[..^1];
            visible += "…";
        }
        Utils.DrawBorderString(batch, visible, position, color, scale);
    }
}
#endif
