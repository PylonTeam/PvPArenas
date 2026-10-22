using System;
using System.IO;
using System.Linq;
using ErkySSC.Core.Inventory;
using PvPArenas.Common.Game;
using PvPArenas.Common.Game.LoadoutSelector;
using PvPArenas.Core.Configs;
using PvPArenas.Core.Inventory;
using Terraria.ID;

namespace PvPArenas.Common.Loadouts;

/// <summary>Collect slot preferences before granting a kit, then acknowledge its installation.</summary>
internal sealed class ArenaLoadoutDelivery
{
    private readonly Player owner;
    private int revision, requestedBoss, requestedIndex;
    private int[] preferences;
    private bool requestingPreferences, requestSent, awaitingAcknowledgement;
    private bool resetBuffsOnGrant;
    private long requestedAt;
    private int receivedRevision;
    private object ownerSocket;

    internal bool Pending => requestingPreferences || awaitingAcknowledgement;
    internal bool AwaitingGrant => receivedRevision == 0;

    internal ArenaLoadoutDelivery(Player owner) => this.owner = owner;

    internal void BeginRound()
    {
        ArenaRoundBuffs.Clear(owner);
        resetBuffsOnGrant = true;
    }

    internal void Reset()
    {
        if (requestedBoss != 0 || Pending) revision++;
        requestedBoss = 0;
        preferences = null;
        requestingPreferences = requestSent = awaitingAcknowledgement = false;
        resetBuffsOnGrant = false;
    }

    internal static bool RequestPreferences(Player player, BossFightPreset preset)
    {
        if (preset.IsSandbox()) return true;
        ArenaPlayer owner = player.GetModPlayer<ArenaPlayer>();
        ArenaLoadoutDelivery delivery = owner.Delivery;
        object socket = Netplay.Clients[player.whoAmI]?.Socket;
        if (!ReferenceEquals(delivery.ownerSocket, socket))
        {
            delivery.Reset();
            delivery.ownerSocket = socket;
        }
        int index = owner.SelectedLoadoutIndex;
        if (delivery.requestedBoss != preset.Boss.Type || delivery.requestedIndex != index)
        {
            delivery.SetPreferences(preset, index, null);
            if (HasOwner(player))
            {
                delivery.requestingPreferences = true;
                delivery.requestedAt = Environment.TickCount64;
            }
        }
        if (delivery.requestingPreferences && !delivery.requestSent && Connected(player))
        {
            delivery.requestSent = true;
            ModPacket packet = ModContent.GetInstance<PvPArenas>().GetPacket();
            packet.Write((byte)PvPArenas.PacketType.RequestLoadoutPreferences);
            packet.Write(preset.Boss.Type);
            packet.Write(index);
            packet.Write(delivery.revision);
            packet.Send(player.whoAmI);
        }
        // A missing reply cannot hold generation or a late join indefinitely.
        if (delivery.requestingPreferences && (!HasOwner(player)
            || Environment.TickCount64 - delivery.requestedAt >= 5_000))
            delivery.requestingPreferences = false;
        return !delivery.requestingPreferences;
    }

    internal void SetPreferences(BossFightPreset preset, int index, int[] positions)
    {
        // Validate packing before touching the player's live inventory.
        preferences = LoadoutSlotLayout.Types(LoadoutSlotLayout.Arrange(
            ArenaPlayer.ResolveBaseLoadout(preset, index), positions, owner));
        requestedBoss = preset.Boss.Type;
        requestedIndex = index;
        revision++;
        requestingPreferences = requestSent = awaitingAcknowledgement = false;
    }

    internal Loadout Arrange(BossFightPreset preset, int index) => LoadoutSlotLayout.Arrange(
        ArenaPlayer.ResolveBaseLoadout(preset, index),
        requestedBoss == preset.Boss.Type && requestedIndex == index ? preferences : null, owner);

