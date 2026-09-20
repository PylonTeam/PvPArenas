using PvPFramework.Common.EndScreen;
using Pylon.Common.Authentication;
using Pylon.Common.MainMenu.API;
using Pylon.Common.MainMenu.API.MatchHistory;
using Pylon.Common.MainMenu.API.Profile;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Terraria.Enums;
using Terraria.ID;
using CompletedMatchPayload = Pylon.Common.MainMenu.API.MatchHistory.MatchApi.CompletedMatchPayload;
using MatchPayload = Pylon.Common.MainMenu.API.MatchHistory.MatchApi.MatchPayload;
using MatchPlayerPayload = Pylon.Common.MainMenu.API.MatchHistory.MatchApi.MatchPlayerPayload;
using MatchTeamPayload = Pylon.Common.MainMenu.API.MatchHistory.MatchApi.MatchTeamPayload;

namespace PvPArenas.Common.Game.Score;

/// <summary>
/// Posts completed Arenas rounds to Tavernkeep, mirroring PvPAdventure's MatchReporter but for
/// the "arenas" game mode. Tavernkeep already accepts this mode (see match.go UnmarshalJSON),
/// so no server change is required. Only official dedicated servers post; Steam identities come
/// from Pylon's authentication.
///
/// Every exit path reports a terminal gem result back to the end screen. The screen opens in
/// <see cref="EndScreenGemStatus.Pending"/> and would otherwise sit on "Waiting for Tavernkeep
/// confirmation..." forever.
/// </summary>
internal static class MatchReporter
{
    private const string GameMode = "arenas";
    private const string MatchTokenMetric = "match_token";

    // Stat keys match PvPAdventure's StatsReporter so Tavernkeep aggregates both modes alike.
    private const string DamageDealtStat = "damage_dealt";
    private const string DamageTakenStat = "damage_taken";
    private const string BossDamageDealtStat = "boss_damage_dealt";

    internal readonly record struct ReportPlayer(
        int PlayerIndex,
        Team Team,
        string Name,
        int Kills,
        int Deaths,
        long Damage,
        long DamageTaken,
        long BossDamage,
        IReadOnlyDictionary<int, uint> BossDamageByItem,
        uint Reward,
        bool Winner);

    /// <param name="presentationKey">
    /// The end screen's presentation key, reused as the match token so late gem confirmations
    /// find the snapshot they belong to.
    /// </param>
    internal static void PostCompletedMatch(
        string presentationKey,
        DateTime startUtc,
        DateTime endUtc,
        int bossType,
        Team winningTeam,
        IReadOnlyList<ReportPlayer> players)
    {
        if (players is not { Count: > 0 })
            return;

        try
        {
            Report(presentationKey, startUtc, endUtc, bossType, winningTeam, players);
        }
        catch (Exception exception)
        {
            Log.Error($"[Arenas match] Reporting threw: {exception}");
            ReportToAll(presentationKey, players, EndScreenGemResult.Failed(
                0, $"Unexpected {exception.GetType().Name}: {exception.Message}"));
        }
    }

