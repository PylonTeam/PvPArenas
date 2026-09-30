using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PvPArenas.Common.Game.LoadoutSelector;
using PvPArenas.Core.Compat;
using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Serialization;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader.Config;

namespace PvPArenas.Core.Configs;

internal sealed class ServerConfig : ModConfig
{
    public override ConfigScope Mode => ConfigScope.ServerSide;

    private BossFightPreset kingSlime = FightPresets.Create(0);
    private BossFightPreset eyeOfCthulhu = FightPresets.Create(1);
    private BossFightPreset plantera = FightPresets.Create(2);
    private BossFightPreset golem = FightPresets.Create(3);
    private int configuredPresets;
    private JArray legacyPresets;

    [Header("BossFights")]
    [Expand(false), JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace, DefaultValueHandling = DefaultValueHandling.Ignore)]
    public BossFightPreset KingSlime
    {
        get => kingSlime;
        set { kingSlime = value; configuredPresets |= 1; }
    }

    [Expand(false), JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace, DefaultValueHandling = DefaultValueHandling.Ignore)]
    public BossFightPreset EyeOfCthulhu
    {
        get => eyeOfCthulhu;
        set { eyeOfCthulhu = value; configuredPresets |= 2; }
    }

    [Expand(false), JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace, DefaultValueHandling = DefaultValueHandling.Ignore)]
    public BossFightPreset Plantera
    {
        get => plantera;
        set { plantera = value; configuredPresets |= 4; }
    }

    [Expand(false), JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace, DefaultValueHandling = DefaultValueHandling.Ignore)]
    public BossFightPreset Golem
    {
        get => golem;
        set { golem = value; configuredPresets |= 8; }
    }

    [Header("Voting")]
    [DefaultValue(30), Range(5, 300)]
    public int VotingDurationSeconds = 30;

    [DefaultValue(8), Range(0, 30)]
    public int ResultsDurationSeconds = 8;

    [Header("RoundTime")]
    [DefaultValue(600), Range(1, 3600)]
    public int RoundDurationSeconds = 600;

    [DefaultValue(20), Range(0, 300)]
    public int FreezeCountdownSeconds = 20;

    [Header("GemRewards")]
    [DefaultValue(10), Range(0, 150)]
    public int VictoryGemReward = 10;

    internal BossFightPreset GetFightPreset(int index) => index switch
    {
        0 => kingSlime,
        1 => eyeOfCthulhu,
        2 => plantera,
        3 => golem,
        _ => null
    };

    // Read the former editable list once; new named sections take precedence.
    [JsonProperty("FightPresets")]
    private JArray LegacyFightPresets { set => legacyPresets = value; }

    [OnDeserialized]
    private void OnDeserialized(StreamingContext _) => NormalizePresets();

    public override void OnLoaded() => NormalizePresets();
    public override void OnChanged() => NormalizePresets();

    public override ModConfig Clone()
    {
        ServerConfig clone = (ServerConfig)base.Clone();
        ServerConfig settings = JsonConvert.DeserializeObject<ServerConfig>(
            JsonConvert.SerializeObject(this, ConfigManager.serializerSettings), ConfigManager.serializerSettings);
        clone.kingSlime = settings.kingSlime;
        clone.eyeOfCthulhu = settings.eyeOfCthulhu;
        clone.plantera = settings.plantera;
        clone.golem = settings.golem;
        clone.legacyPresets = null;
        return clone;
    }

    private void NormalizePresets()
    {
        JsonSerializer serializer = legacyPresets == null ? null : JsonSerializer.Create(ConfigManager.serializerSettings);
        for (int index = 0; index < FightPresets.Count; index++)
        {
            BossFightPreset defaults = FightPresets.Create(index);
            BossFightPreset preset = GetFightPreset(index);
            if ((configuredPresets & (1 << index)) == 0 && legacyPresets != null)
            {
                JToken saved = legacyPresets.FirstOrDefault(entry =>
                    entry is JObject && entry["Boss"]?.ToObject<NPCDefinition>(serializer)?.Type == defaults.Boss.Type);
                if (saved != null)
                {
                    preset = saved.ToObject<BossFightPreset>(serializer);
                    JToken loadout = saved["Loadouts"]?.FirstOrDefault(entry => entry?["Loadout"] is JObject)?["Loadout"];
                    preset.MaxHealth = (int?)saved["MaxHealth"]
                        ?? (loadout != null ? (int?)loadout["MaxHealth"] ?? 500 : defaults.MaxHealth);
                    preset.MaxMana = (int?)saved["MaxMana"]
                        ?? (loadout != null ? (int?)loadout["MaxMana"] ?? 200 : defaults.MaxMana);
                }
            }

            preset ??= defaults;
            preset.Boss = defaults.Boss;
            preset.ArenaKind = defaults.ArenaKind;
            preset.MaxHealth = Math.Clamp(preset.MaxHealth, 1, 500);
            preset.MaxMana = Math.Clamp(preset.MaxMana, 0, 200);
            preset.GracePeriodSeconds = Math.Clamp(preset.GracePeriodSeconds, 0, 300);
            preset.Loadouts ??= defaults.Loadouts;
            switch (index)
            {
                case 0: kingSlime = preset; break;
                case 1: eyeOfCthulhu = preset; break;
                case 2: plantera = preset; break;
                case 3: golem = preset; break;
            }
        }

        legacyPresets = null;
        VotingDurationSeconds = Math.Clamp(VotingDurationSeconds, 5, 300);
        ResultsDurationSeconds = Math.Clamp(ResultsDurationSeconds, 0, 30);
    }

    public override bool AcceptClientChanges(ModConfig pendingConfig, int whoAmI, ref NetworkText message)
    {
        string reason = "";
        bool accepted = Main.netMode == NetmodeID.SinglePlayer || ErkySSCCompat.IsAdmin(whoAmI, out reason);
        message = NetworkText.FromLiteral(accepted ? "Saved" : reason);
        return accepted;
    }
}
