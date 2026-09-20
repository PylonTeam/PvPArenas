using System.Collections;
using System.Reflection;
using ErkySSC.Common.RegionProtection;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

internal static class RegionIntegrationTests
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private const string Key = "PvPArenas.Spawnbox";
    private static int checks;

    public static void Run(Assembly arenas)
    {
        // Exercise the compiled mods with real tModLoader types, without a game or Steam session.
        Main.netMode = NetmodeID.SinglePlayer;
        Main.maxTilesX = 400;
        Main.maxTilesY = 300;
        Main.spawnTileX = 200;
        Main.spawnTileY = 80;
        RegionSystem regions = new();
        Register(regions);
        Type roundType = arenas.GetType("PvPArenas.Common.Game.RoundManager", true)!;
        object round = Activator.CreateInstance(roundType, true)!;
        Register(round);
        FieldInfo phase = roundType.GetField("currentPhase", All)!;
        Type integrationType = arenas.GetType("PvPArenas.Common.Game.ArenaSpawnBoxIntegration", true)!;
        ModSystem integration = (ModSystem)Activator.CreateInstance(integrationType, true)!;
        Register(integration);
        MethodInfo update = integrationType.GetMethod("UpdateMatchState", All)!;
        void SetPhase(string value)
        {
            phase.SetValue(round, Enum.Parse(phase.FieldType, value));
            update.Invoke(null, null);
        }

        int manualId = regions.AddRegion();
        RegionSettings manualSettings = RegionSystem.Defaults() with { X = 20, Y = 20, Width = 10, Height = 10 };
        regions.UpdateRegion(manualId, manualSettings);
        integration.PostSetupContent();
        regions.PreUpdateEntities();
        integration.PreUpdateEntities();
        ProtectedRegion lobby = regions.FindManaged(Key);
        Check("lobby is a separate protected region at world spawn", lobby != null && lobby.Id != manualId
            && lobby.Settings == RegionSystem.Defaults() && lobby.Behavior.SpawnProtection);

        RegionSettings edited = lobby.Settings with { Width = 73, Height = 29, X = 215, Y = 95, CanInteract = true };
        regions.UpdateRegion(lobby.Id, edited);
        int revision = regions.Revision;
        integration.PreUpdateEntities();
        regions.PreUpdateEntities();
        Check("unchanged ticks do not broadcast or recreate the lobby", regions.Revision == revision);

        SetPhase("Generating");
        Check("lobby remains protected during arena generation", regions.FindManaged(Key) != null);
        SetPhase("FreezeCountdown");
        Check("countdown immediately hides lobby protection", regions.FindManaged(Key) == null
            && regions.Regions.Count == 1 && regions.AllowsTile(215, 95, RegionField.CanUseItems));
        Check("manual regions remain unchanged", regions.Find(manualId).Settings == manualSettings);
        Check("hidden lobby retains its saved settings", regions.Find(lobby.Id).Settings == edited);
        byte[] hiddenSnapshot = Snapshot(regions);
        TagCompound hiddenSave = new();
        regions.SaveWorldData(hiddenSave);
        revision = regions.Revision;
        SetPhase("Playing");
        regions.PreUpdateEntities();
        integration.PreUpdateEntities();
        Check("playing keeps lobby dormant without repeated changes", regions.FindManaged(Key) == null && regions.Revision == revision);

        Main.spawnTileX += 7;
        Main.spawnTileY += 3;
        integration.PreUpdateEntities();
        SetPhase("VotingOrEndScreen");
        integration.PreUpdateEntities();
        RegionSettings shifted = edited with { X = edited.X + 7, Y = edited.Y + 3 };
        Check("ending restores the same region and follows spawn movement", regions.FindManaged(Key).Id == lobby.Id
            && regions.FindManaged(Key).Settings == shifted && regions.Regions.Count == 2);
        SetPhase("WaitingForPlayers");
        Check("waiting keeps the restored lobby", regions.FindManaged(Key).Settings == shifted);
        byte[] lobbySnapshot = Snapshot(regions);

        regions.ClearWorld();
        integration.ClearWorld();
        regions.LoadWorldData(hiddenSave);
        regions.PreUpdateEntities();
        integration.PreUpdateEntities();
        Check("a save made during combat restores lobby settings without duplication", regions.FindManaged(Key).Id == lobby.Id
            && regions.FindManaged(Key).Settings == edited && regions.Regions.Count == 2);

        SetPhase("Playing");
        regions.ClearWorld();
        integration.ClearWorld();
        Main.netMode = NetmodeID.MultiplayerClient;
        phase.SetValue(round, Enum.Parse(phase.FieldType, "WaitingForPlayers"));
        using (BinaryReader reader = new(new MemoryStream(lobbySnapshot))) regions.NetReceive(reader);
        integration.PreUpdateEntities();
        Check("joining multiplayer after leaving mid-round recognizes the server lobby", regions.FindManaged(Key)?.Settings == shifted);

        RegionSystem client = new();
        Register(client);
        ModSystem clientIntegration = (ModSystem)Activator.CreateInstance(integrationType, true)!;
        Register(clientIntegration);
        clientIntegration.PostSetupContent();
        using (BinaryReader reader = new(new MemoryStream(hiddenSnapshot))) client.NetReceive(reader);
        clientIntegration.PreUpdateEntities();
        client.PreUpdateEntities();
        Check("late join during combat does not invent a lobby from a stale waiting phase", client.FindManaged(Key) == null
            && client.Regions.Count == 1);
        using (BinaryReader reader = new(new MemoryStream(lobbySnapshot))) client.NetReceive(reader);
        phase.SetValue(round, Enum.Parse(phase.FieldType, "Playing"));
        update.Invoke(null, null);
        clientIntegration.PreUpdateEntities();
        Check("client phase cannot override server lobby snapshots", client.FindManaged(Key).Settings == shifted);
        clientIntegration.Unload();
        Check("unload removes only Arenas lobby behavior", client.FindManaged(Key) == null && client.Regions.Count == 1);

        Console.WriteLine($"{checks} Arenas/ErkySSC region checks passed.");
    }

    private static byte[] Snapshot(RegionSystem regions)
    {
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, true)) regions.NetSend(writer);
        return stream.ToArray();
    }

    private static void Register(object value)
    {
        Type registry = typeof(ModContent).Assembly.GetType("Terraria.ModLoader.ContentInstance")!;
        IDictionary entries = (IDictionary)registry.GetField("contentByType", All)!.GetValue(null)!;
        if (entries[value.GetType()] is { } entry) entry.GetType().GetMethod("Clear", All)!.Invoke(entry, null);
        registry.GetMethod("Register", All)!.Invoke(null, [value]);
    }

    private static void Check(string name, bool passed)
    {
        if (!passed) throw new Exception("FAIL: " + name);
        checks++;
        Console.WriteLine("PASS: " + name);
    }
}
