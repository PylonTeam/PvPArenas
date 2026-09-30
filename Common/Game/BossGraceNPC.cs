using System;
using System.IO;
using Terraria.ID;
using Terraria.ModLoader.IO;

namespace PvPArenas.Common.Game;

/// <summary>Pauses the server-owned round boss without replacing its vanilla AI state.</summary>
internal sealed class BossGraceNPC : GlobalNPC
{
    private const int TicksPerSecond = 60;

    private int remainingTicks;
    private bool managed;
    private bool paused;
    private bool originalDontTakeDamage;

    public override bool InstancePerEntity => true;
    internal bool Paused => paused;

    public override void SetDefaults(NPC npc) => ResetState();

    internal void Begin(NPC npc, int durationSeconds)
    {
        int durationTicks = Math.Max(0, durationSeconds) * TicksPerSecond;
        if (npc?.active != true || durationTicks <= 0)
            return;

        remainingTicks = durationTicks;
        managed = true;
        originalDontTakeDamage = npc.dontTakeDamage;
        paused = true;
        ApplyPause(npc);
    }

    public override bool PreAI(NPC npc)
    {
        if (!paused)
            return true;

        ApplyPause(npc);

        if (Main.netMode != NetmodeID.MultiplayerClient && remainingTicks <= 0)
        {
            End(npc);
            return true;
        }

        if (Main.netMode != NetmodeID.MultiplayerClient)
            remainingTicks--;

        return false;
    }

    public override void PostAI(NPC npc)
    {
        if (paused)
            npc.velocity = Vector2.Zero;
    }

    public override bool CheckActive(NPC npc) => !paused;

    public override bool CanHitPlayer(NPC npc, Player target, ref int cooldownSlot) => !paused;

    public override bool? CanBeHitByItem(NPC npc, Player player, Item item) => paused ? false : null;

    public override bool? CanBeHitByProjectile(NPC npc, Projectile projectile) => paused ? false : null;

    public override void SendExtraAI(NPC npc, BitWriter bitWriter, BinaryWriter binaryWriter)
    {
        bitWriter.WriteBit(managed);
        if (!managed)
            return;

        bitWriter.WriteBit(paused);
        bitWriter.WriteBit(originalDontTakeDamage);
        binaryWriter.Write(remainingTicks);
    }

    public override void ReceiveExtraAI(NPC npc, BitReader bitReader, BinaryReader binaryReader)
    {
        if (!bitReader.ReadBit())
        {
            if (paused)
                RemovePause(npc);
            ResetState();
            return;
        }

        bool wasPaused = paused;
        managed = true;
        paused = bitReader.ReadBit();
        originalDontTakeDamage = bitReader.ReadBit();
        remainingTicks = binaryReader.ReadInt32();

        if (paused)
            ApplyPause(npc);
        else if (wasPaused)
            RemovePause(npc);
    }

    private static void ApplyPause(NPC npc)
    {
        npc.velocity = Vector2.Zero;
        npc.dontTakeDamage = true;
    }

    private void End(NPC npc)
    {
        remainingTicks = 0;
        paused = false;
        RemovePause(npc);
        npc.netUpdate = true;
        Log.Info($"Boss grace period ended for NPC type={npc.type}, index={npc.whoAmI}.");
    }

    private void RemovePause(NPC npc)
    {
        npc.dontTakeDamage = originalDontTakeDamage;
    }

    private void ResetState()
    {
        remainingTicks = 0;
        managed = false;
        paused = false;
        originalDontTakeDamage = false;
    }
}
