using System;
using System.Diagnostics;
using System.Threading;
using Terraria.ID;

namespace PvPArenas.Common.Generation;

/// <summary>Server diagnostics independent of the game update loop. Notifications must only queue main-thread work.</summary>
internal sealed class WorldGenProgressLog : IDisposable
{
    private const double StallSeconds = 30, RepeatSeconds = 60, DebugSeconds = 5;
    private readonly object gate = new();
    private readonly string job;
    private readonly bool enabled;
    private Func<double> clock;
    private Action<string, string> log;
    private Action<string, bool> notify;
    private Timer timer;
    private string stageKey = "", stage = "Starting", operation;
    private double progress, lastAdvance, lastWarning, lastDebug, lastDebugProgress;
    private bool stalled, finished;

    internal WorldGenProgressLog(string purpose, int seed, int width, int height, Action<string, bool> notify = null)
        : this(purpose, seed, width, height, notify, CreateClock(), WriteServerLog, true) { }

    // A monotonic clock and manual Poll let tests cover a frozen native worker without waiting in real time.
    internal WorldGenProgressLog(string purpose, int seed, int width, int height, Action<string, bool> notify,
        Func<double> elapsedSeconds, Action<string, string> log, bool startTimer)
    {
        job = $"[worldgen] {purpose} seed={seed}";
        enabled = Main.netMode == NetmodeID.Server;
        clock = elapsedSeconds;
        this.log = log;
        this.notify = notify;
        lastAdvance = lastDebug = clock();
        if (!enabled) return;
        Write("info", $"{job} started size={width}x{height}.");
        if (startTimer) timer = new Timer(_ => Poll(), null, 1000, 1000);
    }

    internal void Report(string stage, double progress) => Advance(stage, stage, progress);

    internal void NativeProgress(string passName, int passId, double progress) =>
        Advance($"native:{passId}", $"Native: {passName}", progress);

    // Short framing/sync operations keep their parent stage and do not produce a log line per strip.
    internal void SetOperation(string name)
    {
        lock (gate)
        {
            if (finished || !enabled || Main.netMode != NetmodeID.Server || operation == name) return;
            operation = name;
            RecordAdvance(clock());
        }
    }

    private void Advance(string key, string label, double value)
    {
        lock (gate)
        {
            if (finished || !enabled || Main.netMode != NetmodeID.Server) return;
            double now = clock();
            value = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : progress;
            bool changed = key != stageKey;
            bool advanced = changed || value > progress + 0.000001;
            if (changed)
            {
                stageKey = key;
                stage = label;
                progress = value;
            }
            else progress = Math.Max(progress, value);

            if (advanced) RecordAdvance(now);

            if (changed || (now - lastDebug >= DebugSeconds &&
                (progress - lastDebugProgress >= 0.099999 || progress >= 1 && lastDebugProgress < 1)))
            {
                Write("debug", Describe(now));
                lastDebug = now;
                lastDebugProgress = progress;
            }
        }
    }

    internal void Poll()
    {
        lock (gate)
        {
            if (finished || !enabled || Main.netMode != NetmodeID.Server) return;
            try
            {
                double now = clock(), idle = now - lastAdvance;
                if (idle < StallSeconds || stalled && now - lastWarning < RepeatSeconds) return;
                string message = $"{Describe(now)} no progress for {idle:0}s; generation may be stuck.";
                Write("warn", message);
                lastWarning = now;
                if (!stalled)
                {
                    stalled = true;
                    Notify(message, true);
                }
            }
            catch (Exception error)
            {
                Write("warn", $"{job} watchdog failed: {error.Message}");
            }
        }
    }

    internal void Complete() => Finish("info", "completed");
    internal void Fail(Exception error) => Finish("error", $"failed: {error.GetBaseException().GetType().Name}: {error.GetBaseException().Message}");
    public void Dispose() => Finish("info", "cancelled");

    private void Finish(string level, string outcome)
    {
        lock (gate)
        {
            if (finished) return;
            finished = true;
            timer?.Dispose();
            timer = null;
            if (enabled && Main.netMode == NetmodeID.Server) Write(level, $"{Describe(clock())} {outcome}.");
            notify = null;
            log = null;
            clock = null;
        }
    }

    private string Describe(double elapsed) => $"{job} stage=\"{stage}\""
        + (operation == null ? "" : $" operation=\"{operation}\"")
        + $" progress={progress:P0} elapsed={elapsed:0.0}s";

    private void RecordAdvance(double now)
    {
        double idle = now - lastAdvance;
        lastAdvance = now;
        if (!stalled) return;
        stalled = false;
        string message = $"{Describe(now)} resumed after {idle:0}s without progress.";
        Write("info", message);
        Notify(message, false);
    }

    private void Notify(string message, bool isStalled)
    {
        try { notify?.Invoke(message, isStalled); }
        catch (Exception error) { Write("warn", $"{job} notification failed: {error.Message}"); }
    }

    private void Write(string level, string message)
    {
        // A logger must never abort native generation or escape a thread-pool watchdog callback.
        try { log?.Invoke(level, message); }
        catch { }
    }

    private static Func<double> CreateClock()
    {
        Stopwatch watch = Stopwatch.StartNew();
        return () => watch.Elapsed.TotalSeconds;
    }

    private static void WriteServerLog(string level, string message)
    {
        switch (level)
        {
            case "info": Log.Info(message); break;
            case "warn": Log.Warn(message); break;
            case "error": Log.Error(message); break;
            default: Log.Debug(message); break;
        }
    }
}
