using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.WorldBuilding;

namespace PvPArenas.Common.AdminTools.WorldGenManager;

public static class NativeLibraryLoader {
	private static readonly object Gate = new();
	// Framework-owned state survives mod reloads without keeping a mod assembly rooted.
	private static readonly int[] PendingCleanup = GetCleanupCounter();
	private static IntPtr _handle;
	private static bool _resolverInstalled;
	private static bool _unloadRequested;
	private static int _activeSessions;
	// The original API 1.0 build emitted AVX-512 even in its session constructor.
	private const string Avx512Build = "54DABF6CE366A4C1444822C0A742C7B33A7BACAA35C8F981B0C4760DE353428F";
	public static bool IsLoaded => _handle != IntPtr.Zero;
	public static string Error { get; private set; }
	internal static int PendingCleanupCount => Volatile.Read(ref PendingCleanup[0]);
	internal static int ActiveSessionCount { get { lock (Gate) return _activeSessions; } }
	private const string PendingCleanupError = "Previous WorldGen++ work is still stopping. Wait for it to finish; restart the server if it remains stuck.";

	public static bool TryLoad(Mod mod, out string error) {
		lock (Gate) return TryLoadCore(mod, out error);
	}

	private static bool TryLoadCore(Mod mod, out string error) {
		if (PendingCleanupCount != 0 || _unloadRequested) {
			Error = error = PendingCleanupError;
			return false;
		}
		if (IsLoaded) { Error = error = null; return true; }
		try {
			if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
				throw new PlatformNotSupportedException("WorldGen++ requires Windows x64.");
			const string file = "WorldGen++_x64.dll";
			string resource = "lib/" + file;
			if (!mod.FileExists(resource))
				throw new FileNotFoundException("WorldGen++ DLL is missing from the mod.");
			byte[] bytes = mod.GetFileBytes(resource);
			string hash = Convert.ToHexString(SHA256.HashData(bytes));
			string compatibilityError = GetCompatibilityError(hash);
			if (compatibilityError != null) throw new PlatformNotSupportedException(compatibilityError);
			string directory = Path.Combine(Path.GetTempPath(), mod.Name, "WorldGen", hash);
			Directory.CreateDirectory(directory);
			string path = Path.Combine(directory, file);
			if (!File.Exists(path))
				File.WriteAllBytes(path, bytes);
			if (!_resolverInstalled) {
				NativeLibrary.SetDllImportResolver(typeof(Native).Assembly,
					(name, _, _) => name == Native.Library ? _handle : IntPtr.Zero);
				_resolverInstalled = true;
			}
			_handle = NativeLibrary.Load(path);
			NativeWorldGenSession.VerifyAbi();
			Error = error = null;
			return true;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DllNotFoundException
			or BadImageFormatException or EntryPointNotFoundException or InvalidOperationException or PlatformNotSupportedException) {
			Unload();
			Error = error = exception.Message;
			return false;
		}
	}

	internal static void ThrowIfUnavailable() {
		lock (Gate) {
			if (PendingCleanupCount != 0 || _unloadRequested)
				throw new InvalidOperationException(PendingCleanupError);
			if (!IsLoaded) throw new InvalidOperationException("WorldGen++ is not loaded.");
		}
	}

	internal static void AcquireSession() {
		lock (Gate) {
			ThrowIfUnavailable();
			_activeSessions++;
		}
	}

	internal static void ReleaseSession() {
		lock (Gate) {
			_activeSessions--;
			if (_activeSessions == 0 && _unloadRequested) FreeLibrary();
		}
	}

	internal static void BeginCleanup() => Interlocked.Increment(ref PendingCleanup[0]);
	internal static void EndCleanup() => Interlocked.Decrement(ref PendingCleanup[0]);

