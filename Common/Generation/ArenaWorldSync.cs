using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria.DataStructures;
using Terraria.ID;

namespace PvPArenas.Common.Generation;

/// <summary>A round can start only after every connected client verifies the complete replacement world.</summary>
internal sealed class ArenaWorldSync : ModSystem
{
    private enum Message : byte { Begin, Verify, Acknowledge }
    private sealed class Recipient
    {
        internal int NextSection;
        internal int Attempt;
        internal long SentAt;
        internal bool Verified;
        internal bool RequiresAcknowledgement;
        internal object Socket;
    }

    private const int SectionsPerTick = 8;
    private const int AcknowledgeTimeoutMs = 10_000;
    private const int MaximumAttempts = 3;
    private static readonly Dictionary<int, Recipient> recipients = new();
    private static ulong[] expected;
    private static uint revision;
    private static uint clientRevision;
    private static byte clientAttempt;
    private static bool clientStarted;
    private static bool running;
    private static int surface, rock, spawnX, spawnY;

    internal static bool IsRunning => running;

    internal static void Begin()
    {
        if (Main.netMode != NetmodeID.Server)
            throw new InvalidOperationException("Only the server can publish an arena world.");
        Cancel();
        revision++;
        // WorldData transmits these compact-world layer values as integers, not Double.
        surface = (int)Main.worldSurface;
        rock = (int)Main.rockLayer;
        spawnX = Main.spawnTileX;
        spawnY = Main.spawnTileY;
        expected = Enumerable.Range(0, SectionCount).Select(index => ArenaTileSync.Fingerprint(SectionBounds(index))).ToArray();
        running = true;
        Log.Debug($"[worldgen] Publishing revision {revision} | Tiles: {Main.maxTilesX * Main.maxTilesY} | Sections: {expected.Length}");
    }

    internal static bool Advance(out string failure)
    {
        failure = "";
        if (!running)
            return true;
        // Include arrivals during publication; disconnected recipients cannot block a round.
        foreach (int id in recipients.Keys.ToArray())
            if (!Connected(id) || !ReferenceEquals(recipients[id].Socket, Netplay.Clients[id].Socket))
                recipients.Remove(id);
        for (int id = 0; id < Main.maxPlayers; id++)
            if (Connected(id) && !recipients.ContainsKey(id))
            {
                var recipient = new Recipient
                {
                    Socket = Netplay.Clients[id].Socket,
                    // Reese's owned recording socket records packets but has no receiving player.
                    RequiresAcknowledgement = !ErkySSC.Core.Compat.ReeseCompat.IsRecordingClient(id, Netplay.Clients[id].Socket)
                };
                recipients.Add(id, recipient);
                StartAttempt(id, recipient);
            }

        int budget = SectionsPerTick;
        foreach ((int id, Recipient recipient) in recipients)
        {
            if (recipient.Verified)
                continue;
            if (recipient.NextSection == expected.Length)
            {
                if (Environment.TickCount64 - recipient.SentAt < AcknowledgeTimeoutMs)
                    continue;
                if (recipient.Attempt >= MaximumAttempts)
                {
                    failure = $"Could not verify the replacement arena for {Main.player[id].name}. The round has not started.";
                    Log.Warn($"[worldgen] FAIL | Revision: {revision} | Client: {id} | Attempts: {recipient.Attempt} | No verified world acknowledgement");
                    Cancel();
                    return true;
                }
                StartAttempt(id, recipient);
            }
            while (budget > 0 && recipient.NextSection < expected.Length)
            {
                Rectangle bounds = SectionBounds(recipient.NextSection++);
                ArenaTileSync.SendSection(id, bounds);
                Netplay.Clients[id].TileSections[bounds.X / 200, bounds.Y / 150] = true;
                budget--;
            }
            if (recipient.NextSection == expected.Length && recipient.SentAt == 0)
            {
                // Frame only after the complete world has arrived, not against old adjacent sections.
                ArenaTileSync.SendFrames(id, 0, 0, Columns - 1, Rows - 1);
                ModPacket packet = Packet(Message.Verify, recipient.Attempt);
                packet.Send(id);
                recipient.SentAt = Environment.TickCount64;
                if (!recipient.RequiresAcknowledgement)
                    recipient.Verified = true;
            }
        }
        if (recipients.Values.Any(recipient => !recipient.Verified))
            return false;
        int clients = recipients.Values.Count(recipient => recipient.RequiresAcknowledgement);
        Log.Debug($"[worldgen] PASS | Revision: {revision} | ClientsVerified: {clients}/{clients} | TilesVerifiedPerClient: {Main.maxTilesX * Main.maxTilesY} | RecordersPublished: {recipients.Count - clients}");
        running = false;
        return true;
    }

    private static void StartAttempt(int id, Recipient recipient)
    {
        recipient.Attempt++;
        recipient.NextSection = 0;
        recipient.SentAt = 0;
        recipient.Verified = false;
        NetMessage.SendData(MessageID.WorldData, id);
        ModPacket packet = Packet(Message.Begin, recipient.Attempt);
        packet.Write((ushort)Main.maxTilesX);
        packet.Write((ushort)Main.maxTilesY);
        packet.Send(id);
        if (recipient.Attempt > 1)
            Log.Warn($"[worldgen] Retrying complete world | Revision: {revision} | Client: {id} | Attempt: {recipient.Attempt}/{MaximumAttempts}");
    }

