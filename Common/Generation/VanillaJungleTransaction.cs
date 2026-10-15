using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Terraria.WorldBuilding;

namespace PvPArenas.Common.Generation;

/// <summary>Measures actual vanilla placement writes and rolls back rejected source-world attempts.</summary>
internal sealed class VanillaJungleTransaction : IDisposable
{
    private const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags InstanceFields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly FieldInfo[] GenerationFields = typeof(GenVars).GetFields(Fields)
        .Where(field => !field.IsLiteral && !field.IsInitOnly).ToArray();
    private static readonly FieldInfo[] StructureFields = typeof(StructureMap).GetFields(InstanceFields)
        .Where(field => field.FieldType == typeof(List<Rectangle>)).ToArray();
    private readonly int width = Main.maxTilesX, height = Main.maxTilesY, columns = Main.tile.Width, stride = Main.tile.Height;
    private readonly byte[][] planes;
    private readonly int[] elementSizes;
    private readonly Chest[] chests;
    private readonly object[] generation;
    private readonly StructureMap structures;
    private readonly Rectangle[][] structureAreas;
    private readonly bool[] solid = (bool[])Main.tileSolid.Clone(), solidTop = (bool[])Main.tileSolidTop.Clone();
    private bool committed, disposed;
    internal string LastFailure { get; private set; } = "";

    internal VanillaJungleTransaction()
    {
        int count = Main.tile.Width * Main.tile.Height;
        planes = Enumerable.Range(0, 5).Select(index => Plane(index).ToArray()).ToArray();
        elementSizes = planes.Select(plane => plane.Length / count).ToArray();
        chests = CloneChests(Main.chest);
        generation = GenerationFields.Select(field => CloneValue(field.GetValue(null))).ToArray();
        structures = GenVars.structures;
        structureAreas = StructureFields.Select(field => ((List<Rectangle>)field.GetValue(structures)).ToArray()).ToArray();
    }

    internal bool TryCommit(Rectangle allowed, out Rectangle footprint) => TryCommit(allowed, Array.Empty<Rectangle>(), out footprint);

    /// <summary>Checks every changed tile, including writes outside the advertised vanilla structure rectangle.</summary>
    internal bool TryCommit(Rectangle allowed, IReadOnlyList<Rectangle> excluded, out Rectangle footprint)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        LastFailure = "";
        footprint = Rectangle.Empty;
        if (Main.maxTilesX != width || Main.maxTilesY != height || Main.tile.Width != columns || Main.tile.Height != stride)
            throw new InvalidOperationException("A vanilla placement changed source-world dimensions.");
        int left = width, top = height, right = -1, bottom = -1;
        for (int component = 0; component < planes.Length; component++)
        {
            ReadOnlySpan<byte> before = planes[component];
            Span<byte> after = Plane(component);
            int size = elementSizes[component];
            for (int x = 0; x < columns; x++)
            {
                int start = x * stride * size;
                if (before.Slice(start, stride * size).SequenceEqual(after.Slice(start, stride * size))) continue;
                for (int y = 0; y < stride; y++)
                {
                    int offset = start + y * size;
                    if (before.Slice(offset, size).SequenceEqual(after.Slice(offset, size))) continue;
                    bool reserved = false;
                    for (int i = 0; i < excluded.Count && !reserved; i++) reserved = excluded[i].Contains(x, y);
                    if (!allowed.Contains(x, y) || reserved)
                    {
                        LastFailure = $"{(reserved ? "reserved feature" : "outside crop")} write at ({x},{y}), tile plane {component}";
                        Restore();
                        return false;
                    }
                    left = Math.Min(left, x); right = Math.Max(right, x);
                    top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                }
            }
        }
        if (right < left)
        {
            LastFailure = "placement changed no tiles";
            Restore();
            return false;
        }
        footprint = new Rectangle(left, top, right - left + 1, bottom - top + 1);
        committed = true;
        return true;
    }

    /// <summary>Can also undo a successful commit, allowing callers to reject incomplete generated features.</summary>
    internal void Restore()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        for (int i = 0; i < planes.Length; i++)
        {
            ReadOnlySpan<byte> before = planes[i];
            Span<byte> after = Plane(i);
            if (before.SequenceEqual(after)) continue;
            int columnSize = stride * elementSizes[i];
            for (int x = 0; x < columns; x++)
            {
                int start = x * columnSize;
                ReadOnlySpan<byte> original = before.Slice(start, columnSize);
                Span<byte> current = after.Slice(start, columnSize);
                if (!original.SequenceEqual(current)) original.CopyTo(current);
            }
        }
        Main.chest = CloneChests(chests);
        for (int i = 0; i < GenerationFields.Length; i++) GenerationFields[i].SetValue(null, CloneValue(generation[i]));
        for (int i = 0; i < StructureFields.Length; i++)
        {
            List<Rectangle> areas = (List<Rectangle>)StructureFields[i].GetValue(structures);
            areas.Clear(); areas.AddRange(structureAreas[i]);
        }
        solid.CopyTo(Main.tileSolid, 0);
        solidTop.CopyTo(Main.tileSolidTop, 0);
        committed = false;
    }

    public void Dispose()
    {
        if (disposed) return;
        if (!committed) Restore();
        disposed = true;
    }

    private static object CloneValue(object value) => value is Array array ? array.Clone() : value;
    private static Chest[] CloneChests(Chest[] source) => source.Select(chest => chest == null ? null : new Chest
    {
        x = chest.x, y = chest.y, name = chest.name,
        item = chest.item.Select(item => item?.Clone() ?? new Item()).ToArray()
    }).ToArray();

    private static Span<byte> Plane(int index) => index switch
    {
        0 => MemoryMarshal.AsBytes(Main.tile.GetData<TileTypeData>().AsSpan()),
        1 => MemoryMarshal.AsBytes(Main.tile.GetData<WallTypeData>().AsSpan()),
        2 => MemoryMarshal.AsBytes(Main.tile.GetData<LiquidData>().AsSpan()),
        3 => MemoryMarshal.AsBytes(Main.tile.GetData<TileWallBrightnessInvisibilityData>().AsSpan()),
        4 => MemoryMarshal.AsBytes(Main.tile.GetData<TileWallWireStateData>().AsSpan()),
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
}
