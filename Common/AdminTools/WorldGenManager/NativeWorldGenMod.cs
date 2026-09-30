using Terraria.ModLoader;
using PvPArenas.Common.Generation;

namespace PvPArenas.Common.AdminTools.WorldGenManager;

public class NativeWorldGenLoaderSystem : ModSystem {
	public override void Unload() {
		ModContent.GetInstance<ArenaPreparation>()?.Cancel();
		ModContent.GetInstance<WorldGenPassRunner>()?.Shutdown();
		NativeLibraryLoader.Unload();
	}
}
