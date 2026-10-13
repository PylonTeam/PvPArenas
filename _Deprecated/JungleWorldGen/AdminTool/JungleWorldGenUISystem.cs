#if DEBUG
using ErkySSC.Common.AdminTools.UI;
using Microsoft.Xna.Framework.Input;
using PvPArenas.Core.Compat;
using Terraria.UI;

namespace PvPArenas.Common.AdminTools.JungleWorldGen;

[Autoload(Side = ModSide.Client)]
internal sealed class JungleWorldGenUISystem : ModSystem
{
    private UserInterface ui;
    private UIState state;
    private JungleWorldGenPanel panel;
    private double nextPoll;

    internal bool IsActive => state != null && ui?.CurrentState == state;
    internal static bool CanUse => !Main.dedServ && !Main.gameMenu && ErkySSCCompat.IsPlayerAdmin(Main.LocalPlayer, out _);

    internal void Toggle()
    {
        if (IsActive) Close();
        else if (CanUse && state != null)
        {
            ui.SetState(state);
            AdminPanelLayerSystem.BringToFront(panel);
            nextPoll = 0d;
            JungleWorldGenNetHandler.RequestStatus();
        }
    }

    internal void Close()
    {
        panel?.ReleaseInput();
        ui?.SetState(null);
    }

    internal void ResetClient()
    {
        Close();
        panel?.ResetDraft();
        JungleWorldGenNetHandler.ResetClient();
    }

    public override void OnWorldLoad()
    {
        OnWorldUnload();
        ui = new UserInterface();
        state = new UIState();
        panel = new JungleWorldGenPanel();
        state.Append(panel);
        AdminPanelLayerSystem.Register("Arenas.JungleWorldGen", ui, state, panel);
    }

    public override void OnWorldUnload()
    {
        ResetClient();
        ui = null;
        state = null;
        panel = null;
        nextPoll = 0d;
    }

    public override void Unload() => OnWorldUnload();

    public override void UpdateUI(GameTime gameTime)
    {
        if (!IsActive) return;
        if (!CanUse)
        {
            ResetClient();
            return;
        }
        if (!Main.hasFocus) panel.ReleaseInput();
        if (Main.keyState.IsKeyDown(Keys.Escape) && !Main.oldKeyState.IsKeyDown(Keys.Escape))
        {
            Close();
            return;
        }
        double now = gameTime.TotalGameTime.TotalSeconds;
        if (now >= nextPoll)
        {
            nextPoll = now + 1d;
            JungleWorldGenNetHandler.RequestStatus();
        }
        // ErkySSC updates and draws the registered panel, including its mouse shield.
        panel.ObserveServerStatus();
    }
}
#endif
