using System.Collections;
using System.Reflection;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;
using Terraria.Utilities;

internal static class ArenaVotingTests
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static int checks;
    private static object Get(object target, string name) =>
        target.GetType().GetProperty(name, All)?.GetValue(target)
        ?? target.GetType().GetField(name, All)!.GetValue(target)!;
    private static object Call(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, All)!.Invoke(target, args)!;
    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, All)!.SetValue(target, value);
    private static void Check(string name, bool passed)
    {
        if (!passed) throw new Exception("FAIL: " + name);
        checks++;
        Console.WriteLine("PASS: " + name);
    }

    public static void Run(Assembly arenas)
    {
        Main.netMode = NetmodeID.SinglePlayer;
        Main.myPlayer = 0;
        Main.rand = new UnifiedRandom(42);
        Type configType = arenas.GetType("PvPArenas.Core.Configs.ServerConfig", true)!;
        Type roundType = arenas.GetType("PvPArenas.Common.Game.RoundManager", true)!;
        Type voteType = arenas.GetType("PvPArenas.Common.Game.BossVoting.BossVoteSystem", true)!;
        Type presentationType = arenas.GetType("PvPArenas.Common.Game.BossVoting.BossVotePresentation", true)!;
        ModConfig config = (ModConfig)Activator.CreateInstance(configType, true)!;
        config.OnLoaded();
        object Preset(object source, int index) => Call(source, "GetFightPreset", index);
        int Health(object source, int index) => (int)Get(Preset(source, index), "MaxHealth");
        int Mana(object source, int index) => (int)Get(Preset(source, index), "MaxMana");
        int Boss(object source, int index) => ((NPCDefinition)Get(Preset(source, index), "Boss")).Type;
        Check("exactly four fixed boss identities", Enumerable.Range(0, 4).Select(i => Boss(config, i))
            .SequenceEqual(new int[] { NPCID.KingSlime, NPCID.EyeofCthulhu, NPCID.Plantera, NPCID.Golem })
            && Preset(config, 4) == null);
        Check("tier health and mana defaults", Enumerable.Range(0, 4).Select(i => Health(config, i))
            .SequenceEqual(new[] { 200, 200, 400, 500 })
            && Enumerable.Range(0, 4).Select(i => Mana(config, i)).SequenceEqual(new[] { 100, 100, 180, 200 }));

        JsonSerializerSettings settings = (JsonSerializerSettings)typeof(ConfigManager)
            .GetField("serializerSettings", All)!.GetValue(null)!;
        object Read(JObject json) => JsonConvert.DeserializeObject(json.ToString(), configType, settings)!;
        JObject Write(object source) => JObject.Parse(JsonConvert.SerializeObject(source, settings));
        JToken Npc(int type) => new JObject { ["Mod"] = "Terraria", ["Name"] = NPCID.Search.GetName(type) };
        JObject saved = Write(config);
        Check("config serializes named fights without editable boss IDs or list", saved["FightPresets"] == null
            && saved["KingSlime"] is JObject && saved["KingSlime"]!["Boss"] == null
            && saved["KingSlime"]!["ArenaKind"] == null);
        object roundTrip = Read(saved);
        Check("config round trip preserves all tier defaults", Enumerable.Range(0, 4).All(i =>
            Health(roundTrip, i) == Health(config, i) && Mana(roundTrip, i) == Mana(config, i)
            && Boss(roundTrip, i) == Boss(config, i)));

        JObject legacyEntry = new()
        {
            ["Boss"] = Npc(NPCID.Plantera),
            ["ArenaWidthTiles"] = 321,
            ["GracePeriodSeconds"] = 9,
            ["Loadouts"] = new JArray(new JObject
            {
                ["Name"] = "Custom",
                ["Loadout"] = new JObject { ["MaxHealth"] = 320, ["MaxMana"] = 140 }
            })
        };
        JObject legacy = new() { ["FightPresets"] = new JArray(
            new JObject { ["Boss"] = Npc(NPCID.MoonLordCore) },
            legacyEntry,
            new JObject { ["Boss"] = Npc(NPCID.None) }) };
        object migrated = Read(legacy);
        Check("legacy entries map by boss identity and preserve fight settings",
            Boss(migrated, 2) == NPCID.Plantera && (int)Get(Preset(migrated, 2), "ArenaWidthTiles") == 321
            && (int)Get(Preset(migrated, 2), "GracePeriodSeconds") == 9 && Health(migrated, 2) == 320 && Mana(migrated, 2) == 140);
        Check("legacy custom loadouts survive migration", (string)Get(((IList)Get(Preset(migrated, 2), "Loadouts"))[0]!, "Name") == "Custom");
        Check("unsupported and sandbox entries cannot replace fixed choices", Boss(migrated, 0) == NPCID.KingSlime
            && Boss(migrated, 3) == NPCID.Golem && Preset(migrated, 4) == null);
        object omittedStats = Read(new JObject { ["FightPresets"] = new JArray(new JObject
        {
            ["Boss"] = Npc(NPCID.KingSlime),
            ["Loadouts"] = new JArray(new JObject { ["Name"] = "Default stats", ["Loadout"] = new JObject() })
        }) });
        Check("omitted legacy stats retain their former 500/200 defaults",
            Health(omittedStats, 0) == 500 && Mana(omittedStats, 0) == 200);

        legacy["Plantera"] = new JObject { ["MaxHealth"] = 450, ["MaxMana"] = 160 };
        object preferred = Read(legacy);
        Check("named settings take precedence over legacy data", Health(preferred, 2) == 450 && Mana(preferred, 2) == 160);
        object clamped = Read(new JObject { ["KingSlime"] = new JObject
        {
            ["Boss"] = Npc(NPCID.MoonLordCore),
            ["MaxHealth"] = 0, ["MaxMana"] = 900
        } });
        Check("invalid stats clamp and boss identity stays fixed",
            Boss(clamped, 0) == NPCID.KingSlime && Health(clamped, 0) == 1 && Mana(clamped, 0) == 200);
        typeof(ModConfig).GetProperty("Name", All)!.SetValue(config, "ServerConfig");
        typeof(ModConfig).GetProperty("Mod", All)!.SetValue(config,
            Activator.CreateInstance(arenas.GetType("PvPArenas.PvPArenas", true)!));
        ModConfig clone = config.Clone();
        Check("config clones retain their mod and config identity",
            ReferenceEquals(clone.Mod, config.Mod) && clone.Name == "ServerConfig");
        Set(Preset(clone, 0), "MaxHealth", 350);
        Set(((IList)Get(Preset(clone, 0), "Loadouts"))[0]!, "Name", "Changed");
        Check("editing a config clone cannot mutate live fight settings", Health(config, 0) == 200
            && (string)Get(((IList)Get(Preset(config, 0), "Loadouts"))[0]!, "Name") != "Changed");

        Register(config);
        object round = Activator.CreateInstance(roundType, true)!;
        Register(round);
        FieldInfo phase = roundType.GetField("currentPhase", All)!;
        phase.SetValue(round, Enum.Parse(phase.FieldType, "VotingOrEndScreen"));
        ModSystem vote = (ModSystem)Activator.CreateInstance(voteType, true)!;
        Register(vote);
        Main.player[0] = new Player { whoAmI = 0, active = true, team = 1 };
        Main.player[1] = new Player { whoAmI = 1, active = true, team = 3 };
        Call(vote, "Start", 1800);
        uint id = (uint)Get(vote, "BallotId");
        Call(vote, "CastVote", 0, id, 2);
        Check("one vote cannot end a ballot with another active player", !(bool)Get(vote, "AllPlayersVoted"));
        Call(vote, "CastVote", 1, id, 2);
        Check("all players voting enables ErkySSC-style early completion", (bool)Get(vote, "AllPlayersVoted"));
        Check("active players can vote for a fixed boss", (int)Call(vote, "VoteCount", 2) == 2);
        Call(vote, "CastVote", 0, id, 0);
        Check("changing a choice moves one vote", (int)Call(vote, "VoteCount", 2) == 1
            && (int)Call(vote, "VoteCount", 0) == 1 && (int)Get(vote, "TotalVotes") == 2);
        Call(vote, "CastVote", 0, id, 4);
        Call(vote, "CastVote", 0, id - 1, 3);
        Check("invalid options and stale ballots are rejected", (int)Call(vote, "VoteCount", 3) == 0
            && (int)Get(vote, "TotalVotes") == 2);
        Set(round, "showingResults", true);
        Call(vote, "CastVote", 0, id, 1);
        Check("end-screen stage cannot accept votes", (int)Call(vote, "VoteCount", 1) == 0);
        Set(round, "showingResults", false);
        Main.player[1].active = false;
        vote.PostUpdatePlayers();
        Check("departed players stop counting", (int)Get(vote, "TotalVotes") == 1);
        int winner = (int)Call(vote, "Complete");
        Call(vote, "CastVote", 0, id, 3);
        Check("winner is stable and completed voting is locked",
            winner == 0 && (int)Call(vote, "Complete") == 0 && !(bool)Get(vote, "Active"));
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, true)) vote.NetSend(writer);
        stream.Position = 0;
        ModSystem client = (ModSystem)Activator.CreateInstance(voteType, true)!;
        using (BinaryReader reader = new(stream, System.Text.Encoding.UTF8, true)) client.NetReceive(reader);
        Check("network snapshot carries ballot, winner, timing, and voter IDs",
            (uint)Get(client, "BallotId") == id && (int)Get(client, "Winner") == 0
            && (int)Get(client, "DurationTicks") == 1800 && (int)Get(client, "LocalVote") == 0);
        Call(vote, "Start", 300);
        Check("new ballot clears the previous result and votes",
            (uint)Get(vote, "BallotId") != id && (int)Get(vote, "Winner") == -1 && (int)Get(vote, "TotalVotes") == 0);
        Check("empty ballot picks one of the fixed bosses", (int)Call(vote, "Complete") is >= 0 and < 4);

        phase.SetValue(round, Enum.Parse(phase.FieldType, "Playing"));
        Set(round, "selectedPresetIndex", 2);
        Type playerType = arenas.GetType("PvPArenas.Common.Game.LoadoutSelector.ArenaPlayer", true)!;
        object arenaPlayer = Activator.CreateInstance(playerType, true)!;
        playerType.GetProperty("Entity", All)!.SetValue(arenaPlayer, Main.player[0]);
        Set(Preset(config, 2), "MaxHealth", 330);
        Set(Preset(config, 2), "MaxMana", 120);
        Set(arenaPlayer, "SelectedLoadoutIndex", 3);
        object[] stats = [StatModifier.Default, StatModifier.Default];
        playerType.GetMethod("ModifyMaxStats", All)!.Invoke(arenaPlayer, stats);
        Check("player max stats use fight settings regardless of selected loadout",
            ((StatModifier)stats[0]).ApplyTo(100) == 330 && ((StatModifier)stats[1]).ApplyTo(20) == 120);

        object presentation = Activator.CreateInstance(presentationType, true)!;
        Call(presentation, "Update", 1u, true, -1, .1f);
        Check("vote opens over time", (float)Get(presentation, "Opening") == .5f && (bool)Get(presentation, "Interactive"));
        Call(presentation, "Update", 1u, false, 2, 0f);
        Check("completion locks input and shows the server winner",
            (bool)Get(presentation, "Complete") && !(bool)Get(presentation, "Interactive")
            && (int)Get(presentation, "Winner") == 2);
        Call(presentation, "Update", 1u, false, 2, 1.5f);
        Check("winner holds for the ErkySSC duration", (float)Get(presentation, "Closing") == 0f);
        Call(presentation, "Update", 1u, false, 2, .2f);
        Check("closing animation progresses smoothly", Math.Abs((float)Get(presentation, "Closing") - .5f) < .001f);
        Call(presentation, "Update", 1u, false, 2, .21f);
        Call(presentation, "Update", 1u, false, 2, .1f);
        Check("completed ballots stay closed", !(bool)Get(presentation, "Visible"));
        Call(presentation, "Update", 2u, true, -1, .1f);
        Check("next ballot starts a fresh opening", (float)Get(presentation, "Opening") == .5f
            && (bool)Get(presentation, "Interactive") && !(bool)Get(presentation, "Complete"));
        Call(presentation, "Update", 2u, false, -1, 0f);
        Check("cancelled ballots clear presentation", !(bool)Get(presentation, "Visible"));

        Console.WriteLine($"{checks} arena voting checks passed.");
    }

    private static void Register(object value)
    {
        Type registry = typeof(ModContent).Assembly.GetType("Terraria.ModLoader.ContentInstance")!;
        IDictionary entries = (IDictionary)registry.GetField("contentByType", All)!.GetValue(null)!;
        if (entries[value.GetType()] is { } entry) entry.GetType().GetMethod("Clear", All)!.Invoke(entry, null);
        registry.GetMethod("Register", All)!.Invoke(null, [value]);
    }
}