    internal void SendGrant(Player player, BossFightPreset preset)
    {
        ownerSocket = Netplay.Clients[player.whoAmI]?.Socket;
        int index = player.GetModPlayer<ArenaPlayer>().SelectedLoadoutIndex;
        if (requestedBoss != preset.Boss.Type || requestedIndex != index)
            SetPreferences(preset, index, null);
        awaitingAcknowledgement = Connected(player);
        PlayerLoadouts.Synchronize(player);
        NetMessage.SendData(MessageID.PlayerLifeMana, number: player.whoAmI);
        NetMessage.SendData(MessageID.PlayerMana, number: player.whoAmI);
        if (!awaitingAcknowledgement)
        {
            resetBuffsOnGrant = false;
            return;
        }
        ModPacket packet = ModContent.GetInstance<PvPArenas>().GetPacket();
        packet.Write((byte)PvPArenas.PacketType.LoadoutApplied);
        packet.Write(preset.Boss.Type);
        packet.Write(index);
        packet.Write(revision);
        packet.Write(player.statLife);
        packet.Write(player.statMana);
        packet.Write(resetBuffsOnGrant);
        // An owner's vanilla equipment packets are ignored when global SSC is off.
        CarriedItemSnapshot.Write(packet, player);
        packet.Send(player.whoAmI);
        resetBuffsOnGrant = false;
    }

    internal static void HandleRequest(BinaryReader reader)
    {
        int boss = reader.ReadInt32(), index = reader.ReadInt32(), revision = reader.ReadInt32();
        if (Main.netMode != NetmodeID.MultiplayerClient || !ArenaRespawns.IsInitialParticipant(Main.LocalPlayer)) return;
        // The request can precede the round's selected-preset sync.
        ServerConfig config = ModContent.GetInstance<ServerConfig>();
        BossFightPreset preset = Enumerable.Range(0, FightPresets.Count)
            .Select(config.GetFightPreset).FirstOrDefault(p => p?.Boss?.Type == boss);
        if (preset == null || preset.IsSandbox() || !ArenaPlayer.IsValidLoadoutIndex(preset, index)) return;
        ModPacket packet = ModContent.GetInstance<PvPArenas>().GetPacket();
        packet.Write((byte)PvPArenas.PacketType.LoadoutPreferences);
        packet.Write(boss);
        packet.Write(index);
        packet.Write(revision);
        WritePreferences(packet, preset, index);
        packet.Send();
    }

    internal static void WritePreferences(BinaryWriter writer, BossFightPreset preset, int index)
    {
        foreach (int type in LocalLoadoutPositions.Get(preset, index, ArenaPlayer.ResolveBaseLoadout(preset, index)))
            writer.Write(type);
    }

    internal static int[] ReadPreferences(BinaryReader reader)
    {
        int[] positions = new int[LoadoutSlotLayout.PositionCount];
        for (int i = 0; i < positions.Length; i++) positions[i] = reader.ReadInt32();
        return positions;
    }

    internal static void HandlePreferences(int sender, BinaryReader reader)
    {
        int boss = reader.ReadInt32(), index = reader.ReadInt32(), revision = reader.ReadInt32();
        int[] positions = ReadPreferences(reader);
        if (!TryOwner(sender, boss, index, out Player player, out BossFightPreset preset)) return;
        ArenaLoadoutDelivery delivery = player.GetModPlayer<ArenaPlayer>().Delivery;
        if (!delivery.requestingPreferences || delivery.revision != revision
            || delivery.requestedBoss != boss || delivery.requestedIndex != index) return;
        delivery.preferences = LoadoutSlotLayout.Types(LoadoutSlotLayout.Arrange(
            ArenaPlayer.ResolveBaseLoadout(preset, index), positions, player));
        delivery.requestingPreferences = false;
    }

    internal static void HandleAcknowledgement(int sender, BinaryReader reader)
    {
        int boss = reader.ReadInt32(), index = reader.ReadInt32(), revision = reader.ReadInt32();
        if (!TryOwner(sender, boss, index, out Player player, out _)) return;
        ArenaLoadoutDelivery delivery = player.GetModPlayer<ArenaPlayer>().Delivery;
        if (delivery.awaitingAcknowledgement && delivery.revision == revision
            && delivery.requestedBoss == boss && delivery.requestedIndex == index)
            delivery.awaitingAcknowledgement = false;
    }