    private static void Report(
        string presentationKey,
        DateTime startUtc,
        DateTime endUtc,
        int bossType,
        Team winningTeam,
        IReadOnlyList<ReportPlayer> players)
    {
        if (Main.netMode != NetmodeID.Server)
        {
            Log.Info("[Arenas match] Not a dedicated server; the round was not sent to Tavernkeep.");
            ReportToAll(presentationKey, players, EndScreenGemResult.NotPosted(
                "This round was not played on an official server."));
            return;
        }

        string matchToken = string.IsNullOrWhiteSpace(presentationKey)
            ? Guid.NewGuid().ToString("N")
            : presentationKey;
        // MatchTeamPayload.Bosses is a short list, so a modded NPC type above that range is
        // reported as "no boss" rather than wrapping into an unrelated id.
        short boss = bossType is > 0 and <= short.MaxValue ? (short)bossType : (short)0;

        // Unofficial servers have no Steam identities at all, so a missing one there is expected
        // rather than a per-player failure worth reporting.
        bool isOfficial = global::Pylon.Pylon.IsOfficial;
        SteamAuthentication auth = ModContent.GetInstance<SteamAuthentication>();
        Dictionary<ulong, MatchPlayerPayload> payloadPlayers = [];
        List<GemRecipient> recipients = [];
        HashSet<ulong> seenSteamIds = [];

        foreach (ReportPlayer player in players)
        {
            if (!TryGetSteamId(auth, player, out ulong steamId))
            {
                Log.Warn($"[Arenas match] Skipping {player.Name}: no Steam identity for slot {player.PlayerIndex}.");
                if (isOfficial)
                    QueueGemResult(presentationKey, player.PlayerIndex, EndScreenGemResult.Failed(
                        401,
                        "Steam authentication was unavailable, so this player was not included in the match upload."));
                continue;
            }

            payloadPlayers[steamId] = BuildPlayerPayload(player);
            if (seenSteamIds.Add(steamId))
                recipients.Add(new GemRecipient(steamId, player.PlayerIndex));
        }

        MatchPayload payload = new(
            DateTime.SpecifyKind(startUtc, DateTimeKind.Utc),
            DateTime.SpecifyKind(endUtc, DateTimeKind.Utc),
            GameMode,
            payloadPlayers,
            new Dictionary<string, string> { [MatchTokenMetric] = matchToken },
            BuildTeams(players, winningTeam, boss),
            boss);

        // Permanent local backup before validation/auth — a match is never lost to an API failure.
        try
        {
            string backupPath = MatchBackupStore.Save(payload, matchToken);
            Log.Info($"[Arenas match] Backed up to {backupPath}.");
        }
        catch (Exception exception)
        {
            Log.Error($"[Arenas match] Backup failed (posting continues): {exception}");
        }

        LogSummary(payload, boss, winningTeam);

        if (!isOfficial)
        {
            Log.Info("[Arenas match] Backup saved; not an official server, so not posting to Tavernkeep.");
            ReportToAll(presentationKey, players, EndScreenGemResult.NotPosted(
                "This server is not official, so the round was not uploaded."));
            return;
        }

        if (!IsValidPayload(payload))
        {
            ReportToRecipients(presentationKey, recipients, EndScreenGemResult.Failed(
                400, "The match payload was invalid, so Tavernkeep did not add the gems."));
            return;
        }

        // Intentionally no automatic retry: Tavernkeep has no idempotency key, so an ambiguous
        // retry could duplicate the match and its gem rewards.
        _ = PostAsync(payload, presentationKey, recipients);
    }

    private static MatchPlayerPayload BuildPlayerPayload(ReportPlayer player)
    {
        uint bossDamage = Clamp(player.BossDamage);

        // Tavernkeep's match_player_stat and match_player_item_stat both CHECK (value > 0), and a
        // rejected insert rolls back the whole match — so zero-valued entries must never be sent.
        Dictionary<string, uint> stats = [];
        AddStat(stats, DamageDealtStat, Clamp(player.Damage));
        AddStat(stats, DamageTakenStat, Clamp(player.DamageTaken));
        AddStat(stats, BossDamageDealtStat, bossDamage);

        Dictionary<string, IDictionary<int, uint>> itemStats = [];
        Dictionary<int, uint> bossDamageByItem = player.BossDamageByItem?
            .Where(entry => entry.Value > 0)
            .ToDictionary(entry => entry.Key, entry => entry.Value) ?? [];
        if (bossDamageByItem.Count > 0)
            itemStats[BossDamageDealtStat] = bossDamageByItem;

        return new MatchPlayerPayload(
            player.Name ?? "",
            (uint)player.Team,
            player.Reward,
            player.Kills,
            player.Deaths,
            player.Winner,
            stats,
            itemStats,
            // Gem captures are ctg-only, and Tavernkeep rejects the whole match when a payload
            // carries a field owned by another game mode — an empty array counts as provided.
            null,
            bossDamage);
    }

