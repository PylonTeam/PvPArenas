using System;
using System.IO;
using PvPArenas.Common.Game;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader.IO;

namespace PvPArenas.Common.CultistArena;

/// <summary>Tracks this encounter through vanilla NPC and projectile spawn sources.</summary>
internal sealed class CultistArenaNPC : GlobalNPC
{
    internal int EncounterId;
    internal int RootIndex = -1;
    internal Rectangle Bounds;
    private bool targetPaused;
    private bool originalDontTakeDamage;
    private Vector2 originalVelocity;

    public override bool InstancePerEntity => true;
    internal bool Managed => EncounterId != 0;

    public override void Load() => On_NPC.DoDeathEvents += OnDeathEvents;

    public override void Unload() => On_NPC.DoDeathEvents -= OnDeathEvents;

    public override void SetDefaults(NPC npc)
    {
        EncounterId = 0;
        RootIndex = -1;
        Bounds = Rectangle.Empty;
        targetPaused = false;
    }

    public override void OnSpawn(NPC npc, IEntitySource source)
    {
        if (CultistArenaEncounter.TryGetParent(source, out int encounter, out int root, out Rectangle bounds))
        {
            EncounterId = encounter;
            RootIndex = root;
            Bounds = bounds;
            npc.netUpdate = true;
        }
    }

    public override bool PreAI(NPC npc)
    {
        if (!Managed || npc.type is not (NPCID.CultistBoss or NPCID.CultistBossClone))
            return true;

        // Grace already owns invulnerability and suppresses native AI. Avoid
        // capturing its temporary immunity as the state to restore after a wipe.
        if (npc.GetGlobalNPC<BossGraceNPC>().Paused)
            return true;

        if (!CultistArenaEncounter.TryGetRoot(this, out NPC root))
            return true;

        int target = CultistArenaEncounter.FindTarget(root, Bounds);
        if (target < 0)
        {
            Pause(npc);
            return false;
        }

        // Vanilla directly destroys the body and clones beyond 5,600 pixels.
        // Recover before that branch, preserving both virtual health pools and AI.
        if (Vector2.DistanceSquared(root.Center, Main.player[target].Center) > 5200f * 5200f
            || Vector2.DistanceSquared(npc.Center, Main.player[target].Center) > 5500f * 5500f)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                Pause(npc);
                return false; // Wait for the authoritative formation position.
            }

            if (!CultistArenaEncounter.MoveFormation(root, this, Main.player[target].Center + new Vector2(0f, -300f)))
            {
                Pause(npc);
                return false;
            }
        }

        Resume(npc);
        npc.target = target;
        npc.timeLeft = Math.Max(npc.timeLeft, 3600);
        return true;
    }

    public override void PostAI(NPC npc)
    {
        if (targetPaused)
            npc.velocity = Vector2.Zero;
        else if (Managed && npc.type == NPCID.CultistBoss && Main.netMode != NetmodeID.MultiplayerClient)
            CultistArenaEncounter.MoveFormation(npc, this, npc.Center);
    }

    public override bool CheckActive(NPC npc) => !Managed;

    public override bool CanHitPlayer(NPC npc, Player target, ref int cooldownSlot) => !targetPaused;

    public override void SendExtraAI(NPC npc, BitWriter bitWriter, BinaryWriter binaryWriter)
    {
        bitWriter.WriteBit(Managed);
        if (!Managed)
            return;
        binaryWriter.Write(EncounterId);
        binaryWriter.Write((short)RootIndex);
        binaryWriter.Write(Bounds.X);
        binaryWriter.Write(Bounds.Y);
        binaryWriter.Write(Bounds.Width);
        binaryWriter.Write(Bounds.Height);
    }

    public override void ReceiveExtraAI(NPC npc, BitReader bitReader, BinaryReader binaryReader)
    {
        if (!bitReader.ReadBit())
        {
            Resume(npc);
            EncounterId = 0;
            RootIndex = -1;
            Bounds = Rectangle.Empty;
            return;
        }
        EncounterId = binaryReader.ReadInt32();
        RootIndex = binaryReader.ReadInt16();
        Bounds = new Rectangle(binaryReader.ReadInt32(), binaryReader.ReadInt32(),
            binaryReader.ReadInt32(), binaryReader.ReadInt32());
    }

    private void Pause(NPC npc)
    {
        if (!targetPaused)
        {
            originalDontTakeDamage = npc.dontTakeDamage;
            originalVelocity = npc.velocity;
            targetPaused = true;
        }
        npc.velocity = Vector2.Zero;
        npc.dontTakeDamage = true;
        npc.timeLeft = Math.Max(npc.timeLeft, 3600);
    }

    private void Resume(NPC npc)
    {
        if (!targetPaused)
            return;
        npc.dontTakeDamage = originalDontTakeDamage;
        npc.velocity = originalVelocity;
        targetPaused = false;
        if (Main.netMode != NetmodeID.MultiplayerClient)
            npc.netUpdate = true;
    }

    private static void OnDeathEvents(On_NPC.orig_DoDeathEvents orig, NPC npc, Player closestPlayer)
    {
        // NPCLoader.OnKill and ordinary loot execute before DoDeathEvents.
        // Skip only this managed Cultist's world progression, including the
        // synchronous Lunar Apocalypse spawn; ordinary Cultists retain vanilla behavior.
        if (npc.type == NPCID.CultistBoss && npc.TryGetGlobalNPC(out CultistArenaNPC arena)
            && arena.Managed && arena.RootIndex == npc.whoAmI)
            return;
        orig(npc, closestPlayer);
    }
}
