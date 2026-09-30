using System.Collections.Generic;
using Terraria.UI;

namespace PvPArenas.Common.AdminTools.WorldGenManager;

[Autoload(Side = ModSide.Client)]
internal sealed class WorldGenManagerUISystem : ModSystem
{
    private UserInterface ui;
    private UIState state;
    private WorldGenManagerPanel panel;

    internal bool IsActive => state != null && ui?.CurrentState == state;

    internal void Toggle()
    {
        if (IsActive)
        {
            Close();
            return;
        }
        ui?.SetState(state);
        WorldGenManagerNetHandler.RequestStatus();
    }

    internal void Close()
    {
        panel?.ReleaseInput();
        ui?.SetState(null);
    }

    public override void OnWorldLoad()
    {
        Close();
        ui = new UserInterface();
        state = new UIState();
        panel = new WorldGenManagerPanel();
        state.Append(panel);
    }

    public override void OnWorldUnload()
    {
        Close();
        ui = null;
        state = null;
        panel = null;
    }

    public override void Unload() => OnWorldUnload();

    public override void UpdateUI(GameTime gameTime)
    {
        if (IsActive)
            ui.Update(gameTime);
    }

    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        int index = layers.FindIndex(layer => layer.Name == "Vanilla: Mouse Text");
        if (index < 0)
            return;
        layers.Insert(index, new LegacyGameInterfaceLayer("Arenas: World Gen Manager", () =>
        {
            if (IsActive)
                ui.Draw(Main.spriteBatch, Main._drawInterfaceGameTime);
            return true;
        }, InterfaceScaleType.UI));
    }
}
