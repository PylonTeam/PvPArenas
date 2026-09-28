using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace PvPArenas.Common.AdminTools.WorldGenManager;

/*
session.Initialize();
session.RunPasses(new[] { "Reset" }); // reset sets some initial fields, you can overwrite basics below
session.SetField("DungeonX", 1200); // if you want to change a field (like with the beach side but thats a wip rn)
session.RunPasses(new[] { "Terrain", "Dunes", "Dungeon" });  // other passes
session.Dispose(); // cleanup
*/


public static class NativeLibraryLoader {
		private static IntPtr _handle;
		private static bool _installed;

		public static bool IsLoaded => _handle != IntPtr.Zero;

		public static bool TryLoad(Mod mod, out string error) {
			error = null;
			if (IsLoaded) {
				return true;
			}

			if (RuntimeInformation.ProcessArchitecture != Architecture.X64) {
				error = "Native world generation requires an x64 process.";
				return false;
			}

			string fileName = GetPlatformFileName();
			if (fileName == null) {
				error = "Native world generation is not supported on this operating system.";
				return false;
			}

			string resourcePath = "lib/" + fileName;
			if (!mod.FileExists(resourcePath)) {
				error = $"Native world generation is unavailable: '{resourcePath}' is not bundled in {mod.Name}.";
				return false;
			}

			try {
				string path = Path.Combine(Path.GetTempPath(), mod.Name + "_" + mod.Version, fileName);
				Directory.CreateDirectory(Path.GetDirectoryName(path));

				using (Stream source = mod.GetFileStream(resourcePath))
				using (FileStream target = File.Create(path)) {
					source.CopyTo(target);
				}

				if (!_installed) {
					NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), Resolve);
					_installed = true;
				}
				_handle = NativeLibrary.Load(path);
				mod.Logger.Info("Loaded native library from " + path);
				return true;
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DllNotFoundException or BadImageFormatException) {
				error = $"Native world generation is unavailable: could not load '{resourcePath}': {exception.Message}";
				return false;
			}
		}

		private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath) {
			return libraryName == Native.Library ? _handle : IntPtr.Zero;
		}

		private static string GetPlatformFileName() {
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
				return "WorldGen++_x64.dll";
			}
			if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
				return "libWorldGen++_x64.dylib";
			}
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
				return "libWorldGen++_x64.so";
			}
			return null;
		}

		public static void Unload() {
			if (_handle != IntPtr.Zero) {
				NativeLibrary.Free(_handle);
				_handle = IntPtr.Zero;
			}
			// The resolver stays registered for this assembly and uses the current handle.
		}
	}

	public enum WgResult {
		Ok = 0,
		InvalidSession = -1,
		InvalidArgument = -2,
		NotInitialized = -3,
		UnknownPass = -4,
		PassFailed = -5,
		Cancelled = -6,
		SaveFailed = -7,
		Unsupported = -8
	}

	public enum TmlIndexMode {
		ColumnMajor = 0,
		RowMajor = 1
	}

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void WgProgressCallback(IntPtr userData, int taskId, float progress, int pass, IntPtr message);

	[StructLayout(LayoutKind.Sequential)]
	internal struct WgSessionDesc {
		public int Seed;
		public int TaskId;
		public int SizeClass;
		public int Width;
		public int Height;
		public int Evil;
		public int GameMode;
		public int Reserved;
		public IntPtr ProgressCallback;
		public IntPtr ProgressUserData;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct WgTmlBuffers {
		public IntPtr TileType;
		public IntPtr WallType;
		public IntPtr Liquid;
		public IntPtr Brightness;
		public IntPtr State;
		public int Stride;
		public int IndexMode;
		public int Width;
		public int Height;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct WgChestSlot {
		public int Type;
		public int Stack;
		public int Prefix;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct WgChest {
		public const int MaxItems = 40;

		public int X;
		public int Y;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxItems)]
		public WgChestSlot[] Items;
	}

	internal static class Native {
		public const string Library = "WorldGenNative";
		private const CallingConvention Cdecl = CallingConvention.Cdecl;

		[DllImport(Library, CallingConvention = Cdecl)]
		public static extern int WG_GetApiVersionMajor();

		[DllImport(Library, CallingConvention = Cdecl)]
		public static extern int WG_GetApiVersionMinor();

		[DllImport(Library, CallingConvention = Cdecl)]
		public static extern IntPtr WG_CreateSession(ref WgSessionDesc desc);

		[DllImport(Library, CallingConvention = Cdecl)]
		public static extern void WG_DestroySession(IntPtr session);

		[DllImport(Library, CallingConvention = Cdecl)]
		public static extern void WG_RequestCancel(IntPtr session);

		[DllImport(Library, CallingConvention = Cdecl)]
		public static extern int WG_SetTmlBuffers(IntPtr session, ref WgTmlBuffers buffers);

		[DllImport(Library, CallingConvention = Cdecl)]
		public static extern int WG_SyncFromTml(IntPtr session, int startX, int startY, int endX, int endY);

		[DllImport(Library, CallingConvention = Cdecl)]
		public static extern int WG_SyncToTml(IntPtr session, int startX, int startY, int endX, int endY);

		[DllImport(Library, CallingConvention = Cdecl)]
		public static extern int WG_Initialize(IntPtr session);

		[DllImport(Library, CallingConvention = Cdecl)]
		public static extern int WG_RunAllPasses(IntPtr session);

		[DllImport(Library, CallingConvention = Cdecl)]
		public static extern int WG_GenerateWorld(IntPtr session);

		[DllImport(Library, CallingConvention = Cdecl)]
		public static extern int WG_SaveWorld(IntPtr session, [MarshalAs(UnmanagedType.LPUTF8Str)] string directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string worldName);

		[DllImport(Library, CallingConvention = Cdecl)]
		public static extern int WG_GetPassCount(IntPtr session);

		[DllImport(Library, CallingConvention = Cdecl)]
		public static extern int WG_GetPassName(IntPtr session, int index, byte[] buffer, int bufferSize);

		[DllImport(Library, CallingConvention = Cdecl)]
		public static extern int WG_GetPassIndex(IntPtr session, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

		[DllImport(Library, CallingConvention = Cdecl)]
		public static extern int WG_RunPassByName(IntPtr session, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

		[DllImport(Library, CallingConvention = Cdecl)]
		public static extern int WG_RunPassList(IntPtr session, IntPtr[] names, int count);

		[DllImport(Library, CallingConvention = Cdecl)]
		public static extern int WG_RunPassRange(IntPtr session, int startIndex, int count);

		[DllImport(Library, CallingConvention = Cdecl)]
		public static extern int WG_SetPassEnabled(IntPtr session, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int enabled);

		[DllImport(Library, CallingConvention = Cdecl)]
		public static extern int WG_SetAllPassesEnabled(IntPtr session, int enabled);

		[DllImport(Library, CallingConvention = Cdecl)]
		public static extern int WG_GetFieldCount();

		[DllImport(Library, CallingConvention = Cdecl)]
		public static extern int WG_GetFieldName(int field, byte[] buffer, int bufferSize);

		[DllImport(Library, CallingConvention = Cdecl)]
		public static extern int WG_GetField(IntPtr session, int field, out double value);

		[DllImport(Library, CallingConvention = Cdecl)]
		public static extern int WG_SetField(IntPtr session, int field, double value);

		[DllImport(Library, CallingConvention = Cdecl)]
		public static extern int GetChestCount(IntPtr session);

		[DllImport(Library, CallingConvention = Cdecl)]
		public static extern int GetChests(IntPtr session, [Out] WgChest[] buffer, int maxChests);
	}

	public sealed class NativeWorldGenSession : IDisposable {
		private IntPtr _handle;
		private GCHandle[] _pins;
		private WgProgressCallback _callback;
		private Dictionary<string, int> _fieldIndex;
		private WgChest[] _chestBuffer;

		public bool IsValid => _handle != IntPtr.Zero;

		public NativeWorldGenSession(int seed, int sizeClass, int width, int height, int evil, int gameMode, WgProgressCallback callback = null) {
			_callback = callback;

			var desc = new WgSessionDesc {
				Seed = seed,
				TaskId = 0,
				SizeClass = sizeClass,
				Width = width,
				Height = height,
				Evil = evil,
				GameMode = gameMode,
				ProgressCallback = callback != null ? Marshal.GetFunctionPointerForDelegate(callback) : IntPtr.Zero,
				ProgressUserData = IntPtr.Zero
			};

			_handle = Native.WG_CreateSession(ref desc);
			if (_handle == IntPtr.Zero) {
				throw new InvalidOperationException("WG_CreateSession returned null.");
			}
		}

		public void BindTileArrays() {
			var tileType = Main.tile.GetData<TileTypeData>();
			var wallType = Main.tile.GetData<WallTypeData>();
			var liquid = Main.tile.GetData<LiquidData>();
			var brightness = Main.tile.GetData<TileWallBrightnessInvisibilityData>();
			var state = Main.tile.GetData<TileWallWireStateData>();

			_pins = new GCHandle[5];
			_pins[0] = GCHandle.Alloc(tileType, GCHandleType.Pinned);
			_pins[1] = GCHandle.Alloc(wallType, GCHandleType.Pinned);
			_pins[2] = GCHandle.Alloc(liquid, GCHandleType.Pinned);
			_pins[3] = GCHandle.Alloc(brightness, GCHandleType.Pinned);
			_pins[4] = GCHandle.Alloc(state, GCHandleType.Pinned);

			var buffers = new WgTmlBuffers {
				TileType = _pins[0].AddrOfPinnedObject(),
				WallType = _pins[1].AddrOfPinnedObject(),
				Liquid = _pins[2].AddrOfPinnedObject(),
				Brightness = _pins[3].AddrOfPinnedObject(),
				State = _pins[4].AddrOfPinnedObject(),
				Stride = Main.tile.Height,
				IndexMode = (int)TmlIndexMode.ColumnMajor,
				Width = Main.maxTilesX,
				Height = Main.maxTilesY
			};

			Check(Native.WG_SetTmlBuffers(_handle, ref buffers), nameof(BindTileArrays));
		}

		public void Initialize() {
			Check(Native.WG_Initialize(_handle), nameof(Initialize));
		}

		public void SyncFromTml() {
			Check(Native.WG_SyncFromTml(_handle, 0, 0, Main.maxTilesX, Main.maxTilesY), nameof(SyncFromTml));
		}

		public void SyncToTml() {
			Check(Native.WG_SyncToTml(_handle, 0, 0, Main.maxTilesX, Main.maxTilesY), nameof(SyncToTml));
		}

		public WgResult RunAllPasses() {
			return (WgResult)Native.WG_RunAllPasses(_handle);
		}

		public WgResult RunPasses(IReadOnlyList<string> passNames) {
			if (passNames == null || passNames.Count == 0) {
				return WgResult.Ok;
			}

			var pointers = new IntPtr[passNames.Count];
			try {
				for (int i = 0; i < passNames.Count; i++) {
					pointers[i] = Marshal.StringToCoTaskMemUTF8(passNames[i]);
				}
				return (WgResult)Native.WG_RunPassList(_handle, pointers, pointers.Length);
			}
			finally {
				foreach (IntPtr p in pointers) {
					if (p != IntPtr.Zero) {
						Marshal.FreeCoTaskMem(p);
					}
				}
			}
		}

		public WgResult RunPass(string name) {
			return (WgResult)Native.WG_RunPassByName(_handle, name);
		}

		public WgResult RunPassRange(int startIndex, int count) {
			return (WgResult)Native.WG_RunPassRange(_handle, startIndex, count);
		}

		public void SetPassEnabled(string name, bool enabled) {
			Check(Native.WG_SetPassEnabled(_handle, name, enabled ? 1 : 0), nameof(SetPassEnabled));
		}

		public void SetAllPassesEnabled(bool enabled) {
			Check(Native.WG_SetAllPassesEnabled(_handle, enabled ? 1 : 0), nameof(SetAllPassesEnabled));
		}

		public void RequestCancel() {
			Native.WG_RequestCancel(_handle);
		}

		public void Save(string directory, string worldName) {
			Check(Native.WG_SaveWorld(_handle, directory, worldName), nameof(Save));
		}

		// TODO: Manual for now...
		public double GetField(string name) {
			Check(Native.WG_GetField(_handle, FieldIndex(name), out double value), nameof(GetField));
			return value;
		}

		public void SetField(string name, double value) {
			Check(Native.WG_SetField(_handle, FieldIndex(name), value), nameof(SetField));
		}

		private int FieldIndex(string name) {
			if (_fieldIndex == null) {
				_fieldIndex = new Dictionary<string, int>(StringComparer.Ordinal);
				int count = Native.WG_GetFieldCount();
				var buffer = new byte[128];
				for (int i = 0; i < count; i++) {
					Native.WG_GetFieldName(i, buffer, buffer.Length);
					_fieldIndex[CString(buffer)] = i;
				}
			}
			if (!_fieldIndex.TryGetValue(name, out int index)) {
				throw new ArgumentException($"Unknown field '{name}'.", nameof(name));
			}
			return index;
		}

		public int ImportChests() {
			for (int i = 0; i < Main.chest.Length; i++) {
				Main.chest[i] = null;
			}

			int count = Native.GetChestCount(_handle);
			if (count <= 0) {
				return 0;
			}

			if (_chestBuffer == null || _chestBuffer.Length < count) {
				_chestBuffer = new WgChest[count];
			}

			int written = Native.GetChests(_handle, _chestBuffer, count);
			if (written < 0) {
				throw new InvalidOperationException($"GetChests failed ({written}).");
			}

			int imported = 0;
			for (int i = 0; i < written && i < Main.chest.Length; i++) {
				var chest = new Chest {
					x = _chestBuffer[i].X,
					y = _chestBuffer[i].Y,
					item = new Item[Chest.maxItems]
				};

				for (int slot = 0; slot < Chest.maxItems; slot++) {
					var item = new Item();
					if (slot < WgChest.MaxItems) {
						WgChestSlot incoming = _chestBuffer[i].Items[slot];
						if (incoming.Type > 0 && incoming.Stack > 0) {
							item.SetDefaults(incoming.Type);
							item.stack = incoming.Stack;
							if (incoming.Prefix > 0) {
								item.Prefix(incoming.Prefix);
							}
						}
						else {
							item.SetDefaults(0);
						}
					}
					else {
						item.SetDefaults(0);
					}
					chest.item[slot] = item;
				}

				Main.chest[i] = chest;
				imported++;
			}

			return imported;
		}

		public int PassCount => Native.WG_GetPassCount(_handle);

		public string GetPassName(int index) {
			var buffer = new byte[128];
			int needed = Native.WG_GetPassName(_handle, index, buffer, buffer.Length);
			return needed < 0 ? null : CString(buffer);
		}

		public int GetPassIndex(string name) {
			return Native.WG_GetPassIndex(_handle, name);
		}

		public int ChestCount => Native.GetChestCount(_handle);

		public static void VerifyAbi(int expectedMajor = 1) {
			int major = Native.WG_GetApiVersionMajor();
			if (major != expectedMajor) {
				throw new InvalidOperationException($"Native API major version is {major}, expected {expectedMajor}. Rebuild the library or the mod.");
			}
		}

		private static string CString(byte[] buffer) {
			int length = Array.IndexOf(buffer, (byte)0);
			if (length < 0) {
				length = buffer.Length;
			}
			return System.Text.Encoding.UTF8.GetString(buffer, 0, length);
		}

		private static void Check(int result, string what) {
			if (result != (int)WgResult.Ok) {
				throw new InvalidOperationException($"{what} failed ({(WgResult)result}).");
			}
		}

		public void Dispose() {
			if (_handle != IntPtr.Zero) {
				Native.WG_DestroySession(_handle);
				_handle = IntPtr.Zero;
			}

			if (_pins != null) {
				for (int i = 0; i < _pins.Length; i++) {
					if (_pins[i].IsAllocated) {
						_pins[i].Free();
					}
				}
				_pins = null;
			}

			_callback = null;
			GC.SuppressFinalize(this);
		}

		~NativeWorldGenSession() {
			Dispose();
		}
	}
