using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using PvPArenas.Common.Game;
using Terraria.ID;

namespace PvPArenas.Common.Generation;

/// <summary>Server-side tile overview and generation recipe; no renderer or graphics device required.</summary>
internal static class JungleMapExport
{
    private sealed record ExportRecipe(int Seed, string[] Passes, Rectangle ArenaBounds, Rectangle? SourceBounds,
        string Source, int WorldId, int Width, int Height);
    private static ExportRecipe lastRecipe;
    internal static string LastImagePath { get; private set; } = "";
    internal static bool HasRecipe => lastRecipe != null && lastRecipe.WorldId == Main.worldID
        && lastRecipe.Width == Main.maxTilesX && lastRecipe.Height == Main.maxTilesY;
    internal static int? LastSeed => HasRecipe ? lastRecipe.Seed : null;

    internal static void ClearLast()
    {
        lastRecipe = null;
        LastImagePath = "";
    }

    /// <summary>The caller must also verify that the current round uses the Plantera layout.</summary>
    internal static string TryExportLast()
    {
        ExportRecipe recipe = lastRecipe;
        if (recipe == null || recipe.WorldId != Main.worldID
            || recipe.Width != Main.maxTilesX || recipe.Height != Main.maxTilesY) return null;
        return TryExport(recipe.Seed, recipe.Passes, recipe.ArenaBounds, recipe.SourceBounds, recipe.Source);
    }

    /// <summary>Exports the completed compact world at one pixel per tile. Export failure never fails a round.</summary>
    internal static string TryExport(int seed, IReadOnlyList<string> passes, Rectangle arenaBounds,
        Rectangle? sourceBounds = null, string source = null)
    {
        try
        {
            LastImagePath = "";
            // Keep the actual completed generation recipe even when the diagnostics directory is unwritable.
            // Copy the list so subsequent debug settings edits cannot relabel an already generated map.
            lastRecipe = new(seed, new List<string>(passes).ToArray(), arenaBounds, sourceBounds, source,
                Main.worldID, Main.maxTilesX, Main.maxTilesY);
            string directory = Path.Combine(Main.SavePath, "PvPArenas", "WorldGen");
            Directory.CreateDirectory(directory);
            DateTime created = DateTime.UtcNow;
            string filename = $"plantera-{created:yyyyMMdd-HHmmss-fffffff}-seed-{seed}";
            string imagePath = Path.Combine(directory, filename + ".png");
            int width = Main.maxTilesX, height = Main.maxTilesY;
            ArenaLayout layout = MirroredJungleGenerator.CreateLayout();
            arenaBounds = Rectangle.Intersect(arenaBounds, new Rectangle(0, 0, width, height));
            byte[] pixels = new byte[checked((width * 3 + 1) * height)];
            Dictionary<string, int> counts = new();
            int arenaDryOpen = 0;

            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                Tile tile = Main.tile[x, y];
                string material = Material(tile);
                counts.TryGetValue(material, out int count);
                counts[material] = count + 1;
                Color color = Palette(material);
                if (tile.LiquidAmount > 0)
                {
                    string liquid = tile.LiquidType switch
                    {
                        LiquidID.Honey => "honey",
                        LiquidID.Lava => "lava",
                        LiquidID.Shimmer => "shimmer",
                        _ => "water"
                    };
                    counts.TryGetValue(liquid, out count);
                    counts[liquid] = count + 1;
                    color = Palette(liquid);
                }
                Set(x, y, color);
                if (arenaBounds.Contains(x, y) && tile.LiquidAmount == 0
                    && !(tile.HasUnactuatedTile && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType]))
                    arenaDryOpen++;
            }

            Outline(arenaBounds, new Color(102, 143, 150));
            Mark(layout.RedSpawn, Palette("redSpawn"));
            Mark(layout.BlueSpawn, Palette("blueSpawn"));
            Mark(layout.BossSpawn, Palette("bossSpawn"));
            WritePng(imagePath, width, height, pixels);