    private static List<MatchTeamPayload?> BuildTeams(
        IReadOnlyList<ReportPlayer> players,
        Team winningTeam,
        short boss)
    {
        List<Team> teams = players.Select(player => player.Team)
            .Where(team => team != Team.None)
            .Distinct()
            .ToList();
        if (winningTeam != Team.None && !teams.Contains(winningTeam))
            teams.Add(winningTeam);

        int lastTeamId = teams.Select(team => (int)team).DefaultIfEmpty(0).Max();
        // Index 0 must exist and stay null — Tavernkeep reads it as "no team".
        List<MatchTeamPayload?> result = Enumerable.Repeat<MatchTeamPayload?>(null, lastTeamId + 1).ToList();

        foreach (Team team in teams)
        {
            long teamBossDamage = 0;
            foreach (ReportPlayer player in players)
                if (player.Team == team)
                    teamBossDamage += Math.Clamp(player.BossDamage, 0L, uint.MaxValue);

            bool won = team == winningTeam;
            // The round's boss counts as killed only for the team that brought it down.
            List<short> bosses = won && boss > 0 ? [boss] : [];
            // Points are pvpa-only and gem captures are ctg-only; Tavernkeep rejects the match if
            // either one is present. The winning team is already carried by MatchPlayerPayload.Winner.
            result[(int)team] = new MatchTeamPayload(Bosses: bosses, BossDamage: Clamp(teamBossDamage));
        }

        return result;
    }