	private static int[] GetCleanupCounter() {
		const string key = "PvPArenas.WorldGen.PendingCleanup";
		lock (AppDomain.CurrentDomain) {
			if (AppDomain.CurrentDomain.GetData(key) is int[] counter) return counter;
			int[] created = [0];
			AppDomain.CurrentDomain.SetData(key, created);
			return created;
		}
	}

	internal static string GetCompatibilityError(string hash) =>
		hash == Avx512Build && (!Avx512F.IsSupported || !Avx512BW.IsSupported || !Avx512DQ.IsSupported || !Avx512CD.IsSupported)
			? "This WorldGen++ DLL needs AVX-512. Ask cactus for a Windows x64 build without AVX-512."
			: null;

	public static void Unload() {
		lock (Gate) {
			_unloadRequested = true;
			if (_activeSessions == 0) FreeLibrary();
		}
	}

	private static void FreeLibrary() {
		_unloadRequested = false;
		if (_handle == IntPtr.Zero) return;
		NativeLibrary.Free(_handle);
		_handle = IntPtr.Zero;
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
	public static extern int WG_GetPassCount(IntPtr session);

	[DllImport(Library, CallingConvention = Cdecl)]
	public static extern int WG_GetPassName(IntPtr session, int index, byte[] buffer, int bufferSize);

	[DllImport(Library, CallingConvention = Cdecl)]
	public static extern int WG_RunPassList(IntPtr session, IntPtr[] names, int count);

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
	private readonly int _width, _height;
	private WgProgressCallback _callback;
	private Exception _callbackError;
	private GCHandle[] _pins;
	private Array[] _sourceTiles, _tiles;
	private Dictionary<string, int> _fieldIndex;
	private Dictionary<string, double> _worldFields;
	private string[] _passNames;
	private bool _initialized;
	private int _cleanupScheduled;
	private int _stride;
	private HashSet<(int X, int Y)> _seenChests = [];

	public bool IsValid => _handle != IntPtr.Zero;
	private IntPtr Handle => IsValid ? _handle : throw new ObjectDisposedException(nameof(NativeWorldGenSession));
	public IReadOnlyList<string> PassNames => _passNames ??= Enumerable.Range(0, PassCount).Select(GetPassName).ToArray();
	public int PassCount => Native.WG_GetPassCount(Handle);
	public int ChestCount => Native.GetChestCount(Handle);

	public NativeWorldGenSession(int seed, int sizeClass, int width, int height, int evil, int gameMode, WgProgressCallback callback = null) {
		if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
		_width = width;
		_height = height;
		// Exceptions must never unwind across the native callback boundary.
		_callback = (user, task, progress, pass, message) => {
			try { callback?.Invoke(user, task, progress, pass, message); }
			catch (Exception exception) { _callbackError = exception; }
		};
		var desc = new WgSessionDesc {
			Seed = seed, SizeClass = sizeClass, Width = width, Height = height, Evil = evil, GameMode = gameMode,
			ProgressCallback = Marshal.GetFunctionPointerForDelegate(_callback)
		};
		NativeLibraryLoader.AcquireSession();
		try {
			_handle = Native.WG_CreateSession(ref desc);
			if (!IsValid) throw new InvalidOperationException("WorldGen++ could not create a session.");
		}
		catch {
			NativeLibraryLoader.ReleaseSession();
			throw;
		}
	}

	// Capture on the main thread before each job; retained sessions reuse their pinned working arrays.
	public void BindTileArrays() {
		_ = Handle;
		if (_width != Main.maxTilesX || _height != Main.maxTilesY)
			throw new InvalidOperationException("Session and loaded world dimensions must match.");
		try {
			Array[] current = CurrentTiles();
			if (_pins == null) {
				_tiles = current.Select(array => (Array)array.Clone()).ToArray();
				_pins = new GCHandle[_tiles.Length];
				_stride = Main.tile.Height;
				for (int i = 0; i < _pins.Length; i++) _pins[i] = GCHandle.Alloc(_tiles[i], GCHandleType.Pinned);
				var buffers = new WgTmlBuffers {
					TileType = _pins[0].AddrOfPinnedObject(), WallType = _pins[1].AddrOfPinnedObject(),
					Liquid = _pins[2].AddrOfPinnedObject(), Brightness = _pins[3].AddrOfPinnedObject(),
					State = _pins[4].AddrOfPinnedObject(), Stride = _stride,
					Width = _width, Height = _height
				};
				Check(Native.WG_SetTmlBuffers(Handle, ref buffers), "Bind tiles");
			}
			else {
				if (Main.tile.Height != _stride || current.Where((array, i) => array.Length != _tiles[i].Length).Any())
					throw new InvalidOperationException("The loaded tilemap dimensions changed between generation jobs.");
				for (int i = 0; i < current.Length; i++) Array.Copy(current[i], _tiles[i], current[i].Length);
			}
			_sourceTiles = current;
			_worldFields = new Dictionary<string, double>(StringComparer.Ordinal);
			foreach (string name in Fields.Keys) {
				MemberInfo member = ImportWorldMember(name);
				object value = member is FieldInfo field ? field.GetValue(null) : (member as PropertyInfo)?.GetValue(null);
				if (value != null && (value.GetType().IsPrimitive || value.GetType().IsEnum))
					_worldFields[name] = Convert.ToDouble(value);
			}
		}
		catch { Dispose(); throw; }
	}

	public void Initialize() {
		if (_initialized) throw new InvalidOperationException("Session is already initialized.");
		Check(Native.WG_Initialize(Handle), "Initialize");
		_initialized = true;
	}

	public void SyncFromTml() => Check(Native.WG_SyncFromTml(Handle, 0, 0, _width, _height), "Import tiles");
	public void SyncToTml() => Check(Native.WG_SyncToTml(Handle, 0, 0, _width, _height), "Export tiles");

	// Commit only after generation completes, on the server's main thread and in the same loaded world.
	public void CommitTiles() => CommitTiles(new Rectangle(0, 0, _width, _height), Rectangle.Empty);

	// A small region can be published each tick while leaving the occupied lobby intact.
	internal void CommitTiles(Rectangle bounds, Rectangle excluded) {
		_ = Handle;
		if (Main.netMode != NetmodeID.Server)
			throw new InvalidOperationException("Only the server can publish generated tiles.");
		Array[] current = CurrentTiles();
		if (_tiles == null || _width != Main.maxTilesX || _height != Main.maxTilesY ||
			current.Where((array, i) => !ReferenceEquals(array, _sourceTiles[i])).Any())
			throw new InvalidOperationException("The loaded tilemap changed during generation.");
		if (bounds.Left < 0 || bounds.Top < 0 || bounds.Right > _width || bounds.Bottom > _height ||
			bounds.Width < 0 || bounds.Height < 0)
			throw new ArgumentOutOfRangeException(nameof(bounds));
		for (int x = bounds.Left; x < bounds.Right; x++) {
			int skipStart = Math.Max(bounds.Top, excluded.Top), skipEnd = Math.Min(bounds.Bottom, excluded.Bottom);
			if (x >= excluded.Left && x < excluded.Right && skipStart < skipEnd) {
				Copy(bounds.Top, skipStart);
				Copy(skipEnd, bounds.Bottom);
			}
			else Copy(bounds.Top, bounds.Bottom);

			void Copy(int start, int end) {
				int index = x * _stride + start;
				for (int i = 0; i < current.Length; i++) Array.Copy(_tiles[i], index, current[i], index, end - start);
			}
		}
	}

	private static Array[] CurrentTiles() => [
		Main.tile.GetData<TileTypeData>(), Main.tile.GetData<WallTypeData>(), Main.tile.GetData<LiquidData>(),
		Main.tile.GetData<TileWallBrightnessInvisibilityData>(), Main.tile.GetData<TileWallWireStateData>()
	];

	public WgResult RunPass(string name) => RunPasses([name]);

	public WgResult RunPasses(IReadOnlyList<string> passNames) {
		if (!_initialized) throw new InvalidOperationException("Initialize the session before running passes.");
		if (passNames == null || passNames.Count == 0) return WgResult.Ok;
		foreach (string name in passNames)
			if (!PassNames.Contains(name, StringComparer.Ordinal)) throw new ArgumentException($"Unknown native pass '{name}'.");
		var pointers = new IntPtr[passNames.Count];
		try {
			for (int i = 0; i < pointers.Length; i++) pointers[i] = Marshal.StringToCoTaskMemUTF8(passNames[i]);
			WgResult result = (WgResult)Native.WG_RunPassList(Handle, pointers, pointers.Length);
			if (_callbackError != null) throw new InvalidOperationException("Generation progress callback failed.", _callbackError);
			return result;
		}
		finally {
			foreach (IntPtr pointer in pointers)
				if (pointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pointer);
			GC.KeepAlive(_callback);
		}
	}

	public void RequestCancel() { if (IsValid) Native.WG_RequestCancel(_handle); }

	public string GetPassName(int index) {
		var buffer = new byte[256];
		int needed = Native.WG_GetPassName(Handle, index, buffer, buffer.Length);
		if (needed <= 0 || needed > buffer.Length) throw new InvalidOperationException($"Invalid native pass at index {index}.");
		return CString(buffer);
	}

	private Dictionary<string, int> Fields {
		get {
			if (_fieldIndex != null) return _fieldIndex;
			var fields = new Dictionary<string, int>(StringComparer.Ordinal);
			int count = Native.WG_GetFieldCount();
			if (count < 0 || count > 4096) throw new InvalidOperationException("Invalid native field table.");
			for (int i = 0; i < count; i++) {
				var buffer = new byte[256];
				int needed = Native.WG_GetFieldName(i, buffer, buffer.Length);
				if (needed <= 0 || needed > buffer.Length) throw new InvalidOperationException($"Invalid native field at index {i}.");
				fields.Add(CString(buffer), i);
			}
			return _fieldIndex = fields;
		}
	}

	public double GetField(string name) {
		Check(Native.WG_GetField(Handle, Fields[name], out double value), "Read " + name);
		return value;
	}

	public void SetField(string name, double value) => Check(Native.WG_SetField(Handle, Fields[name], value), "Write " + name);

	// Uses the snapshot captured by BindTileArrays; safe to call on the worker after Initialize/Reset.
	public void CopyWorldFieldsToNative() {
		if (_worldFields == null) throw new InvalidOperationException("Capture tile buffers before importing world fields.");
		foreach ((string name, double value) in _worldFields)
			SetField(name, value);
	}

	// Saved worlds do not retain GenVars. Keep native generation state, deriving only its two layer anchors.
	private static MemberInfo ImportWorldMember(string name) => name switch {
		"GenWorldSurface" => WorldMember("WorldSurface"),
		"GenRockLayer" => WorldMember("RockLayer"),
		"MaxTilesX" or "MaxTilesY" or "ForceEvilType" or "NeonMossType" => null,
		_ => name.StartsWith("Gen", StringComparison.Ordinal) ? null : WorldMember(name)
	};

	// The DLL exposes scalar metadata only; its internal structure lists cannot be transferred.
	public void ApplyWorldFields() {
		var values = new List<(MemberInfo Member, object Value)>();
		foreach (string name in Fields.Keys) {
			if (name is "MaxTilesX" or "MaxTilesY" or "WorldID" or "GameMode") continue;
			MemberInfo member = WorldMember(name);
			Type type = member is FieldInfo field ? field.FieldType : (member as PropertyInfo)?.PropertyType;
			if (type == null || !(type.IsPrimitive || type.IsEnum) ||
				member is FieldInfo { IsInitOnly: true } || member is PropertyInfo { CanWrite: false }) continue;
			double value = GetField(name);
			if (!double.IsFinite(value)) throw new InvalidOperationException("Invalid generated field " + name);
			object converted = type.IsEnum ? Enum.ToObject(type, (int)value) : Convert.ChangeType(value, type);
			values.Add((member, converted));
		}
		foreach ((MemberInfo member, object value) in values) {
			if (member is FieldInfo field) field.SetValue(null, value);
			else ((PropertyInfo)member).SetValue(null, value);
		}
	}

	private static MemberInfo WorldMember(string name) {
		const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
		bool generation = name.StartsWith("Gen", StringComparison.Ordinal);
		string memberName = name switch {
			"GenNumMountCaves" => "numMCaves",
			"ForceEvilType" => "WorldGenParam_Evil",
			_ => generation ? name[3..] : name
		};
		Type owner = generation ? typeof(GenVars) : typeof(Main);
		return (MemberInfo)owner.GetField(memberName, flags) ?? owner.GetProperty(memberName, flags) ??
			(MemberInfo)typeof(WorldGen).GetField(memberName, flags);
	}

	// Construct and validate the entire new chest table before replacing any live entry.
	public int ImportChests(bool replaceExisting = false) {
		int count = ChestCount;
		if (count < 0 || count > Main.chest.Length) throw new InvalidOperationException($"Invalid generated chest count: {count}.");
		var incoming = new WgChest[count];
		int written = count == 0 ? 0 : Native.GetChests(Handle, incoming, count);
		if (written != count) throw new InvalidOperationException($"Native chest transfer failed ({written}/{count}).");
		return MergeChests(incoming, replaceExisting);
	}

	private int MergeChests(WgChest[] incoming, bool replaceExisting) {
		Chest[] next = replaceExisting ? new Chest[Main.chest.Length] : (Chest[])Main.chest.Clone();
		for (int i = 0; i < next.Length; i++)
			if (next[i] is Chest old && !HasChestTile(old.x, old.y)) next[i] = null;
		var positions = new HashSet<(int, int)>();
		var seen = replaceExisting ? new HashSet<(int, int)>() : new HashSet<(int, int)>(_seenChests);
		int imported = 0;
		foreach (WgChest data in incoming) {
			var position = (data.X, data.Y);
			if (!positions.Add(position)) {
				if (replaceExisting) throw new InvalidOperationException("Duplicate generated chest position.");
				continue;
			}
			int existing = Array.FindIndex(next, chest => chest != null && chest.x == data.X && chest.y == data.Y);
			// A retained native record is not authoritative for chests players edited or removed.
			if (!replaceExisting && (existing >= 0 || !seen.Add(position) || !HasChestTile(data.X, data.Y))) {
				seen.Add(position);
				continue;
			}
			if (!HasChestTile(data.X, data.Y) || data.Items?.Length != WgChest.MaxItems)
				throw new InvalidOperationException("Invalid generated chest position or contents.");
			int index = Array.FindIndex(next, chest => chest == null);
			if (index < 0) throw new InvalidOperationException("No free chest slots.");
			var chest = new Chest { x = data.X, y = data.Y, item = new Item[Chest.maxItems] };
			for (int slot = 0; slot < chest.item.Length; slot++) {
				WgChestSlot dataItem = data.Items[slot];
				if (dataItem.Type < 0 || dataItem.Type >= ItemLoader.ItemCount || dataItem.Stack < 0 || dataItem.Prefix < 0 || dataItem.Prefix > byte.MaxValue)
					throw new InvalidOperationException("Invalid generated chest item.");
				var item = new Item();
				if (dataItem.Type > 0 && dataItem.Stack > 0) {
					item.SetDefaults(dataItem.Type);
					item.stack = Math.Min(dataItem.Stack, item.maxStack);
					if (dataItem.Prefix > 0) item.Prefix(dataItem.Prefix);
				}
				chest.item[slot] = item;
			}
			next[index] = chest;
			seen.Add(position);
			imported++;
		}
		Array.Copy(next, Main.chest, next.Length);
		_seenChests = seen;
		return imported;
	}

	private static bool HasChestTile(int x, int y) =>
		x >= 0 && y >= 0 && x < Main.maxTilesX && y < Main.maxTilesY &&
		Main.tile[x, y].HasTile && TileID.Sets.BasicChest[Main.tile[x, y].TileType];

	public static void VerifyAbi(int expectedMajor = 1) {
		int major = Native.WG_GetApiVersionMajor();
		if (major != expectedMajor) throw new InvalidOperationException($"WorldGen++ API {major} is unsupported; expected {expectedMajor}.");
		// API 1.1 removed IndexMode from WgTmlBuffers without changing its padded size.
		if (Native.WG_GetApiVersionMinor() < 1)
			throw new InvalidOperationException("WorldGen++ API 1.1 or newer is required for the current tile buffer layout.");
		if (Marshal.SizeOf<WgSessionDesc>() != 48 || Marshal.SizeOf<WgTmlBuffers>() != 56 ||
			Marshal.OffsetOf<WgTmlBuffers>(nameof(WgTmlBuffers.Stride)).ToInt32() != 40 ||
			Marshal.OffsetOf<WgTmlBuffers>(nameof(WgTmlBuffers.Width)).ToInt32() != 44 ||
			Marshal.OffsetOf<WgTmlBuffers>(nameof(WgTmlBuffers.Height)).ToInt32() != 48 ||
			Marshal.SizeOf<TileTypeData>() != 2 || Marshal.SizeOf<WallTypeData>() != 2 ||
			Marshal.SizeOf<LiquidData>() != 2 || Marshal.SizeOf<TileWallBrightnessInvisibilityData>() != 1 ||
			Marshal.SizeOf<TileWallWireStateData>() != 8 || Marshal.SizeOf<WgChest>() != 488)
			throw new InvalidOperationException("WorldGen++ and tModLoader tile layouts do not match.");
	}

	private static string CString(byte[] buffer) {
		int length = Array.IndexOf(buffer, (byte)0);
		return Encoding.UTF8.GetString(buffer, 0, length < 0 ? buffer.Length : length);
	}

	private static void Check(int result, string operation) {
		if (result != (int)WgResult.Ok) throw new InvalidOperationException($"{operation} failed ({(WgResult)result}).");
	}

	// A native pass may ignore cancellation. Keep its buffers and DLL alive without blocking the server.
	internal void DisposeWhenCompleted(Task worker) {
		if (Interlocked.Exchange(ref _cleanupScheduled, 1) != 0) return;
		if (worker == null || worker.IsCompleted) {
			_ = worker?.Exception;
			Dispose();
			return;
		}
		NativeLibraryLoader.BeginCleanup();
		var logger = Log.Base;
		_ = Task.Run(async () => {
			try { RequestCancel(); }
			catch (Exception exception) { logger.Warn("WorldGen++ cancellation request failed: " + exception.Message); }
			try { await worker.ConfigureAwait(false); }
			catch (Exception) { } // Observe a cancelled/failed worker; its job already owns user-facing diagnostics.
			finally {
				try { Dispose(); }
				catch (Exception exception) { logger.Error("WorldGen++ session cleanup failed.", exception); }
				finally { NativeLibraryLoader.EndCleanup(); }
			}
		});
	}

	public void Dispose() {
		// The worker owner must use DisposeWhenCompleted while a worker can still access this session.
		IntPtr handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
		if (handle == IntPtr.Zero) return;
		try { Native.WG_DestroySession(handle); }
		finally {
			if (_pins != null) foreach (GCHandle pin in _pins) if (pin.IsAllocated) pin.Free();
			_pins = null;
			_tiles = _sourceTiles = null;
			_callback = null;
			GC.SuppressFinalize(this);
			NativeLibraryLoader.ReleaseSession();
		}
	}
}
