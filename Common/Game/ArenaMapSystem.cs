using System;
using System.Collections.Generic;
using Terraria.ID;
using Terraria.Map;

namespace PvPArenas.Common.Game;

/// <summary>Locally reveals only the current arena and hides the map between rounds.</summary>
[Autoload(Side = ModSide.Client)]
internal sealed class ArenaMapSystem : ModSystem
{
    internal const int SectionWidth = 200;
    internal const int SectionHeight = 150;

    private const int UpdatesPerTick = 100_000;
    private const int SectionsPerDraw = 4;
    private const int DebugScanDelay = 58 * 60;

    private readonly Queue<Point> redrawSections = new();
    private Rectangle revealBounds;
    private int nextTile;
    private int sectionWaitTicks;
    private int redrawPassesRemaining;
    private bool revealStarted;
    private bool revealComplete;
    private bool hiddenApplied;
    private bool allowMapUpdates;

    public override void Load()
    {
        On_WorldMap.Update += FilterMapUpdate;
        On_WorldMap.UpdateLighting += FilterMapLightingUpdate;
        On_Main.DrawToMap += DrawQueuedMapSections;
    }

    public override void Unload()
    {
        On_WorldMap.Update -= FilterMapUpdate;
        On_WorldMap.UpdateLighting -= FilterMapLightingUpdate;
        On_Main.DrawToMap -= DrawQueuedMapSections;
    }

    public override void PostUpdateEverything()
    {
        if (Main.gameMenu || Main.Map == null || !Main.mapEnabled)
            return;

        RoundManager manager = ModContent.GetInstance<RoundManager>();
        if (!manager.IsActive)
        {
            revealStarted = false;
            hiddenApplied = false;
            redrawSections.Clear();
            redrawPassesRemaining = 0;
            return;
        }
        bool activeRound = manager.CurrentPhase is RoundManager.RoundPhase.FreezeCountdown
            or RoundManager.RoundPhase.Playing;

        if (activeRound && manager.CurrentLayout != null)
        {
            Rectangle bounds = ClampToMap(manager.CurrentLayout.ArenaBounds);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                allowMapUpdates = false;
                HideMap();
                return;
            }

            if (!revealStarted || bounds != revealBounds)
                BeginReveal(bounds);

            if (!revealComplete)
            {
                if (ArenaSectionsLoaded(bounds, out int loaded, out int total))
                    RevealBatch();
                else if (sectionWaitTicks++ % 120 == 0)
                    Log.Chat($"Waiting for arena map sections: {loaded}/{total} loaded; bounds={bounds}.");
            }
        }
        else
        {
            allowMapUpdates = false;
            revealStarted = false;
            revealComplete = false;
            nextTile = 0;
            sectionWaitTicks = 0;
            redrawSections.Clear();
            redrawPassesRemaining = 0;

            HideMap();
        }

