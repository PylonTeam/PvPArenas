using System;
using System.Collections.Generic;
using System.Linq;
using Terraria.ID;

namespace PvPArenas.Common.Generation;

/// <summary>Seeds missing small features through Terraria's own placement and growth routines.</summary>
internal static class VanillaJungleDetails
{
    internal static void Place(JungleExtrasArgs args, List<JungleExtraPlacement> result)
    {
        Geology("Clay", TileID.ClayBlock);
        Geology("Dirt", TileID.Dirt);
        Geology("Stone", TileID.Stone);
        Geology("Ores", TileID.Gold);
        Geology("Gems", TileID.Amethyst);
        Floor("Life Crystals", TileID.Heart, point => WorldGen.PlaceTile(point.X, point.Y, TileID.Heart, mute: true));
        Floor("Pots", TileID.Pots, point => WorldGen.PlacePot(point.X, point.Y));
        Floor("Rubble", TileID.SmallPiles, point => WorldGen.PlaceSmallPile(point.X, point.Y, WorldGen.genRand.Next(0, 6), 0));
        Floor("Large jungle plants", TileID.PlantDetritus, point =>
        {
            WorldGen.PlaceJunglePlant(point.X, point.Y, TileID.PlantDetritus, WorldGen.genRand.Next(8), 0);
            return true;
        });
        Floor("Small jungle plants", TileID.JunglePlants, point => WorldGen.PlaceTile(point.X, point.Y, TileID.JunglePlants, mute: true));
        Floor("Ordinary trees", TileID.Trees, point =>
        {
            WorldGen.GrowUndergroundTree(point.X, point.Y + 1);
            return true;
        });
        Ceiling("Vines", TileID.JungleVines, point => WorldGen.PlaceTile(point.X, point.Y, TileID.JungleVines, mute: true));
        Ceiling("Stalactites", TileID.Stalactite, point =>
        {
            WorldGen.PlaceUncheckedStalactite(point.X, point.Y, true, WorldGen.genRand.Next(3), false);
            return true;
        });
        if (VanillaJungleExtras.Count(args.Bounds, TileID.Cobweb) == 0)
            VanillaJungleExtras.TryPlace(args, result, "Cobwebs", VanillaJungleExtras.Candidates(args.Bounds, 10, 10, 96), point =>
            {
                if (Main.tile[point.X, point.Y].HasTile) return false;
                WorldGen.TileRunner(point.X, point.Y, 5, 4, TileID.Cobweb, addTile: true);
                return VanillaJungleExtras.Count(args.Bounds, TileID.Cobweb) > 0;
            });
        if (VanillaJungleExtras.Count(args.Bounds, TileID.LilyPad) + VanillaJungleExtras.Count(args.Bounds, TileID.Cattail)
            + VanillaJungleExtras.Count(args.Bounds, TileID.Bamboo) == 0)
        {
            List<Point> water = [];
            for (int x = args.Bounds.Left + 8; x < args.Bounds.Right - 8; x++)
            for (int y = args.Bounds.Top + 8; y < args.Bounds.Bottom - 8; y++)
                if (!Main.tile[x, y].HasTile && Main.tile[x, y].LiquidAmount > 0 && Main.tile[x, y].LiquidType == LiquidID.Water)
                    water.Add(new(x, y));
            VanillaJungleExtras.Shuffle(water);
            VanillaJungleExtras.TryPlace(args, result, "Water plants", water.Take(128), point =>
            {
                if (WorldGen.PlaceLilyPad(point.X, point.Y)) return true;
                if (WorldGen.PlaceBamboo(point.X, point.Y)) return true;
                WorldGen.PlaceCatTail(point.X, point.Y);
                return VanillaJungleExtras.Count(args.Bounds, TileID.Cattail) > 0;
            });
        }

        void Geology(string feature, ushort type)
        {
            if (VanillaJungleExtras.Count(args.Bounds, type) > 0) return;
            VanillaJungleExtras.TryPlace(args, result, feature, VanillaJungleExtras.Candidates(args.Bounds, 15, 15, 64), point =>
            {
                Tile tile = Main.tile[point.X, point.Y];
                if (!tile.HasTile || !WorldGen.SolidTile(point.X, point.Y)) return false;
                WorldGen.TileRunner(point.X, point.Y, 5, 5, type);
                return VanillaJungleExtras.Count(args.Bounds, type) >= 4;
            });
        }

        void Floor(string feature, ushort type, Func<Point, bool> place)
        {
            if (VanillaJungleExtras.Count(args.Bounds, type) > 0) return;
            VanillaJungleExtras.TryPlace(args, result, feature, VanillaJungleExtras.Floors(args.Bounds, 192), point =>
                place(point) && VanillaJungleExtras.Count(args.Bounds, type) > 0);
        }

        void Ceiling(string feature, ushort type, Func<Point, bool> place)
        {
            if (VanillaJungleExtras.Count(args.Bounds, type) > 0) return;
            List<Point> ceiling = [];
            for (int x = args.Bounds.Left + 8; x < args.Bounds.Right - 8; x++)
            for (int y = args.Bounds.Top + 8; y < args.Bounds.Bottom - 8; y++)
                if (!Main.tile[x, y].HasTile && Main.tile[x, y].LiquidAmount == 0 && WorldGen.SolidTile(x, y - 1))
                    ceiling.Add(new(x, y));
            VanillaJungleExtras.Shuffle(ceiling);
            VanillaJungleExtras.TryPlace(args, result, feature, ceiling.Take(192), point =>
                place(point) && VanillaJungleExtras.Count(args.Bounds, type) > 0);
        }
    }
}
