using PvPArenas.Common.Game;
using Terraria.GameContent.Events;
using Terraria.ID;

namespace PvPArenas.Common.CultistArena;

/// <summary>Uses regular Dungeon spawns without advancing the world's Skeletron progression.</summary>
internal sealed class CultistDungeonSystem : ModSystem
{
    public override void Load()
    {
        On_NPC.SpawnNPC += OnSpawnNPC;
        On_CultistRitual.TrySpawning += OnTrySpawning;
    }

    public override void Unload()
    {
        On_NPC.SpawnNPC -= OnSpawnNPC;
        On_CultistRitual.TrySpawning -= OnTrySpawning;
    }

    private static bool IsCultistArena => ModContent.GetInstance<RoundManager>() is
        { IsActive: true, SelectedBossType: NPCID.CultistBoss, CurrentLayout: not null };

    private static void OnTrySpawning(On_CultistRitual.orig_TrySpawning orig, int x, int y)
    {
        // The world-event timer can otherwise create a second, unmanaged tablet
        // at this natural entrance once the previous round boss is cleaned up.
        if (!IsCultistArena)
            orig(x, y);
    }

    private static void OnSpawnNPC(On_NPC.orig_SpawnNPC orig)
    {
        RoundManager manager = ModContent.GetInstance<RoundManager>();
        bool regularDungeon = IsCultistArena
            && manager.CurrentPhase is RoundManager.RoundPhase.FreezeCountdown or RoundManager.RoundPhase.Playing;
        if (!regularDungeon)
        {
            orig();
            return;
        }

        // Both the guardian spawn branch and its aggressive spawn-rate override
        // read this flag inside the native spawn pass. Restore it before leaving
        // that pass, including when another mod's spawn hook throws.
        bool previous = NPC.downedBoss3;
        try
        {
            NPC.downedBoss3 = true;
            orig();
        }
        finally
        {
            NPC.downedBoss3 = previous;
        }
    }
}
