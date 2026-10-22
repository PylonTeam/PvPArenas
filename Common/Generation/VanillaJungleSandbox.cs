using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.IO;
using Terraria.Utilities;
using Terraria.WorldBuilding;

namespace PvPArenas.Common.Generation;

/// <summary>
/// Executes the installed Terraria C# passes against a disposable small world. Terraria's
/// Tilemap is only a stride descriptor: its five tile planes and their pinned pointers are
/// process globals, so both the planes and world metadata must be scoped together.
/// No scope may survive an iterator yield, await, or game update.
/// </summary>
internal sealed class VanillaJungleSandbox : IDisposable
{
    internal const int SourceWidth = 4200, SourceHeight = 1800;
    private const BindingFlags StaticFields = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static int activeScope;
    private readonly int seed, ownerThread;
    private readonly int width, height;
    private State scratch;
    private TilePlanes planes;
    private bool disposed;

    internal VanillaJungleSandbox(int seed) : this(seed, SourceWidth, SourceHeight) { }

    internal VanillaJungleSandbox(int seed, int width, int height)
    {
        this.seed = seed;
        this.width = width;
        this.height = height;
        ownerThread = Environment.CurrentManagedThreadId;
        if (WorldGen.VanillaGenPasses.Count == 0) WorldGen.AddGenPasses();
        // Ensure every vanilla plane is registered before capturing its GC handle.
        _ = Main.tile.GetData<TileTypeData>();
        _ = Main.tile.GetData<WallTypeData>();
        _ = Main.tile.GetData<LiquidData>();
        _ = Main.tile.GetData<TileWallBrightnessInvisibilityData>();
        _ = Main.tile.GetData<TileWallWireStateData>();
        Enter();
        State live = null;
        TilePlanes livePlanes = null;
        try
        {
            live = State.Capture();
            livePlanes = TilePlanes.Capture();
            live.ApplyCloned();
            // The ordinary constructor frees the current pins. Detach their data first;
            // livePlanes owns those still-valid pins until the finally block restores them.
            livePlanes.DetachArrays();
            Main.tile = new Tilemap(checked((ushort)width), checked((ushort)height));
            planes = TilePlanes.Capture();
            Initialize();
            scratch = State.Capture();
        }
        finally
        {
            try { livePlanes?.Apply(); }
            finally
            {
                try { live?.Apply(); }
                finally
                {
                    Exit();
                    if (scratch == null) planes?.Dispose();
                }
            }
        }
    }

    internal void Run(string passName, WorldGenProgressLog trace,
        Func<GameConfiguration, GameConfiguration> configure = null)
    {
        if (!WorldGen.VanillaGenPasses.TryGetValue(passName, out GenPass pass))
            throw new ArgumentException($"Vanilla generation pass '{passName}' is unavailable.", nameof(passName));
        trace?.Report("Vanilla: " + passName, 0);
        InScope(() =>
        {
            // This is the same reset policy as Terraria.WorldBuilding.WorldGenerator.
            WorldGen._lastSeed = WorldGen._genRandSeed = seed;
            WorldGen._genRand = new UnifiedRandom(seed);
            Main.rand = new UnifiedRandom(seed);
            bool enabled = pass.Enabled;
            try
            {
                pass.Reset();
                GenerationProgress progress = new();
                GameConfiguration configuration = GenVars.configuration.GetPassConfiguration(pass.Name);
                pass.Apply(progress, configure == null ? configuration : configure(configuration));
            }
            catch (Exception error)
            {
                throw new InvalidOperationException("Vanilla pass failed: " + passName + ".", error);
            }
            finally { if (!enabled) pass.Disable(); }
        });
        trace?.Report("Vanilla: " + passName, 1);
    }

    /// <summary>Copy value data inside this callback. Tile handles must never escape it.</summary>
    internal void Configure(Action configure) => InScope(configure);

    internal void Read(Action read) => InScope(() =>
    {
        // Placement framing must not queue liquid work for a rejected source attempt.
        WorldGen.noLiquidCheck = true;
        read();
    });

