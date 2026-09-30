using PvPArenas.Common.Game.LoadoutSelector;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria.ID;

namespace PvPArenas.Common.Game.BossVoting;

/// <summary>Server-owned ballots with stable boss choices and a completed result for the UI.</summary>
internal sealed class BossVoteSystem : ModSystem
{
    private readonly Dictionary<int, int> votes = [];
    private readonly List<byte>[] voters = [[], [], [], []];

    internal uint BallotId { get; private set; }
    internal bool Active { get; private set; }
    internal int Winner { get; private set; } = -1;
    internal int DurationTicks { get; private set; }

    internal IReadOnlyList<byte> VotersFor(int option) =>
        option >= 0 && option < FightPresets.Count ? voters[option] : Array.Empty<byte>();

    internal int VoteCount(int option) => VotersFor(option).Count;
    internal int TotalVotes => voters.Sum(group => group.Count);
    internal bool AllPlayersVoted => Active && votes.Count > 0
        && Main.player.Where(player => player?.active == true).All(player => votes.ContainsKey(player.whoAmI));
    internal int LocalVote => Array.FindIndex(voters, group => group.Contains((byte)Main.myPlayer));

    internal static void RequestVote(int option)
    {
        BossVoteSystem system = ModContent.GetInstance<BossVoteSystem>();
        if (!system.Active || option < 0 || option >= FightPresets.Count)
            return;

        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            ModPacket packet = ModContent.GetInstance<PvPArenas>().GetPacket();
            packet.Write((byte)PvPArenas.PacketType.CastVote);
            packet.Write(system.BallotId);
            packet.Write((byte)option);
            packet.Send();
            return;
        }

        system.CastVote(Main.myPlayer, system.BallotId, option);
    }

    internal void CastVote(int playerId, uint ballotId, int option)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient || !Active || ballotId != BallotId
            || !ModContent.GetInstance<RoundManager>().IsVoting
            || playerId < 0 || playerId >= Main.maxPlayers || Main.player[playerId]?.active != true
            || option < 0 || option >= FightPresets.Count)
            return;
        if (votes.TryGetValue(playerId, out int previous) && previous == option)
            return;

        votes[playerId] = option;
        RebuildVoters();
        Sync();
    }

    internal void Start(int durationTicks)
    {
        Reset();
        BallotId++;
        Active = true;
        DurationTicks = Math.Max(1, durationTicks);
    }

    internal void Reset()
    {
        Active = false;
        Winner = -1;
        DurationTicks = 0;
        votes.Clear();
        foreach (List<byte> group in voters)
            group.Clear();
    }

    internal int Complete()
    {
        if (Winner >= 0)
            return Winner;

        RebuildVoters();
        int best = voters.Max(group => group.Count);
        int[] winners = Enumerable.Range(0, FightPresets.Count)
            .Where(index => voters[index].Count == best).ToArray();
        Winner = winners[Main.rand.Next(winners.Length)];
        Active = false;
        return Winner;
    }

    public override void PostUpdatePlayers()
    {
        if (Main.netMode == NetmodeID.MultiplayerClient || !Active)
            return;

        int[] departed = votes.Keys.Where(id => Main.player[id]?.active != true).ToArray();
        if (departed.Length == 0)
            return;
        foreach (int id in departed)
            votes.Remove(id);
        RebuildVoters();
        Sync();
    }

    private void RebuildVoters()
    {
        foreach (List<byte> group in voters)
            group.Clear();
        foreach ((int id, int option) in votes.OrderBy(pair => pair.Key))
            if (Main.player[id]?.active == true)
                voters[option].Add((byte)id);
    }

    private static void Sync()
    {
        if (Main.netMode == NetmodeID.Server)
            NetMessage.SendData(MessageID.WorldData);
    }

    public override void ClearWorld()
    {
        Reset();
        BallotId = 0;
    }

    public override void NetSend(BinaryWriter writer)
    {
        writer.Write(BallotId);
        writer.Write(Active);
        writer.Write((sbyte)Winner);
        writer.Write(DurationTicks);
        foreach (List<byte> group in voters)
        {
            writer.Write((byte)group.Count);
            foreach (byte voter in group)
                writer.Write(voter);
        }
    }

    public override void NetReceive(BinaryReader reader)
    {
        BallotId = reader.ReadUInt32();
        Active = reader.ReadBoolean();
        Winner = reader.ReadSByte();
        DurationTicks = Math.Max(1, reader.ReadInt32());
        foreach (List<byte> group in voters)
        {
            group.Clear();
            for (int count = reader.ReadByte(); count > 0; count--)
                group.Add(reader.ReadByte());
        }
    }
}
