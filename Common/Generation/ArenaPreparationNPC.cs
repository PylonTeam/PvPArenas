using PvPArenas.Common.Game;

namespace PvPArenas.Common.Generation;

internal sealed class ArenaPreparationNPC : GlobalNPC
{
    public override void EditSpawnRate(Player player, ref int spawnRate, ref int maxSpawns)
    {
        if (ModContent.GetInstance<RoundManager>().CurrentPhase == RoundManager.RoundPhase.Generating)
            maxSpawns = 0;
    }
}
