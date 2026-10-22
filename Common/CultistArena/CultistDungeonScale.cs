using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace PvPArenas.Common.CultistArena;

/// <summary>Changes only the native Dungeon construction budget; coordinate bounds remain narrow.</summary>
internal sealed class CultistDungeonScale : IDisposable
{
    private const BindingFlags All = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly int width, height, referenceWidth;
    private readonly List<ILHook> hooks = [];
    private readonly List<object> patchedSites = [];
    private int baseBudget, iterationBudget, roomCalls, hallCalls, dimensionsChecks;
    private bool budgetMatched;

    internal CultistDungeonScale(int width, int height, int referenceWidth)
    {
        this.width = width; this.height = height; this.referenceWidth = referenceWidth;
        try
        {
            hooks.Add(new ILHook(typeof(WorldGen).GetMethod(nameof(WorldGen.MakeDungeon), All)!, PatchBudget));
            if (!budgetMatched) throw new InvalidOperationException("Native MakeDungeon width/60 budget was not matched exactly.");
            hooks.Add(new ILHook(typeof(WorldGen).GetMethod(nameof(WorldGen.DungeonRoom), All)!, il =>
            {
                new ILCursor(il).EmitDelegate<Action>(() => { CheckDimensions(); roomCalls++; });
            }));
            hooks.Add(new ILHook(typeof(WorldGen).GetMethod(nameof(WorldGen.DungeonHalls), All)!, il =>
            {
                new ILCursor(il).EmitDelegate<Action>(() => { CheckDimensions(); hallCalls++; });
            }));
            foreach ((string method, int expected) in new[]
            {
                ("MakeDungeon_Traps", 1), ("MakeDungeon_Lights", 1),
                ("MakeDungeon_GroundFurniture", 3), ("MakeDungeon_Pictures", 1), ("MakeDungeon_Banners", 1)
            })
                hooks.Add(new ILHook(typeof(WorldGen).GetMethod(method, All)!, il => ReplaceWidthLoads(il,
                    instruction => instruction.MatchLdsfld(typeof(Main), nameof(Main.maxTilesX))
                        && (method != "MakeDungeon_Lights" || instruction.Next?.MatchLdcI4(150) == true
                            && instruction.Next.Next?.OpCode == OpCodes.Div), expected,
                    "native decoration workload only; helper coordinate guards retain actual dimensions")));
        }
        catch { Dispose(); throw; }
    }

    private void PatchBudget(ILContext il)
    {
        ILCursor cursor = new(il);
        if (!cursor.TryGotoNext(MoveType.After,
                instruction => instruction.MatchLdsfld(typeof(Main), nameof(Main.maxTilesX)),
                instruction => instruction.MatchLdcI4(60),
                instruction => instruction.OpCode == OpCodes.Div)
            || cursor.Next?.OpCode != OpCodes.Conv_R8
            || cursor.Next.Next == null || !cursor.Next.Next.MatchStloc(out int budgetLocal))
            throw new InvalidOperationException("Installed MakeDungeon does not match the expected width/60 -> double budget expression.");
        int matches = il.Instrs.Zip(il.Instrs.Skip(1)).Count(pair => pair.First.MatchLdsfld(typeof(Main), nameof(Main.maxTilesX))
            && pair.Second.MatchLdcI4(60));
        if (matches != 1) throw new InvalidOperationException("Expected exactly one MakeDungeon width/60 budget, got " + matches);
        cursor.EmitDelegate<Func<int, int>>(nativeBase =>
        {
            CheckDimensions();
            if (nativeBase != width / 60) throw new InvalidOperationException("Native dimensions changed before Dungeon budget calculation.");
            baseBudget = referenceWidth / 60;
            return baseBudget;
        });
        // First store initializes the double; second stores its native randomized increment.
        cursor.GotoNext(MoveType.After, instruction => instruction.MatchStloc(budgetLocal));
        cursor.GotoNext(MoveType.After, instruction => instruction.MatchStloc(budgetLocal));
        cursor.Emit(OpCodes.Ldloc, il.Body.Variables[budgetLocal]);
        cursor.EmitDelegate<Action<double>>(budget =>
        {
            CheckDimensions();
            iterationBudget = checked((int)budget);
            Log.Debug($"[worldgen] Cultist Dungeon budget: base={baseBudget}, randomized={iterationBudget}, actual={Main.maxTilesX}x{Main.maxTilesY}.");

        });
        budgetMatched = true;
        patchedSites.Add(new { method = nameof(WorldGen.MakeDungeon), matches = 1, purpose = "main construction width/60, then unchanged native random increment" });
        ReplaceWidthLoads(il, instruction => instruction.MatchLdsfld(typeof(Main), nameof(Main.maxTilesX))
            && instruction.Next != null && instruction.Next.Next != null
            && (instruction.Next.MatchLdcI4(2) && instruction.Next.Next.OpCode == OpCodes.Mul
                || instruction.Next.MatchLdcI4(100) && instruction.Next.Next.OpCode == OpCodes.Div
                || instruction.Next.MatchLdcI4(20) && instruction.Next.Next.OpCode == OpCodes.Div),
            3, "pit-trap attempt budget width*2; spike quota width/100; shelf quota width/20");
    }

    private void ReplaceWidthLoads(ILContext il, Func<Instruction, bool> predicate, int expected, string purpose)
    {
        Instruction[] targets = il.Instrs.Where(predicate).ToArray();
        if (targets.Length != expected)
            throw new InvalidOperationException($"Expected {expected} workload width loads in {il.Method.Name}, matched {targets.Length}.");
        foreach (Instruction target in targets)
        {
            ILCursor cursor = new(il);
            cursor.Goto(target, MoveType.After);
            cursor.EmitDelegate<Func<int, int>>(actualWidth =>
            {
                CheckDimensions();
                if (actualWidth != width) throw new InvalidOperationException("A decoration quota read a substituted global world width.");
                return referenceWidth;
            });
        }
        patchedSites.Add(new { method = il.Method.Name, matches = targets.Length, purpose });
    }

    private void CheckDimensions()
    {
        dimensionsChecks++;
        if (Main.maxTilesX != width || Main.maxTilesY != height || Main.tile.Width != width || Main.tile.Height != height
            || WorldGen.lastMaxTilesX != width || WorldGen.lastMaxTilesY != height)
            throw new InvalidOperationException("Dungeon scaling changed the native world or backing tile dimensions.");
    }

    internal int ConstructionIterations => iterationBudget;
    internal int RoomCalls => roomCalls;
    internal int HallCalls => hallCalls;
    internal int WorkloadWidthLoads => 11;

    public void Dispose()
    {
        for (int i = hooks.Count - 1; i >= 0; i--) hooks[i].Dispose();
        hooks.Clear();
    }
}
