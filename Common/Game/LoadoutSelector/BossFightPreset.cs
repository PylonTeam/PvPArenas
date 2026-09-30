using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace PvPArenas.Common.Game.LoadoutSelector;

internal sealed class BossFightPreset
{
    [JsonIgnore]
    public NPCDefinition Boss { get; internal set; } = new();

    [DefaultValue(500), Range(1, 500)]
    public int MaxHealth = 500;

    [DefaultValue(200), Range(0, 200)]
    public int MaxMana = 200;

    [Expand(true)]
    public List<ArenaLoadoutOption> Loadouts = [];

    [JsonIgnore]
    public ArenaKind ArenaKind = ArenaKind.ArenasV10;

    [DefaultValue(5), Range(0, 300)]
    public int GracePeriodSeconds = 5;

    /// <summary>
    /// A sandbox arena has no boss NPC configured. Its loadouts are empty by default and are
    /// filled in per-player through the local loadout editor / item picker.
    /// </summary>
    public bool IsSandbox() => (Boss?.Type ?? 0) <= 0;
}
