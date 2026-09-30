using ErkySSC.Common.RegionProtection;
using Terraria.ID;

namespace PvPArenas.Common.Game;

/// <summary>ErkySSC protects the staging lobby; Arenas hides it during the countdown and fight.</summary>
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
            // Always preserve the actual world spawn, even if an admin moved the region away from it.
            area = Rectangle.Union(area, new Rectangle(Main.spawnTileX - 5, Main.spawnTileY - 6, 11, 13));
            area.Inflate(2, 2);
            return Rectangle.Intersect(area, new Rectangle(1, 4, Main.maxTilesX - 2, Main.maxTilesY - 5));
        }
    }

    public override void PostSetupContent() => RegisterLobby();

    public override void ClearWorld()
    {
        lastWorldSpawn = null;
        // A later multiplayer join must recognize server regions even after leaving mid-round.
        if (!registered)
            RegisterLobby();
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

    private void RefreshRegistration()
    {
        // Clients retain the owner registration and use ErkySSC's authoritative snapshots.
        if (Main.netMode == NetmodeID.MultiplayerClient)
            return;

        bool lobbyActive = ModContent.GetInstance<RoundManager>().CurrentPhase
            is not (RoundManager.RoundPhase.FreezeCountdown or RoundManager.RoundPhase.Playing);
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
