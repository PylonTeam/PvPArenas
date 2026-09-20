using System;
using System.IO;
using System.Text;
using System.Text.Json;
using MatchPayload = Pylon.Common.MainMenu.API.MatchHistory.MatchApi.MatchPayload;

namespace PvPArenas.Common.Game.Score;

/// <summary>
/// Permanently writes each completed Arenas match payload to disk before it is validated or posted,
/// so matches are never lost to an API failure. Existing backups are immutable and never deleted.
/// </summary>
internal static class MatchBackupStore
{
    private const string BackupDirectoryName = "PvPArenasMatchesBackupDoNotDelete";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string Save(MatchPayload payload, string matchToken)
    {
        ArgumentNullException.ThrowIfNull(payload);

        string backupDirectory = Path.Combine(Main.SavePath, BackupDirectoryName);
        Directory.CreateDirectory(backupDirectory);

        string safeMatchToken = MakeFileNameSafe(matchToken);
        string endTimestamp = payload.End
            .ToUniversalTime()
            .ToString("yyyyMMdd'T'HHmmss.fffffff'Z'");
        string backupPath = Path.Combine(
            backupDirectory,
            $"Arenas {endTimestamp} {safeMatchToken}.json");

        // A completed match can be reported more than once by upstream game events. Keep the
        // original immutable backup instead of overwriting it with a later serialization.
        if (File.Exists(backupPath))
            return backupPath;

        string json = JsonSerializer.Serialize(payload, JsonOptions);
        string temporaryPath = backupPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            File.Move(temporaryPath, backupPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }

        return backupPath;
    }

    private static string MakeFileNameSafe(string matchToken)
    {
        string result = string.IsNullOrWhiteSpace(matchToken) ? "no-match-token" : matchToken.Trim();
        foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
            result = result.Replace(invalidCharacter, '_');

        return result;
    }
}