    internal static void ReceiveGrant(BinaryReader reader)
    {
        int boss = reader.ReadInt32(), index = reader.ReadInt32(), revision = reader.ReadInt32();
        int life = reader.ReadInt32(), mana = reader.ReadInt32();
        bool resetBuffs = reader.ReadBoolean();
        Player player = Main.LocalPlayer;
        CarriedItemSnapshot snapshot = CarriedItemSnapshot.Read(reader, player);
        RoundManager manager = ModContent.GetInstance<RoundManager>();
        if (Main.netMode != NetmodeID.MultiplayerClient
            || manager.CurrentPhase is not (RoundManager.RoundPhase.FreezeCountdown or RoundManager.RoundPhase.Playing)
            || !ArenaRespawns.IsInitialParticipant(player)
            || !manager.TryGetSelectedPreset(out BossFightPreset preset) || preset.Boss.Type != boss
            || preset.IsSandbox() || !ArenaPlayer.IsValidLoadoutIndex(preset, index)) return;
        ArenaPlayer owner = player.GetModPlayer<ArenaPlayer>();
        ArenaLoadoutDelivery delivery = owner.Delivery;
        if (revision <= delivery.receivedRevision) return;
        LocalInventoryPositions.Stop();
        if (resetBuffs)
        {
            ArenaRoundBuffs.Clear(player);
            if (Main.clientPlayer != null) ArenaRoundBuffs.Clear(Main.clientPlayer);
        }
        snapshot.Apply(player);
        if (Main.clientPlayer != null) snapshot.Apply(Main.clientPlayer);
        Main.mouseItem = player.inventory[58].Clone();
        Array.Clear(player.inventoryChestStack);
        player.dead = false;
        player.respawnTimer = 0;
        player.selectedItem = player.itemAnimation = player.itemTime = 0;
        player.statLife = Math.Clamp(life, 1, Math.Clamp(preset.MaxHealth, 1, 500));
        player.statMana = Math.Clamp(mana, 0, Math.Clamp(preset.MaxMana, 0, 200));
        if (Main.clientPlayer != null)
        {
            Main.clientPlayer.statLife = player.statLife;
            Main.clientPlayer.statMana = player.statMana;
        }
        delivery.receivedRevision = revision;
        owner.MarkLoadoutReceived(index);
        LocalInventoryPositions.Begin(preset, index);
        ModPacket packet = ModContent.GetInstance<PvPArenas>().GetPacket();
        packet.Write((byte)PvPArenas.PacketType.AcknowledgeLoadout);
        packet.Write(boss);
        packet.Write(index);
        packet.Write(revision);
        packet.Send();
    }

    private static bool TryOwner(int sender, int boss, int index, out Player player, out BossFightPreset preset)
    {
        player = sender >= 0 && sender < Main.maxPlayers ? Main.player[sender] : null;
        preset = null;
        RoundManager manager = ModContent.GetInstance<RoundManager>();
        return Main.netMode == NetmodeID.Server && ArenaRespawns.IsInitialParticipant(player)
            && manager.CurrentPhase is RoundManager.RoundPhase.Generating or RoundManager.RoundPhase.FreezeCountdown or RoundManager.RoundPhase.Playing
            && manager.TryGetSelectedPreset(out preset) && preset.Boss.Type == boss && !preset.IsSandbox()
            && ArenaPlayer.IsValidLoadoutIndex(preset, index)
            && player.GetModPlayer<ArenaPlayer>().SelectedLoadoutIndex == index;
    }

    private static bool HasOwner(Player player) => player.whoAmI >= 0 && player.whoAmI < Main.maxPlayers
        && Netplay.Clients[player.whoAmI]?.IsConnected() == true
        && !ErkySSC.Core.Compat.ReeseCompat.IsRecordingClient(player.whoAmI, Netplay.Clients[player.whoAmI].Socket);

    private static bool Connected(Player player) => HasOwner(player) && Netplay.Clients[player.whoAmI].State == 10;
}
