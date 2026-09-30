using System;
using System.Collections.Generic;
using System.Linq;

namespace PvPArenas.Common.Generation;

/// <summary>API 1.1 reports completed selected-pass indices, but divides its float by the entire catalog.</summary>
internal sealed class NativePassProgress(WorldGenProgressLog trace, Action<string, double> publish = null)
{
    private string[] passes = [];
    private int completed, batch;

    internal void Begin(IReadOnlyList<string> selected)
    {
        passes = selected.ToArray();
        completed = 0;
        batch++;
        if (passes.Length > 0) Update();
    }

    internal void Report(int selectedIndex, string message)
    {
        // Only a completion acknowledges work. Repeated/cosmetic callbacks cannot conceal a stall.
        if (selectedIndex < completed || selectedIndex >= passes.Length
            || message != $"Pass {passes[selectedIndex]} done.") return;
        completed = selectedIndex + 1;
        Update();
    }

    private void Update()
    {
        int current = Math.Min(completed, passes.Length - 1);
        double progress = completed / (double)passes.Length;
        trace.NativeProgress(passes[current], unchecked(batch * 1024 + current), progress);
        publish?.Invoke(completed == passes.Length ? "Native passes complete"
            : $"{passes[current]} ({current + 1}/{passes.Length})", progress);
    }
}
