using System;
using Terraria.Audio;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.UI;

namespace PvPArenas.Common.AdminTools.UI;

internal abstract class UIDraggablePanel : UIElement
{
    private readonly UIPanel titleBar;
    private readonly UIPanel closeButton;
    private readonly UIPanel refreshButton;
    private readonly UIResizeHandle resizeHandle;
    private bool dragging;
    private Vector2 dragOffset;

    protected UIPanel Content { get; }
    protected abstract void OnClosePanelLeftClick();
    protected virtual void OnRefreshPanelLeftClick() { }
    protected virtual float MinResizeW => 360f;
    protected virtual float MinResizeH => 250f;
    protected virtual float MaxResizeW => 1000f;
    protected virtual float MaxResizeH => 1000f;

    protected UIDraggablePanel(string title)
    {
        Width.Set(350f, 0f);
        Height.Set(460f, 0f);
        HAlign = .9f;
        VAlign = .7f;
        SetPadding(0f);

        titleBar = new UIPanel
        {
            Width = { Percent = 1f },
            Height = { Pixels = 40f },
            BackgroundColor = new Color(63, 82, 151)
        };
        titleBar.SetPadding(0f);
        titleBar.Append(new UIText(title, .86f, true) { HAlign = .5f, VAlign = .5f });

        Content = new UIPanel
        {
            Top = { Pixels = 40f },
            Width = { Percent = 1f },
            Height = { Pixels = -40f, Percent = 1f },
            BackgroundColor = new Color(20, 20, 60) * .7f,
            BorderColor = Color.Black
        };
        Content.SetPadding(0f);

        refreshButton = HeaderButton(0f, new UIImage(Ass.IconRefresh.Value) { HAlign = .5f, VAlign = .5f });
        refreshButton.OnLeftClick += (_, _) => OnRefreshPanelLeftClick();

        closeButton = HeaderButton(1f, new UIText("X", .72f, true) { HAlign = .5f, VAlign = .5f });
        closeButton.OnLeftClick += (_, _) =>
        {
            SoundEngine.PlaySound(SoundID.MenuClose);
            OnClosePanelLeftClick();
        };

        titleBar.Append(refreshButton);
        titleBar.Append(closeButton);
        Append(Content);
        Append(titleBar);

        resizeHandle = new UIResizeHandle();
        resizeHandle.Dragged += Resize;
        Append(resizeHandle);
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        if (IsMouseHovering)
            Main.LocalPlayer.mouseInterface = true;
        if (!dragging || Parent == null)
            return;
        if (!Main.mouseLeft)
        {
            dragging = false;
            return;
        }

        CalculatedStyle parent = Parent.GetDimensions();
        Vector2 mouse = Main.MouseScreen - dragOffset - parent.Position();
        Left.Set(MathHelper.Clamp(mouse.X, 0f, Math.Max(0f, parent.Width - GetDimensions().Width)), 0f);
        Top.Set(MathHelper.Clamp(mouse.Y, 0f, Math.Max(0f, parent.Height - GetDimensions().Height)), 0f);
        Recalculate();
    }

    protected override void DrawSelf(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
    {
        base.DrawSelf(spriteBatch);
        if (refreshButton.IsMouseHovering)
            Main.instance.MouseText("Refresh");
    }

    public override void LeftMouseDown(UIMouseEvent evt)
    {
        base.LeftMouseDown(evt);
        if (Parent == null || !titleBar.ContainsPoint(evt.MousePosition) || IsOverButton())
            return;

        UIElement parent = Parent;
        parent.RemoveChild(this);
        parent.Append(this);
        UsePixelPosition();
        dragging = true;
        dragOffset = Main.MouseScreen - GetDimensions().Position();
    }

    public override void LeftMouseUp(UIMouseEvent evt)
    {
        base.LeftMouseUp(evt);
        dragging = false;
    }

    private UIPanel HeaderButton(float align, UIElement child)
    {
        UIPanel button = new()
        {
            Width = { Pixels = 40f },
            Height = { Percent = 1f },
            HAlign = align,
            VAlign = .5f
        };
        button.SetPadding(0f);
        button.OnMouseOver += (_, _) => button.BorderColor = Color.Yellow;
        button.OnMouseOut += (_, _) => button.BorderColor = Color.Black;
        button.Append(child);
        return button;
    }

    private void Resize(Vector2 delta)
    {
        if (Parent == null)
            return;
        UsePixelPosition();
        CalculatedStyle parent = Parent.GetDimensions();
        float maxWidth = Math.Max(MinResizeW, Math.Min(MaxResizeW, parent.Width - Left.Pixels));
        float maxHeight = Math.Max(MinResizeH, Math.Min(MaxResizeH, parent.Height - Top.Pixels));
        Width.Set(MathHelper.Clamp(Width.Pixels + delta.X, MinResizeW, maxWidth), 0f);
        Height.Set(MathHelper.Clamp(Height.Pixels + delta.Y, MinResizeH, maxHeight), 0f);
        Recalculate();
    }

    private void UsePixelPosition()
    {
        if (Parent == null || HAlign == 0f && VAlign == 0f && Left.Percent == 0f && Top.Percent == 0f)
            return;
        CalculatedStyle parent = Parent.GetDimensions();
        CalculatedStyle panel = GetDimensions();
        HAlign = VAlign = 0f;
        Left.Set(panel.X - parent.X, 0f);
        Top.Set(panel.Y - parent.Y, 0f);
        Recalculate();
    }

    private bool IsOverButton() => closeButton.IsMouseHovering || refreshButton.IsMouseHovering || resizeHandle.IsMouseHovering;
}