    private void InScope(Action action)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Enter();
        State live = null;
        TilePlanes livePlanes = null;
        try
        {
            live = State.Capture();
            livePlanes = TilePlanes.Capture();
            scratch.Apply();
            planes.Apply();
            // Prevent placement routines from emitting packets or updating the live map.
            Main.netMode = NetmodeID.SinglePlayer;
            Main.dedServ = true;
            WorldGen.noMapUpdate = true;
            WorldGen.gen = WorldGen.generatingWorld = WorldGen.noTileActions = true;
            action();
        }
        finally
        {
            try
            {
                if (live != null)
                {
                    scratch = State.Capture();
                    planes = TilePlanes.Capture();
                }
            }
            finally
            {
                try { livePlanes?.Apply(); }
                finally { try { live?.Apply(); } finally { Exit(); } }
            }
        }
    }

    private void Enter()
    {
        if (Environment.CurrentManagedThreadId != ownerThread)
            throw new InvalidOperationException("Vanilla Jungle generation must stay on its owning game thread.");
        if (Interlocked.CompareExchange(ref activeScope, 1, 0) != 0)
            throw new InvalidOperationException("Vanilla Jungle sandbox scopes cannot overlap.");
    }

    private static void Exit() => Volatile.Write(ref activeScope, 0);

    private void Initialize()
    {
        Main.maxTilesX = WorldGen.lastMaxTilesX = width;
        Main.maxTilesY = WorldGen.lastMaxTilesY = height;
        Main.leftWorld = Main.topWorld = 0;
        Main.rightWorld = width * 16;
        Main.bottomWorld = height * 16;
        Main.netMode = NetmodeID.SinglePlayer;
        Main.dedServ = true;
        WorldGen.noMapUpdate = true;
        Main.hardMode = false;
        Main.worldSurface = Main.rockLayer = 0;
        Main.ActiveWorldFileData = new WorldFileData { Name = "PvPArenas Jungle source", GameMode = Main.GameMode };
        Main.ActiveWorldFileData.SetWorldSize(width, height);
        Main.ActiveWorldFileData.SetSeed(seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Main.chest = new Chest[Main.chest.Length];
        Main.sign = new Sign[Main.sign.Length];
        // Collision.EmptyTile consults live players/NPCs even during generation. Empty
        // independent entities keep object placement independent of the ongoing match.
        Main.player = Enumerable.Range(0, Main.player.Length).Select(i => new Player { whoAmI = i }).ToArray();
        Main.npc = Enumerable.Range(0, Main.npc.Length).Select(i => new NPC { whoAmI = i }).ToArray();
        Main.projectile = Enumerable.Range(0, Main.projectile.Length).Select(i => new Projectile { whoAmI = i }).ToArray();
        Main.item = Enumerable.Range(0, Main.item.Length).Select(i => new Item { whoAmI = i }).ToArray();
        Main.dust = Enumerable.Range(0, Main.dust.Length).Select(_ => new Dust()).ToArray();
        Main.myPlayer = 0;
        Main.SceneMetrics = new SceneMetrics();
        Main.liquid = Enumerable.Range(0, Main.liquid.Length).Select(_ => new Liquid()).ToArray();
        Main.liquidBuffer = Enumerable.Range(0, Main.liquidBuffer.Length).Select(_ => new LiquidBuffer()).ToArray();
        WorldGen.TreeTops = new Terraria.GameContent.TreeTopsInfo();
        WorldGen.BackgroundsCache = new Terraria.GameContent.BackgroundChangeFlashInfo();
        WorldGen.ExploitDestroyQueue = new Queue<Point>();
        TileEntity.ByID = new Dictionary<int, TileEntity>();
        TileEntity.ByPosition = new Dictionary<Point16, TileEntity>();
        TileEntity.TileEntitiesNextID = 0;
        // genRand lazily replaces _genRand when these markers disagree. Initialize
        // both before the first draw, including the hut material chosen below.
        WorldGen._lastSeed = WorldGen._genRandSeed = seed;
        WorldGen._genRand = new UnifiedRandom(seed);
        Main.rand = new UnifiedRandom(seed);
        WorldGen.gen = WorldGen.generatingWorld = WorldGen.noTileActions = true;
        WorldGen.noLiquidCheck = false;
        WorldGen.dontStarveWorldGen = WorldGen.remixWorldGen = WorldGen.drunkWorldGen = false;
        WorldGen.getGoodWorldGen = WorldGen.notTheBees = WorldGen.tenthAnniversaryWorldGen = false;
        WorldGen.everythingWorldGen = WorldGen.noTrapsWorldGen = false;
        Main.drunkWorld = Main.getGoodWorld = Main.remixWorld = Main.zenithWorld = false;
        Main.tenthAnniversaryWorld = Main.notTheBeesWorld = Main.dontStarveWorld = Main.noTrapsWorld = false;
        GenVars.configuration = WorldGenConfiguration.FromEmbeddedPath("Terraria.GameContent.WorldBuilding.Configuration.json");
        GenVars.structures = new StructureMap();
        GenVars.desertHiveHigh = height;
        GenVars.desertHiveLow = GenVars.desertHiveRight = 0;
        GenVars.desertHiveLeft = width;
        GenVars.worldSurfaceLow = GenVars.worldSurface = GenVars.worldSurfaceHigh = 0;
        GenVars.rockLayerLow = GenVars.rockLayer = GenVars.rockLayerHigh = 0;
        GenVars.copper = TileID.Copper;
        GenVars.iron = TileID.Iron;
        GenVars.silver = TileID.Silver;
        GenVars.gold = TileID.Gold;
        GenVars.jungleHut = (ushort)WorldGen.genRand.Next(5);
        GenVars.jungleOriginX = GenVars.dungeonSide = 0;
        GenVars.jungleMinX = GenVars.jungleMaxX = -1;
        GenVars.snowMinX = new int[height];
        GenVars.snowMaxX = new int[height];
        GenVars.snowTop = GenVars.snowBottom = 0;
        GenVars.skyLakes = 1;
        GenVars.beachBordersWidth = 275;
        GenVars.beachSandRandomCenter = 320;
        GenVars.beachSandRandomWidthRange = 20;
        GenVars.beachSandDungeonExtraWidth = 40;
        GenVars.beachSandJungleExtraWidth = 20;
        GenVars.oceanWaterStartRandomMin = 220;
        GenVars.oceanWaterStartRandomMax = 260;
        GenVars.oceanWaterForcedJungleLength = 275;
        GenVars.leftBeachEnd = GenVars.rightBeachStart = 0;
        GenVars.evilBiomeBeachAvoidance = 380;
        GenVars.evilBiomeAvoidanceMidFixer = 50;
        GenVars.lakesBeachAvoidance = GenVars.smallHolesBeachAvoidance = 340;
        GenVars.surfaceCavesBeachAvoidance = GenVars.surfaceCavesBeachAvoidance2 = 340;
        GenVars.PyrX = GenVars.PyrY = null;
        GenVars.numPyr = 0;
        GenVars.shellStartXLeft = GenVars.shellStartYLeft = GenVars.shellStartXRight = GenVars.shellStartYRight = 0;
        GenVars.snowOriginLeft = GenVars.snowOriginRight = GenVars.dungeonLocation = 0;
        GenVars.logX = GenVars.logY = -1;
        Liquid.ReInit();
    }

    public void Dispose()
    {
        if (disposed) return;
        if (Environment.CurrentManagedThreadId != ownerThread || Volatile.Read(ref activeScope) != 0)
            throw new InvalidOperationException("Dispose a vanilla sandbox outside its scopes on its owning thread.");
        disposed = true;
        planes?.Dispose();
        planes = null;
        scratch = null;
    }

    /// <summary>Reference-preserving snapshots; scratch gets independent mutable arrays/collections.</summary>
    private sealed class State
    {
        private static readonly Type[] Owners = [typeof(Main), typeof(WorldGen), typeof(GenVars),
            typeof(WorldGen.SavedOreTiers), typeof(WorldGenerator), typeof(Liquid), typeof(LiquidBuffer), typeof(TileEntity)];
        private static readonly FieldInfo[] Fields = Owners.SelectMany(type => type.GetFields(StaticFields))
            .Where(field => !field.IsLiteral && !field.IsInitOnly).ToArray();
        private readonly object[] values;
        private readonly UnifiedRandom random;

        private State(object[] values, UnifiedRandom random) { this.values = values; this.random = random; }
        internal static State Capture() => new(Fields.Select(field => field.GetValue(null)).ToArray(), Main.rand);
        internal void Apply()
        {
            for (int i = 0; i < Fields.Length; i++) Fields[i].SetValue(null, values[i]);
            Main.rand = random;
        }
        internal void ApplyCloned()
        {
            for (int i = 0; i < Fields.Length; i++) Fields[i].SetValue(null, Clone(values[i]));
            Main.rand = new UnifiedRandom(0);
        }
        private static object Clone(object value)
        {
            if (value is Array array)
            {
                Array result = (Array)array.Clone();
                if (array.Rank == 1 && array.GetType().GetElementType().IsArray)
                    for (int i = 0; i < array.Length; i++) result.SetValue(Clone(array.GetValue(i)), i);
                return result;
            }
            if (value is IDictionary dictionary && value.GetType().GetConstructor(Type.EmptyTypes) != null)
            {
                IDictionary result = (IDictionary)Activator.CreateInstance(value.GetType());
                foreach (DictionaryEntry entry in dictionary) result.Add(entry.Key, entry.Value);
                return result;
            }
            if (value is IList list && value.GetType().GetConstructor(Type.EmptyTypes) != null)
            {
                IList result = (IList)Activator.CreateInstance(value.GetType());
                foreach (object item in list) result.Add(item);
                return result;
            }
            // These collections expose IEnumerable<T> constructors instead of IList.
            if (value != null && value.GetType().IsGenericType)
            {
                Type definition = value.GetType().GetGenericTypeDefinition();
                if (definition == typeof(HashSet<>) || definition == typeof(Queue<>))
                    return Activator.CreateInstance(value.GetType(), value);
            }
            return value;
        }
    }

    private sealed class TilePlanes : IDisposable
    {
        private static readonly Type TileDataType = typeof(Main).Assembly.GetType("Terraria.TileData", true);
        private static readonly FieldInfo Length = TileDataType.GetField("<Length>k__BackingField", StaticFields);
        private readonly (FieldInfo Field, object Value)[] fields;
        private readonly object length;
        private bool freed;
        private TilePlanes((FieldInfo Field, object Value)[] fields, object length) { this.fields = fields; this.length = length; }
        internal static TilePlanes Capture()
        {
            Delegate registrations = (Delegate)TileDataType.GetField("OnSetLength", StaticFields).GetValue(null);
            Type[] owners = registrations.GetInvocationList().Select(callback => callback.Method.DeclaringType)
                .Where(owner => owner?.IsGenericType == true && owner.GetGenericTypeDefinition().FullName == "Terraria.TileData`1")
                .Distinct().ToArray();
            return new(owners.SelectMany(owner => owner.GetFields(StaticFields)).Where(field => !field.IsLiteral && !field.IsInitOnly)
                .Select(field => (field, field.GetValue(null))).ToArray(), Length.GetValue(null));
        }
        internal void DetachArrays()
        {
            foreach ((FieldInfo field, object value) in fields)
                if (value is Array) field.SetValue(null, null);
        }
        internal void Apply()
        {
            foreach ((FieldInfo field, object value) in fields) field.SetValue(null, value);
            Length.SetValue(null, length);
        }
        public void Dispose()
        {
            if (freed) return;
            freed = true;
            foreach ((FieldInfo _, object value) in fields)
                if (value is GCHandle handle && handle.IsAllocated) handle.Free();
        }
    }
}