    internal static void HandlePacket(BinaryReader reader, int sender)
    {
        Message message = (Message)reader.ReadByte();
        uint receivedRevision = reader.ReadUInt32();
        byte attempt = reader.ReadByte();
        int width = 0, height = 0;
        ulong[] receivedHashes = null;
        int receivedSurface = 0, receivedRock = 0, receivedSpawnX = 0, receivedSpawnY = 0;
        if (message == Message.Begin)
        {
            width = reader.ReadUInt16();
            height = reader.ReadUInt16();
        }
        else if (message == Message.Acknowledge)
        {
            int count = reader.ReadUInt16();
            if (count > 1024) throw new InvalidDataException("Invalid arena section acknowledgement length.");
            receivedHashes = new ulong[count];
            for (int index = 0; index < count; index++) receivedHashes[index] = reader.ReadUInt64();
            if (count > 0)
            {
                receivedSurface = reader.ReadInt32();
                receivedRock = reader.ReadInt32();
                receivedSpawnX = reader.ReadInt32();
                receivedSpawnY = reader.ReadInt32();
            }
        }
        if (Main.netMode == NetmodeID.Server)
        {
            if (message != Message.Acknowledge || !running || receivedRevision != revision
                || !recipients.TryGetValue(sender, out Recipient recipient) || attempt != recipient.Attempt)
                return;
            // Consume even obsolete replies fully: tML requires each mod packet's reader
            // to finish at its packet boundary, including acknowledgements from old attempts.
            int count = receivedHashes.Length;
            if (count != expected.Length)
            {
                recipient.SentAt = Environment.TickCount64 - AcknowledgeTimeoutMs;
                return;
            }
            int matching = 0, tiles = 0;
            for (int index = 0; index < count; index++)
                if (receivedHashes[index] == expected[index])
                {
                    matching++;
                    Rectangle bounds = SectionBounds(index);
                    tiles += bounds.Width * bounds.Height;
                }
            bool metadata = receivedSurface == surface && receivedRock == rock
                && receivedSpawnX == spawnX && receivedSpawnY == spawnY;
            recipient.Verified = matching == count && metadata;
            Log.Debug($"[worldgen] {(recipient.Verified ? "PASS" : "FAIL")} | Revision: {revision} | Client: {sender} | SectionsVerified: {matching}/{count} | TilesVerifiedToWork: {tiles}/{Main.maxTilesX * Main.maxTilesY} | Metadata: {(metadata ? "PASS" : "FAIL")}");
            if (!recipient.Verified)
                recipient.SentAt = Environment.TickCount64 - AcknowledgeTimeoutMs;
            return;
        }
        if (Main.netMode != NetmodeID.MultiplayerClient)
            return;
        if (message == Message.Begin)
        {
            clientRevision = receivedRevision;
            clientAttempt = attempt;
            clientStarted = width == Main.maxTilesX && height == Main.maxTilesY;
            if (!clientStarted)
                return;
            // TileSection adds the entities it contains but does not remove old ones.
            // This publication covers the entire world, including the occupied lobby.
            Array.Clear(Main.chest);
            Array.Clear(Main.sign);
            TileEntity.ByID.Clear();
            TileEntity.ByPosition.Clear();
            Main.LocalPlayer.chest = -1;
            Main.LocalPlayer.sign = -1;
            Main.LocalPlayer.tileEntityAnchor.Clear();
            Main.Map?.Clear();
            Main.clearMap = true;
        }
        else if (message == Message.Verify)
        {
            bool valid = clientStarted && clientRevision == receivedRevision && clientAttempt == attempt;
            ModPacket packet = ModContent.GetInstance<PvPArenas>().GetPacket();
            packet.Write((byte)PvPArenas.PacketType.ArenaWorldSync);
            packet.Write((byte)Message.Acknowledge);
            packet.Write(receivedRevision);
            packet.Write(attempt);
            packet.Write((ushort)(valid ? SectionCount : 0));
            if (valid)
            {
                for (int index = 0; index < SectionCount; index++)
                    packet.Write(ArenaTileSync.Fingerprint(SectionBounds(index)));
                packet.Write((int)Main.worldSurface);
                packet.Write((int)Main.rockLayer);
                packet.Write(Main.spawnTileX);
                packet.Write(Main.spawnTileY);
                Main.instance.ClearCachedTileDraws();
                Lighting.Clear();
                Main.Map?.Clear();
                Main.clearMap = true;
                Main.updateMap = true;
                Main.instance.waterfallManager?.FindWaterfalls(true);
            }
            packet.Send();
        }
    }

    private static ModPacket Packet(Message message, int attempt)
    {
        ModPacket packet = ModContent.GetInstance<PvPArenas>().GetPacket();
        packet.Write((byte)PvPArenas.PacketType.ArenaWorldSync);
        packet.Write((byte)message);
        packet.Write(revision);
        packet.Write((byte)attempt);
        return packet;
    }

    private static int Columns => (Main.maxTilesX + 199) / 200;
    private static int Rows => (Main.maxTilesY + 149) / 150;
    private static int SectionCount => Columns * Rows;
    internal static Rectangle SectionBounds(int index)
    {
        int x = index % Columns * 200, y = index / Columns * 150;
        return new Rectangle(x, y, Math.Min(200, Main.maxTilesX - x), Math.Min(150, Main.maxTilesY - y));
    }
    private static bool Connected(int id) => Netplay.Clients[id].IsConnected() && Netplay.Clients[id].State >= 3;
    internal static void Cancel()
    {
        running = false;
        expected = null;
        recipients.Clear();
    }
    public override void OnWorldUnload()
    {
        Cancel();
        clientStarted = false;
    }
}
