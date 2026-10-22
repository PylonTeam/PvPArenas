using System.IO;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader.IO;

namespace PvPArenas.Common.CultistArena;

internal sealed class CultistArenaProjectile : GlobalProjectile
{
    internal int EncounterId;
    internal int RootIndex = -1;
    internal Rectangle Bounds;

    public override bool InstancePerEntity => true;

    public override void SetDefaults(Projectile projectile)
    {
        EncounterId = 0;
        RootIndex = -1;
        Bounds = Rectangle.Empty;
    }

    public override void OnSpawn(Projectile projectile, IEntitySource source)
    {
        if (!CultistArenaEncounter.TryGetParent(source, out int encounter, out int root, out Rectangle bounds))
            return;
        EncounterId = encounter;
        RootIndex = root;
        Bounds = bounds;
        projectile.netUpdate = true;
    }

    public override bool PreAI(Projectile projectile)
    {
        if (EncounterId == 0 || projectile.type != ProjectileID.CultistRitual
            || (uint)RootIndex >= Main.maxNPCs || Main.npc[RootIndex]?.active != true)
            return true;

        // Keep a paused ritual synchronized with the paused body and clones.
        // Let other attacks expire normally while everybody is respawning.
        if (CultistArenaEncounter.FindTarget(Main.npc[RootIndex], Bounds) >= 0)
            return true;
        projectile.timeLeft++;
        return false;
    }

    public override void SendExtraAI(Projectile projectile, BitWriter bitWriter, BinaryWriter binaryWriter)
    {
        bitWriter.WriteBit(EncounterId != 0);
        if (EncounterId == 0)
            return;
        binaryWriter.Write(EncounterId);
        binaryWriter.Write((short)RootIndex);
        binaryWriter.Write(Bounds.X);
        binaryWriter.Write(Bounds.Y);
        binaryWriter.Write(Bounds.Width);
        binaryWriter.Write(Bounds.Height);
    }

    public override void ReceiveExtraAI(Projectile projectile, BitReader bitReader, BinaryReader binaryReader)
    {
        if (!bitReader.ReadBit())
        {
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
}
