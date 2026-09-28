using Microsoft.Xna.Framework;
using PvPArenas.Common.AdminTools.WorldGenManager;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace VanillaWorldGenCPP {

	//   /wgtest vec				run the WHOLE pass table as a vector
	//   /wgtest vec a, b, c		run only these passes, in order, as a vector
	//   Options: seed:123 size:8400x2400
	public class WorldGenTestCommand : ModCommand {
		public override CommandType Type => CommandType.Chat;
		public override string Command => "wgtest";
		public override string Description => "Native world generator test (safe | vec [names]) (seed:N size:WxH)";

		public override void Action(CommandCaller caller, string input, string[] args) {
			if (!NativeLibraryLoader.TryLoad(Mod, out string error)) {
				caller.Reply(error, Color.OrangeRed);
				return;
			}

			int seed = GetSeed(args, 100);
			GetSize(args, out int width, out int height, out bool custom);
			int sizeClass = GetSizeClass();

			if (args.Length > 0 && string.Equals(args[0], "vec", StringComparison.OrdinalIgnoreCase)) {
				RunVectorAsync(caller, args, seed, width, height, sizeClass);
				return;
			}

			bool safe = args.Length > 0 && string.Equals(args[0], "safe", StringComparison.OrdinalIgnoreCase);
			RunFullTest(caller, safe, seed, width, height, sizeClass);
		}

		private void RunVectorAsync(CommandCaller caller, string[] args, int seed, int width, int height, int sizeClass) {
			var parts = new List<string>();
			for (int i = 1; i < args.Length; i++) {
				if (!IsOptionToken(args[i])) {
					parts.Add(args[i]);
				}
			}

			var requested = new List<string>();
			foreach (string part in string.Join(" ", parts).Split(',')) {
				string name = part.Trim();
				if (name.Length > 0) {
					requested.Add(name);
				}
			}

			Mod.Logger.Info($"wgtest vec starting. seed={seed} size={width}x{height} requested={requested.Count}");
			caller.Reply($"[C++] Starting background world generation task (seed: {seed}, size: {width}x{height})...", Color.Yellow);

			// not on main thread
			Task.Run(() => {
				WgProgressCallback log = (userData, taskId, progress, pass, message) => {
					string name = message == IntPtr.Zero ? "?" : Marshal.PtrToStringUTF8(message);
					Mod.Logger.Info($"wgtest vec running pass {pass}: {name}");
				};

				NativeWorldGenSession session = null;
				try {
					session = new NativeWorldGenSession(seed, sizeClass, width, height, GetEvil(), Main.GameMode, log);

					session.BindTileArrays();

					Mod.Logger.Info("wgtest vec >>> Initialize");
					session.Initialize();
					Mod.Logger.Info("wgtest vec <<< Initialize ok");

					var allNames = new List<string>();
					var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
					int count = session.PassCount;
					for (int i = 0; i < count; i++) {
						string name = session.GetPassName(i);
						if (string.IsNullOrWhiteSpace(name)) {
							continue;
						}
						allNames.Add(name);
						lookup[name] = name;
					}

					if (requested.Count == 0) {
						requested = allNames;
					}

					var valid = new List<string>();
					foreach (string name in requested) {
						if (lookup.TryGetValue(name, out string canonical)) {
							valid.Add(canonical);
						}
						else {
							Mod.Logger.Warn($"wgtest vec: '{name}' is not a pass name, skipping");
						}
					}

					if (valid.Count == 0) {
						Main.QueueMainThreadAction(() => {
							caller.Reply("No valid pass names found.", Color.OrangeRed);
						});
						return;
					}

					Mod.Logger.Info($"wgtest vec >>> RunPasses ({valid.Count} of {requested.Count}): {string.Join(" | ", valid)}");

					var sw = System.Diagnostics.Stopwatch.StartNew();

					WgResult result = session.RunPasses(valid);

					sw.Stop();
					Mod.Logger.Info($"wgtest vec <<< RunPasses -> {result} ({sw.ElapsedMilliseconds} ms)");

					if (result == WgResult.Ok) {
						Mod.Logger.Info("wgtest vec >>> SyncToTml");
						session.SyncToTml();
						Mod.Logger.Info("wgtest vec <<< SyncToTml ok");
					}

					Main.QueueMainThreadAction(() => {
						if (result == WgResult.Ok) {
							WorldGen.RangeFrame(0, 0, Main.maxTilesX, Main.maxTilesY);

							if (Main.netMode == NetmodeID.SinglePlayer) {
								Lighting.Clear();
							}

							caller.Reply($"[C++] Ran {valid.Count} passes in {sw.ElapsedMilliseconds} ms (seed {seed}) -> {result}", Color.LightGreen);
						}
						else {
							caller.Reply($"[C++] Pass execution failed: {result}", Color.OrangeRed);
						}
					});
				}
				catch (Exception e) {
					Mod.Logger.Error("wgtest vec exception", e);
					Main.QueueMainThreadAction(() => {
						caller.Reply("vec failed: " + e.Message, Color.OrangeRed);
					});
				}
				finally {
					session?.Dispose();
				}
			});
		}

		// ignore
		private void RunFullTest(CommandCaller caller, bool safe, int seed, int width, int height, int sizeClass) {
			Mod.Logger.Info($"wgtest starting. safe={safe} seed={seed} size={width}x{height} loaded={NativeLibraryLoader.IsLoaded}");

			var report = new StringBuilder();
			bool ok = true;

			void Step(string name, Action body) {
				Mod.Logger.Info("wgtest >>> " + name);
				try {
					body();
					Mod.Logger.Info("wgtest <<< ok: " + name);
					report.AppendLine("[ok]   " + name);
				}
				catch (Exception e) {
					ok = false;
					Mod.Logger.Error("wgtest <<< FAIL: " + name, e);
					report.AppendLine("[FAIL] " + name + ": " + e.Message);
				}
			}

			NativeWorldGenSession session = null;
			try {
				Step("VerifyAbi", () => NativeWorldGenSession.VerifyAbi());

				Step("Create session", () => {
					session = new NativeWorldGenSession(seed, sizeClass, width, height, GetEvil(), Main.GameMode, null);
				});

				if (session == null) {
					Finish(caller, report, false);
					return;
				}

				Step("Bind tile arrays", () => session.BindTileArrays());

				Step("Field read (MaxTilesX)", () => {
					int value = (int)session.GetField("MaxTilesX");
					if (value != width) {
						throw new Exception("expected " + width + ", got " + value);
					}
				});

				Step("Field write round trip (MoonType)", () => {
					session.SetField("MoonType", 3);
					int value = (int)session.GetField("MoonType");
					if (value != 3) {
						throw new Exception("wrote 3, read " + value);
					}
				});

				Step("Pass discovery", () => {
					int count = session.PassCount;
					if (count <= 0) {
						throw new Exception("pass count was " + count);
					}
					report.AppendLine("       (" + count + " passes, first is '" + session.GetPassName(0) + "')");
				});

				if (!safe) {
					Step("Initialize (native gen)", () => session.Initialize());

					Step("Run first pass (native gen)", () => {
						WgResult result = session.RunPassRange(0, 1);
						if (result != WgResult.Ok) {
							throw new Exception(result.ToString());
						}
						session.SyncToTml();
					});
				}
				else {
					report.AppendLine("[skip] Initialize and Run pass (safe mode)");
				}

				Step("Chest count", () => {
					int count = session.ChestCount;
					if (count < 0) {
						throw new Exception("returned " + count);
					}
					report.AppendLine("       (" + count + " chests)");
				});
			}
			catch (Exception e) {
				ok = false;
				Mod.Logger.Error("wgtest top-level exception", e);
				report.AppendLine("[FAIL] unexpected: " + e.Message);
			}
			finally {
				Mod.Logger.Info("wgtest >>> Dispose session");
				session?.Dispose();
				Mod.Logger.Info("wgtest <<< ok: Dispose session");
			}

			Finish(caller, report, ok);
		}

		private void Finish(CommandCaller caller, StringBuilder report, bool ok) {
			report.AppendLine(ok ? "RESULT: all steps passed" : "RESULT: failures above");
			string text = report.ToString();
			Mod.Logger.Info("wgtest result:\n" + text);
			caller.Reply(text, ok ? Color.LightGreen : Color.OrangeRed);
		}

		private static int GetSeed(string[] args, int fallback) {
			foreach (string arg in args) {
				if (IsSeedToken(arg) && int.TryParse(arg.Substring(5), out int value)) {
					return value;
				}
			}
			return fallback;
		}

		private static bool IsSeedToken(string arg) {
			return arg.StartsWith("seed:", StringComparison.OrdinalIgnoreCase) || arg.StartsWith("seed=", StringComparison.OrdinalIgnoreCase);
		}

		private static bool IsSizeToken(string arg) {
			return arg.StartsWith("size:", StringComparison.OrdinalIgnoreCase) || arg.StartsWith("size=", StringComparison.OrdinalIgnoreCase);
		}

		private static bool IsOptionToken(string arg) {
			return IsSeedToken(arg) || IsSizeToken(arg);
		}

		private static void GetSize(string[] args, out int width, out int height, out bool custom) {
			width = Main.maxTilesX;
			height = Main.maxTilesY;
			custom = false;
			foreach (string arg in args) {
				if (!IsSizeToken(arg)) {
					continue;
				}
				string[] parts = arg.Substring(5).Split('x', 'X');
				if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h) && w > 0 && h > 0) {
					width = w;
					height = h;
					custom = true;
				}
			}
		}

		private static int GetSizeClass() {
			switch (Main.maxTilesX) {
				case 4200: return 0;
				case 6400: return 1;
				case 8400: return 2;
				default: return 3;
			}
		}

		private static int GetEvil() {
			return WorldGen.crimson ? 2 : 1;
		}
	}
}