            Dictionary<string, string> legend = new();
            foreach (string name in counts.Keys)
            {
                Color color = Palette(name);
                legend[name] = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            }
            foreach (string name in new[] { "redSpawn", "blueSpawn", "bossSpawn" })
            {
                Color color = Palette(name);
                legend[name] = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            }
            object Bounds(Rectangle value) => new { value.X, value.Y, value.Width, value.Height };
            object Position(Point value) => new { value.X, value.Y };
            int area = arenaBounds.Width * arenaBounds.Height;
            var report = new
            {
                Seed = seed,
                CreatedUtc = created,
                Source = source,
                Passes = passes,
                World = new { Width = width, Height = height, Main.worldSurface, Main.rockLayer },
                ArenaBounds = Bounds(arenaBounds),
                SourceBounds = sourceBounds.HasValue ? Bounds(sourceBounds.Value) : null,
                Markers = new { Red = Position(layout.RedSpawn), Blue = Position(layout.BlueSpawn), Boss = Position(layout.BossSpawn) },
                ArenaDryOpenFraction = area == 0 ? 0 : arenaDryOpen / (double)area,
                TileCounts = counts,
                FeatureStatus = VanillaMirroredJungleGenerator.LastFeatureStatus is { } status ? new
                {
                    status.Passed,
                    Checks = status.Checks.Select(check => new { check.Feature, check.Count, check.Passed }).ToArray(),
                    Structures = VanillaMirroredJungleGenerator.FeaturePlacements.Select(feature => new
                    { feature.Feature, Bounds = Bounds(feature.Footprint) }).ToArray()
                } : null,
                Legend = legend,
                Image = Path.GetFileName(imagePath),
                ImageDescription = "Tile-data overview, one pixel per tile; cyan arena outline and spawn markers. Liquid counts overlap material counts."
            };
            File.WriteAllText(Path.ChangeExtension(imagePath, ".json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Log.Info($"[worldgen] Plantera overview exported: {imagePath}");
            LastImagePath = imagePath;
            return imagePath;

            void Set(int x, int y, Color color)
            {
                if (x < 0 || y < 0 || x >= width || y >= height) return;
                int index = y * (width * 3 + 1) + x * 3 + 1;
                pixels[index] = color.R;
                pixels[index + 1] = color.G;
                pixels[index + 2] = color.B;
            }

            void Mark(Point point, Color color)
            {
                for (int dx = -3; dx <= 3; dx++)
                for (int dy = -3; dy <= 3; dy++)
                    if (dx * dx + dy * dy <= 9) Set(point.X + dx, point.Y + dy - 2, color);
            }

            void Outline(Rectangle rectangle, Color color)
            {
                if (rectangle.IsEmpty) return;
                for (int x = rectangle.Left; x < rectangle.Right; x++)
                {
                    Set(x, rectangle.Top, color);
                    Set(x, rectangle.Bottom - 1, color);
                }
                for (int y = rectangle.Top; y < rectangle.Bottom; y++)
                {
                    Set(rectangle.Left, y, color);
                    Set(rectangle.Right - 1, y, color);
                }
            }
        }
        catch (Exception error)
        {
            Log.Warn($"[worldgen] Plantera overview export failed: {error.Message}");
            return null;
        }
    }

    private static string Material(Tile tile)
    {
        if (!tile.HasUnactuatedTile) return tile.WallType == WallID.None ? "air" : "wall";
        return tile.TileType switch
        {
            TileID.Mud => "mud",
            TileID.JungleGrass => "jungleGrass",
            TileID.JungleVines or TileID.JunglePlants or TileID.JunglePlants2 or TileID.PlantDetritus => "foliage",
            TileID.LifeFruit => "lifeFruit",
            TileID.PlanteraBulb => "planteraBulb",
            TileID.Hive => "hive",
            TileID.HoneyBlock => "honeyBlock",
            TileID.Larva => "larva",
            TileID.Containers or TileID.Containers2 => "chest",
            TileID.RichMahogany or TileID.LivingMahogany => "mahogany",
            TileID.LivingMahoganyLeaves => "leaves",
            TileID.LihzahrdBrick => "temple",
            TileID.IridescentBrick or TileID.GreenMossBrick or TileID.Mudstone => "shrine",
            TileID.Stone => "stone",
            TileID.Dirt => "dirt",
            TileID.Platforms => "platform",
            TileID.Iron or TileID.Lead or TileID.Copper or TileID.Tin or TileID.Silver or TileID.Tungsten or TileID.Gold or TileID.Platinum => "ore",
            _ => "other"
        };
    }

    private static Color Palette(string material) => material switch
    {
        "air" => new(12, 21, 26),
        "wall" => new(29, 45, 37),
        "mud" => new(95, 78, 48),
        "jungleGrass" => new(63, 132, 57),
        "foliage" => new(107, 179, 70),
        "lifeFruit" => new(247, 96, 133),
        "planteraBulb" => new(247, 77, 177),
        "hive" => new(220, 165, 48),
        "honeyBlock" => new(197, 119, 24),
        "larva" => new(255, 233, 147),
        "chest" => new(194, 133, 210),
        "mahogany" => new(158, 83, 51),
        "leaves" => new(63, 111, 46),
        "temple" => new(130, 141, 72),
        "shrine" => new(148, 166, 94),
        "stone" => new(107, 108, 110),
        "dirt" => new(111, 83, 59),
        "platform" => new(207, 172, 112),
        "ore" => new(188, 184, 163),
        "water" => new(45, 119, 166),
        "honey" => new(224, 161, 44),
        "lava" => new(227, 81, 28),
        "shimmer" => new(174, 125, 224),
        "redSpawn" => new(247, 80, 86),
        "blueSpawn" => new(83, 149, 255),
        "bossSpawn" => new(250, 218, 98),
        _ => new(82, 102, 76)
    };

    private static void WritePng(string path, int width, int height, byte[] pixels)
    {
        using FileStream file = File.Create(path);
        file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 2;
        Chunk("IHDR", header);
        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.SmallestSize, true)) zlib.Write(pixels);
        Chunk("IDAT", compressed.ToArray());
        Chunk("IEND", Array.Empty<byte>());

        void Chunk(string type, byte[] data)
        {
            Span<byte> size = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(size, data.Length);
            file.Write(size);
            byte[] name = Encoding.ASCII.GetBytes(type);
            file.Write(name);
            file.Write(data);
            uint crc = uint.MaxValue;
            Accumulate(name);
            Accumulate(data);
            BinaryPrimitives.WriteUInt32BigEndian(size, ~crc);
            file.Write(size);

            void Accumulate(byte[] bytes)
            {
                foreach (byte value in bytes)
                {
                    crc ^= value;
                    for (int bit = 0; bit < 8; bit++)
                        crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320);
                }
            }
        }
    }
}

internal sealed class JungleMapExportLifecycle : ModSystem
{
    public override void OnWorldLoad() => JungleMapExport.ClearLast();
    public override void OnWorldUnload() => JungleMapExport.ClearLast();
}
