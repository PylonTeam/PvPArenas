using Terraria.ModLoader;

namespace PvPArenas.Common.AdminTools.WorldGenManager;

public class NativeWorldGenLoaderSystem : ModSystem {
	public override void Load() {
		// The optional C++ generator is only used by /wgtest.
		if (!NativeLibraryLoader.TryLoad(Mod, out string error)) {
			Mod.Logger.Warn(error + " Only /wgtest is disabled; Arenas and the vanilla World Gen Manager remain available.");
		}
	}

	public override void Unload() {
		NativeLibraryLoader.Unload();
	}
}