    private static async Task PostAsync(
        MatchPayload payload,
        string presentationKey,
        IReadOnlyList<GemRecipient> recipients)
    {
        try
        {
            ApiResult<CompletedMatchPayload> result = await MatchApi
                .PostOfficialMatchAsync(payload)
                .ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                Log.Error($"[Arenas match] Post failed: {(int)result.Status} {result.ErrorMessage}. " +
                          "Not retried (posting is not idempotent).");
                ReportToRecipients(presentationKey, recipients, EndScreenGemResult.Failed(
                    (int)result.Status,
                    CleanReason(result.ErrorMessage, "Tavernkeep rejected the match upload.")));
                return;
            }

            Log.Info($"[Arenas match] Posted. Id={result.Data?.Id}, players={payload.Players.Count}.");

            // Tavernkeep increments gems in the same transaction as the match, so a successful
            // response makes the following profile reads authoritative totals.
            await PublishConfirmedGemTotalsAsync(presentationKey, recipients).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Error($"[Arenas match] Post threw: {exception}");
            ReportToRecipients(presentationKey, recipients, EndScreenGemResult.Failed(
                0, $"Unexpected {exception.GetType().Name}: {exception.Message}"));
        }
    }

    private static Task PublishConfirmedGemTotalsAsync(
        string presentationKey,
        IReadOnlyList<GemRecipient> recipients) =>
        Task.WhenAll(recipients.Select(recipient => PublishConfirmedGemTotalAsync(presentationKey, recipient)));

    private static async Task PublishConfirmedGemTotalAsync(string presentationKey, GemRecipient recipient)
    {
        try
        {
            ApiResult<long> result = await GemBalanceApi
                .GetTotalGemsAsync(recipient.SteamId)
                .ConfigureAwait(false);

            QueueGemResult(presentationKey, recipient.PlayerIndex, result.IsSuccess
                ? EndScreenGemResult.Confirmed(result.Data, (int)result.Status)
                : EndScreenGemResult.TotalUnavailable(
                    (int)result.Status,
                    CleanReason(result.ErrorMessage, "Tavernkeep did not return the confirmed gem balance.")));
        }
        catch (Exception exception)
        {
            QueueGemResult(presentationKey, recipient.PlayerIndex, EndScreenGemResult.TotalUnavailable(
                0,
                CleanReason(exception.Message, $"Unexpected {exception.GetType().Name} while loading the gem balance.")));
        }
    }

    private static bool TryGetSteamId(SteamAuthentication auth, ReportPlayer player, out ulong steamId)
    {
        steamId = 0;
        if (player.PlayerIndex is < 0 or >= 255)
            return false;

        ulong? identity = auth?.GetAuthenticatedIdentity((byte)player.PlayerIndex);
        if (identity is not ulong id || id == 0 || id > long.MaxValue)
            return false;

        steamId = id;
        return true;
    }

    private static bool IsValidPayload(MatchPayload payload)
    {
        if (payload.End < payload.Start)
        {
            Log.Error("[Arenas match] Refusing to post: end time precedes start time.");
            return false;
        }

        if (payload.Players.Count == 0)
        {
            Log.Warn("[Arenas match] Refusing to post: no authenticated participants.");
            return false;
        }

        if (payload.Teams.Count == 0 || payload.Teams[0] != null)
        {
            Log.Error("[Arenas match] Refusing to post: team 0 must exist and be null.");
            return false;
        }

        foreach ((ulong steamId, MatchPlayerPayload player) in payload.Players)
        {
            if (steamId == 0 || player.Team == 0 || player.Team >= payload.Teams.Count ||
                payload.Teams[(int)player.Team] == null || player.Kills < 0 || player.Deaths < 0)
            {
                Log.Error($"[Arenas match] Refusing to post malformed player. SteamId={steamId}, " +
                          $"Team={player.Team}, Kills={player.Kills}, Deaths={player.Deaths}");
                return false;
            }

            if (player.GemCaptures != null)
            {
                Log.Error($"[Arenas match] Refusing to post malformed player: gem captures are ctg-only. " +
                          $"SteamId={steamId}");
                return false;
            }
        }

        foreach (MatchTeamPayload? team in payload.Teams)
        {
            if (team is MatchTeamPayload teamPayload &&
                (teamPayload.Points != null || teamPayload.GemCaptures != null))
            {
                Log.Error("[Arenas match] Refusing to post malformed team: points are pvpa-only and gem " +
                          "captures are ctg-only.");
                return false;
            }
        }

        return true;
    }

    private static void LogSummary(MatchPayload payload, short boss, Team winningTeam)
    {
        int durationSeconds = Math.Max(0, (int)(payload.End - payload.Start).TotalSeconds);
        Log.Info($"[Arenas match] Round ended. Boss={boss}, Winner={winningTeam}, " +
                 $"Players={payload.Players.Count}, Teams={payload.Teams.Count}, " +
                 $"Duration={durationSeconds / 60}:{durationSeconds % 60:00}");

        for (int i = 0; i < payload.Teams.Count; i++)
            if (payload.Teams[i] is MatchTeamPayload team)
                Log.Info($"[Arenas match] Team {i}: {team.Bosses.Count} bosses, " +
                         $"{team.BossDamage} boss damage.");
    }

    private static void ReportToAll(
        string presentationKey,
        IReadOnlyList<ReportPlayer> players,
        EndScreenGemResult result)
    {
        foreach (ReportPlayer player in players)
            QueueGemResult(presentationKey, player.PlayerIndex, result);
    }

    private static void ReportToRecipients(
        string presentationKey,
        IEnumerable<GemRecipient> recipients,
        EndScreenGemResult result)
    {
        foreach (GemRecipient recipient in recipients)
            QueueGemResult(presentationKey, recipient.PlayerIndex, result);
    }

    private static void QueueGemResult(string presentationKey, int playerIndex, EndScreenGemResult result) =>
        Main.QueueMainThreadAction(() =>
            EndScreenService.ReportGemResult(presentationKey, playerIndex, result));

    private static string CleanReason(string reason, string fallback)
    {
        string result = string.IsNullOrWhiteSpace(reason) ? fallback : reason;
        result = result.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return result.Length <= 300 ? result : result[..300] + "...";
    }

    private static void AddStat(Dictionary<string, uint> stats, string key, uint value)
    {
        if (value > 0)
            stats[key] = value;
    }

    private static uint Clamp(long value) => (uint)Math.Clamp(value, 0L, uint.MaxValue);

    private readonly record struct GemRecipient(ulong SteamId, int PlayerIndex);
}