        if (redrawSections.Count > 0 || redrawPassesRemaining > 0)
            Main.updateMap = true;

#if DEBUG
        if (Main.GameUpdateCount % DebugScanDelay == 0)
            ReportExploredPercentage();
#endif
    }

    public override void OnWorldUnload()
    {
        revealBounds = Rectangle.Empty;
        nextTile = 0;
        sectionWaitTicks = 0;
        redrawPassesRemaining = 0;
        revealStarted = false;
        revealComplete = false;
        hiddenApplied = false;
        allowMapUpdates = false;
        redrawSections.Clear();
    }

    private void BeginReveal(Rectangle bounds)
    {
        revealBounds = bounds;
        nextTile = 0;
        sectionWaitTicks = 0;
        revealStarted = true;
        revealComplete = false;
        hiddenApplied = false;
        allowMapUpdates = true;
        redrawSections.Clear();
        redrawPassesRemaining = 0;

        ClearLocalMap();
        int sectionCount = CountSections(bounds);
        Log.Chat($"Arena map reveal started: bounds={bounds}, tiles={bounds.Width * bounds.Height}, sections={sectionCount}.");
    }

    private void RevealBatch()
    {
        int total = revealBounds.Width * revealBounds.Height;
        int end = Math.Min(total, nextTile + UpdatesPerTick);
        while (nextTile < end)
        {
            int x = revealBounds.X + nextTile % revealBounds.Width;
            int y = revealBounds.Y + nextTile / revealBounds.Width;
            Main.Map.Update(x, y, byte.MaxValue);
            nextTile++;
        }

        // WorldMap.Update marks MapTiles as changed. updateMap is the flag that
        // actually makes Main.DrawToMap consume those changes on the draw thread.
        Main.mapReady = true;
        Main.updateMap = true;

        if (nextTile < total)
            return;

        revealComplete = true;
        QueueSectionRedraws(twoPasses: true);

        long revealed = CountRevealed(revealBounds);
        double percent = total == 0 ? 0d : revealed * 100d / total;
        Log.Chat($"Arena map reveal complete: {revealed}/{total} tiles ({percent:0.0000}%), sections={CountSections(revealBounds)}.");
    }

    private void HideMap()
    {
        if (hiddenApplied)
            return;

        ClearLocalMap();
        hiddenApplied = true;
        Log.Chat("Arena map hidden: local exploration reset to 0%.");
    }

    private static void ClearLocalMap()
    {
        Main.Map.Clear();
        Main.clearMap = true;
        Main.updateMap = true;
        Main.mapReady = true;
    }

    private void FilterMapUpdate(On_WorldMap.orig_Update orig, WorldMap map,
        int x, int y, byte light)
    {
        if (!ModContent.GetInstance<RoundManager>().IsActive || allowMapUpdates && revealBounds.Contains(x, y))
            orig(map, x, y, light);
    }

    private bool FilterMapLightingUpdate(On_WorldMap.orig_UpdateLighting orig, WorldMap map,
        int x, int y, byte light)
    {
        return (!ModContent.GetInstance<RoundManager>().IsActive || allowMapUpdates && revealBounds.Contains(x, y))
            && orig(map, x, y, light);
    }

    private void DrawQueuedMapSections(On_Main.orig_DrawToMap orig, Main main)
    {
        orig(main);

        int budget = SectionsPerDraw;
        while (budget-- > 0 && redrawSections.TryDequeue(out Point section))
            main.DrawToMap_Section(section.X, section.Y);

        if (redrawSections.Count > 0)
            return;

        if (redrawPassesRemaining > 0)
        {
            redrawPassesRemaining--;
            if (redrawPassesRemaining > 0)
                EnqueueArenaSections();
            else
                Log.Chat($"Arena map render refreshed for {CountSections(revealBounds)} sections.");
        }
    }

    private void QueueSectionRedraws(bool twoPasses)
    {
        redrawSections.Clear();
        redrawPassesRemaining = twoPasses ? 2 : 1;
        EnqueueArenaSections();
    }

    private void EnqueueArenaSections()
    {
        GetSectionRange(revealBounds, out int minX, out int maxX, out int minY, out int maxY);
        if (maxX < minX || maxY < minY)
            return;

        for (int sectionY = minY; sectionY <= maxY; sectionY++)
            for (int sectionX = minX; sectionX <= maxX; sectionX++)
                redrawSections.Enqueue(new Point(sectionX, sectionY));
    }

    private static bool ArenaSectionsLoaded(Rectangle bounds, out int loaded, out int total)
    {
        GetSectionRange(bounds, out int minX, out int maxX, out int minY, out int maxY);
        loaded = 0;
        if (maxX < minX || maxY < minY)
        {
            total = 0;
            return false;
        }

        total = (maxX - minX + 1) * (maxY - minY + 1);

        if (Main.netMode != NetmodeID.MultiplayerClient || Main.sectionManager == null)
        {
            loaded = total;
            return true;
        }

        for (int sectionY = minY; sectionY <= maxY; sectionY++)
            for (int sectionX = minX; sectionX <= maxX; sectionX++)
                if (Main.sectionManager.SectionLoaded(sectionX, sectionY))
                    loaded++;

        return loaded == total;
    }

    private static Rectangle ClampToMap(Rectangle bounds)
    {
        int left = Math.Clamp(bounds.Left, 0, Main.Map.MaxWidth);
        int top = Math.Clamp(bounds.Top, 0, Main.Map.MaxHeight);
        int right = Math.Clamp(bounds.Right, left, Main.Map.MaxWidth);
        int bottom = Math.Clamp(bounds.Bottom, top, Main.Map.MaxHeight);
        return new Rectangle(left, top, right - left, bottom - top);
    }

    private static int CountSections(Rectangle bounds)
    {
        GetSectionRange(bounds, out int minX, out int maxX, out int minY, out int maxY);
        if (maxX < minX || maxY < minY)
            return 0;
        return (maxX - minX + 1) * (maxY - minY + 1);
    }

    private static void GetSectionRange(Rectangle bounds,
        out int minX, out int maxX, out int minY, out int maxY)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0
            || Main.maxSectionsX <= 0 || Main.maxSectionsY <= 0)
        {
            minX = minY = 0;
            maxX = maxY = -1;
            return;
        }

        minX = Math.Clamp(bounds.Left / SectionWidth, 0, Main.maxSectionsX - 1);
        maxX = Math.Clamp((bounds.Right - 1) / SectionWidth, minX, Main.maxSectionsX - 1);
        minY = Math.Clamp(bounds.Top / SectionHeight, 0, Main.maxSectionsY - 1);
        maxY = Math.Clamp((bounds.Bottom - 1) / SectionHeight, minY, Main.maxSectionsY - 1);
    }

    private static long CountRevealed(Rectangle bounds)
    {
        long revealed = 0;
        for (int y = bounds.Top; y < bounds.Bottom; y++)
            for (int x = bounds.Left; x < bounds.Right; x++)
                if (Main.Map.IsRevealed(x, y))
                    revealed++;
        return revealed;
    }

#if DEBUG
    private void ReportExploredPercentage()
    {
        int edge = WorldMap.BlackEdgeWidth;
        Rectangle scan = new(
            Math.Clamp(edge, 0, Main.Map.MaxWidth),
            Math.Clamp(edge, 0, Main.Map.MaxHeight),
            Math.Max(0, Main.Map.MaxWidth - edge * 2),
            Math.Max(0, Main.Map.MaxHeight - edge * 2));
        if (scan.Width <= 0 || scan.Height <= 0)
            scan = new Rectangle(0, 0, Main.Map.MaxWidth, Main.Map.MaxHeight);

        long total = (long)scan.Width * scan.Height;
        if (total <= 0)
            return;

        long revealed = CountRevealed(scan);
        long arenaRevealed = revealStarted ? CountRevealed(revealBounds) : 0;
        long outsideArena = Math.Max(0, revealed - arenaRevealed);
        Log.Chat($"Map Explored: {revealed * 100d / total:0.0000}% ({revealed}/{total}); "
            + $"arena={arenaRevealed}, outside arena={outsideArena} tiles.");
    }
#endif
}
