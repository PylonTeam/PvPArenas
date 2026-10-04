using ErkySSC.Common.RegionProtection;
using Terraria.ID;

namespace PvPArenas.Common.Game;

/// <summary>ErkySSC protects the idle lobby; Arenas owns player protection during replacement and fights.</summary>
internal sealed class ArenaSpawnBoxIntegration : ModSystem
{
    internal const string RegionKey = "PvPArenas.Spawnbox";
    private bool registered;
    private Point? lastWorldSpawn;

    internal static Rectangle LobbyBounds
    {
        get
        {
            Rectangle area = RegionSystem.Instance.FindManaged(RegionKey)?.Settings.TileArea
                ?? RegionSystem.Defaults().TileArea;
            // Include the actual world spawn when reporting the lobby's occupied bounds.
            area = Rectangle.Union(area, new Rectangle(Main.spawnTileX - 5, Main.spawnTileY - 6, 11, 13));
            area.Inflate(2, 2);
            return Rectangle.Intersect(area, new Rectangle(1, 4, Main.maxTilesX - 2, Main.maxTilesY - 5));
        }
    }

    public override void PostSetupContent()
    {
        // Clients need the owner definition to understand ErkySSC snapshots.
        if (Main.netMode == NetmodeID.MultiplayerClient)
            RegisterLobby();
    }

    public override void ClearWorld()
    {
        lastWorldSpawn = null;
        // A later multiplayer join must recognize server regions even after leaving mid-round.
        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            if (!registered)
                RegisterLobby();
        }
        else
            HideLobby();
    }

    public override void PreUpdateEntities()
    {
        if (Main.netMode == NetmodeID.MultiplayerClient)
            return;

        RefreshRegistration();
        if (RegionSystem.Instance.FindManaged(RegionKey) is not { } region)
            return;

        // Preserve admin edits when the world spawn moves, including moves during a round.
        Point spawn = new(Main.spawnTileX, Main.spawnTileY);
        if (lastWorldSpawn is { } previous && spawn != previous)
            RegionSystem.Instance.UpdateRegion(region.Id, region.Settings with
            {
                X = region.Settings.X + spawn.X - previous.X,
                Y = region.Settings.Y + spawn.Y - previous.Y
            });
        lastWorldSpawn = spawn;
    }

    public override void Unload()
    {
        RegionSystem.Instance.UnregisterManaged(RegionKey);
        registered = false;
        lastWorldSpawn = null;
    }

    internal static void UpdateMatchState() =>
        ModContent.GetInstance<ArenaSpawnBoxIntegration>().RefreshRegistration();

    internal static void HideLobby()
    {
        ArenaSpawnBoxIntegration integration = ModContent.GetInstance<ArenaSpawnBoxIntegration>();
        RegionSystem.Instance.UnregisterManaged(RegionKey);
        integration.registered = false;
    }

    private void RefreshRegistration()
    {
        // Clients retain the owner registration and use ErkySSC's authoritative snapshots.
        if (Main.netMode == NetmodeID.MultiplayerClient)
            return;

        // Region tile protection must not veto the server's generated objects or framing.
        // Generating already freezes players and makes them immune through ArenaPlayer.
        RoundManager manager = ModContent.GetInstance<RoundManager>();
        bool lobbyActive = manager.IsActive && manager.CurrentPhase
            is RoundManager.RoundPhase.WaitingForPlayers or RoundManager.RoundPhase.VotingOrEndScreen;
        if (registered == lobbyActive)
            return;

        if (lobbyActive)
        {
            RegisterLobby();
            RegionSystem.Instance.RefreshManagedRegions();
        }
        else
        {
            // Unregistering hides the region without deleting its saved geometry or settings.
            RegionSystem.Instance.UnregisterManaged(RegionKey);
            registered = false;
        }
    }

    private void RegisterLobby()
    {
        RegionSystem.Instance.RegisterManaged(RegionKey, new(
            () => new RegionSeed(RegionSystem.Defaults(), new RegionOptions { SpawnProtection = true }),
            () => true));
        registered = true;
    }
}
