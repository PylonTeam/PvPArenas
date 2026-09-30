using Microsoft.Xna.Framework.Graphics;
using PvPArenas.Common.AdminTools.GameManager;
using PvPArenas.Common.AdminTools.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.Localization;
using Terraria.UI;

namespace PvPArenas.Common.AdminTools.WorldGenManager;

internal sealed class WorldGenManagerPanel : UIDraggablePanel
{
    private readonly UIList passList;
    private readonly UIPanel searchPanel;
    private readonly UISearchBar search;
    private readonly HashSet<string> selected = new(StringComparer.OrdinalIgnoreCase);
    private string[] displayedPasses = [];
    private string filter = "";
    private string error = "";
    private bool rebuild;

    protected override float MinResizeW => 360f;
    protected override float MinResizeH => 320f;
    protected override float MaxResizeW => 600f;
    protected override float MaxResizeH => 800f;
    private static WorldGenPassRunner Runner => ModContent.GetInstance<WorldGenPassRunner>();
    private static bool CanEdit => Runner.Available && !Runner.Busy;

    internal WorldGenManagerPanel() : base("World Generation")
    {
        Width.Set(440f, 0f);
        Height.Set(500f, 0f);
        HAlign = .5f;
        VAlign = 0f;
        Top.Set(80f, 0f);
        Content.SetPadding(10f);

        searchPanel = new UIPanel
        {
            Width = { Pixels = -112f, Percent = 1f },
            Height = { Pixels = 32f },
            BackgroundColor = new Color(19, 27, 57),
            BorderColor = new Color(65, 84, 140)
        };
        searchPanel.SetPadding(0f);
        search = new UISearchBar(Language.GetOrRegister("Mods.PvPArenas.Tools.WorldGenManager.Search", () => "Search passes"), .72f)
        {
            Left = { Pixels = 8f },
            Width = { Pixels = -16f, Percent = 1f },
            Height = { Percent = 1f }
        };
        search.OnContentsChanged += text => { filter = text ?? ""; rebuild = true; };
        search.OnStartTakingInput += () => Main.blockInput = true;
        search.OnEndTakingInput += () => Main.blockInput = false;
        search.SetContents("");
        searchPanel.OnLeftClick += (_, _) =>
        {
            if (CanEdit && !search.IsWritingText)
                search.ToggleTakingText();
        };
        searchPanel.OnRightClick += (_, _) => { if (CanEdit) search.SetContents(""); };
        searchPanel.Append(search);
        Content.Append(searchPanel);

        Content.Append(Button(() => "All", () => CanEdit, () =>
        {
            selected.UnionWith(Runner.PassNames);
            error = "";
        }, -106f, 1f, 0f, 0f, 50f, 0f, 32f));
        Content.Append(Button(() => "None", () => CanEdit && selected.Count > 0, () =>
        {
            selected.Clear();
            error = "";
        }, -50f, 1f, 0f, 0f, 50f, 0f, 32f));

        UIPanel listPanel = new()
        {
            Top = { Pixels = 40f },
            Width = { Percent = 1f },
            Height = { Pixels = -120f, Percent = 1f },
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
        UIScrollbar scrollbar = new()
        {
            HAlign = 1f,
            Width = { Pixels = 20f },
            Height = { Percent = 1f }
        };
        passList.SetScrollbar(scrollbar);
        listPanel.Append(passList);
        listPanel.Append(scrollbar);
        Content.Append(listPanel);

        Content.Append(Button(() => Runner.Busy ? "Running…" : selected.Count == 0 ? "Run" : $"Run ({selected.Count})",
            () => CanEdit && selected.Count > 0, RunSelected,
            0f, 0f, -72f, 1f, 0f, 1f, 38f, () => VanillaAdminIcons.PlayPause));
        Content.Append(new StatusLine(() => string.IsNullOrEmpty(error) ? Runner.Status : error)
        {
            Top = { Pixels = -28f, Percent = 1f },
            Width = { Percent = 1f },
            Height = { Pixels = 26f }
        });
        RebuildList();
    }

    protected override void OnClosePanelLeftClick() => ModContent.GetInstance<WorldGenManagerUISystem>().Close();

    protected override void OnRefreshPanelLeftClick()
    {
        error = "";
        WorldGenManagerNetHandler.RequestStatus();
    }

    internal void ReleaseInput()
    {
        if (search.IsWritingText)
            search.ToggleTakingText();
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        if (search.IsWritingText && (!CanEdit || Main.mouseLeft && !searchPanel.IsMouseHovering))
            ReleaseInput();
        searchPanel.BorderColor = search.IsWritingText ? new Color(151, 189, 255) : new Color(65, 84, 140);
        if (rebuild || !displayedPasses.SequenceEqual(Runner.PassNames))
            RebuildList();
    }

    private void RebuildList()
    {
        rebuild = false;
        displayedPasses = Runner.PassNames.ToArray();
        selected.IntersectWith(displayedPasses);
        passList.Clear();
        for (int i = 0; i < displayedPasses.Length; i++)
        {
            string name = displayedPasses[i];
            if (name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                passList.Add(new PassRow(i + 1, name, () => selected.Contains(name), () =>
                {
                    if (!selected.Add(name))
                        selected.Remove(name);
                    error = "";
                }));
        }
        if (passList.Count == 0 && Runner.Available)
            passList.Add(new UIText("No matching passes", .72f) { Height = { Pixels = 28f } });
    }

    private void RunSelected()
    {
        ReleaseInput();
        string[] passes = Runner.PassNames.Where(selected.Contains).ToArray();
        WorldGenManagerNetHandler.RequestRunPasses(passes, out error);
    }

    private static ArenaGameCommandButton Button(Func<string> label, Func<bool> enabled, Action action,
        float left, float leftPercent, float top, float topPercent, float width, float widthPercent, float height,
        Func<AdminUIIcon> icon = null) => new(label, () => "", enabled, () => false, action, icon)
        {
            Left = { Pixels = left, Percent = leftPercent },
            Top = { Pixels = top, Percent = topPercent },
            Width = { Pixels = width, Percent = widthPercent },
            Height = { Pixels = height }
        };

    private sealed class PassRow : UIElement
    {
        private readonly int order;
        private readonly string name;
        private readonly Func<bool> isSelected;
        private readonly Action toggle;

        internal PassRow(int order, string name, Func<bool> isSelected, Action toggle)
        {
            this.order = order;
            this.name = name;
            this.isSelected = isSelected;
            this.toggle = toggle;
            Width.Set(0f, 1f);
            Height.Set(29f, 0f);
        }

        public override void LeftClick(UIMouseEvent evt)
        {
            base.LeftClick(evt);
            if (!CanEdit)
                return;
            SoundEngine.PlaySound(SoundID.MenuTick);
            toggle();
        }

        protected override void DrawSelf(SpriteBatch spriteBatch)
        {
            Rectangle box = GetDimensions().ToRectangle();
            bool active = isSelected();
            bool hover = CanEdit && IsMouseHovering;
            Color fill = active ? new Color(53, 76, 130) : hover ? new Color(36, 49, 86) : new Color(25, 34, 62);
            spriteBatch.Draw(TextureAssets.MagicPixel.Value, box, fill);
            Rectangle check = new(box.X + 8, box.Y + 7, 15, 15);
            spriteBatch.Draw(TextureAssets.MagicPixel.Value, check, active ? new Color(147, 196, 255) : new Color(85, 104, 148));
            check.Inflate(-2, -2);
            spriteBatch.Draw(TextureAssets.MagicPixel.Value, check, fill);
            if (active)
            {
                check.Inflate(-2, -2);
                spriteBatch.Draw(TextureAssets.MagicPixel.Value, check, new Color(194, 221, 255));
            }
            Color text = CanEdit ? Color.White : new Color(145, 156, 178);
            Utils.DrawBorderString(spriteBatch, order.ToString("00"), new Vector2(box.X + 31, box.Y + 8), new Color(137, 157, 195), .55f);
            DrawText(spriteBatch, name, new Vector2(box.X + 58, box.Y + 6), text, .70f, box.Width - 66f);
            if (IsMouseHovering)
                Main.instance.MouseText(name);
        }
    }

    private sealed class StatusLine(Func<string> status) : UIElement
    {
        protected override void DrawSelf(SpriteBatch spriteBatch)
        {
            Rectangle box = GetDimensions().ToRectangle();
            string text = status() ?? "";
            string percentage = Runner.Busy ? Runner.Progress.ToString("P0") : "";
            float reserved = percentage.Length == 0 ? 0f : 44f;
            DrawText(spriteBatch, text, new Vector2(box.X + 2, box.Y + 3), new Color(177, 194, 222), .64f, box.Width - reserved - 6f);
            if (percentage.Length > 0)
                Utils.DrawBorderString(spriteBatch, percentage, new Vector2(box.Right - 3, box.Y + 3), Color.LightBlue, .64f, 1f);
            if (Runner.Busy)
            {
                Rectangle bar = new(box.X + 2, box.Bottom - 3, box.Width - 4, 2);
                spriteBatch.Draw(TextureAssets.MagicPixel.Value, bar, new Color(41, 56, 89));
                bar.Width = (int)(bar.Width * Math.Clamp(Runner.Progress, 0d, 1d));
                if (bar.Width > 0)
                    spriteBatch.Draw(TextureAssets.MagicPixel.Value, bar, new Color(126, 186, 244));
            }
            if (IsMouseHovering && text.Length > 0)
            {
                string[] lines = Utils.WordwrapString(text, FontAssets.MouseText.Value, 440, 20, out _);
                Main.instance.MouseText(string.Join("\n", lines.Where(line => !string.IsNullOrEmpty(line))));
            }
        }
    }

    private static void DrawText(SpriteBatch batch, string text, Vector2 position, Color color, float scale, float width)
    {
        text = text.Replace('\r', ' ').Replace('\n', ' ');
        string visible = text;
        if (FontAssets.MouseText.Value.MeasureString(text).X * scale > width)
        {
            while (visible.Length > 0 && FontAssets.MouseText.Value.MeasureString(visible + "…").X * scale > width)
                visible = visible[..^1];
            visible += "…";
        }
        Utils.DrawBorderString(batch, visible, position, color, scale);
    }
}
