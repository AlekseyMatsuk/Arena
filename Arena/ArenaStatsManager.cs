using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Arena1v1
{
    public static class ArenaStatsManager
    {
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, ArenaPlayerStatsRecord> Records =
            new Dictionary<string, ArenaPlayerStatsRecord>(StringComparer.OrdinalIgnoreCase);

        private static bool _isLoaded;

        public static string StatsDirectoryPath
        {
            get
            {
                string baseDir = Path.Combine(Environment.CurrentDirectory, "LabApi");
                if (!Directory.Exists(baseDir))
                {
                    try { Directory.CreateDirectory(baseDir); } catch { baseDir = Environment.CurrentDirectory; }
                }
                return baseDir;
            }
        }

        public static string JsonFilePath => Path.Combine(StatsDirectoryPath, "Arena1v1Stats.json");
        public static string DbFilePath => Path.Combine(StatsDirectoryPath, "Arena1v1Stats.db");

        public static void EnsureLoaded()
        {
            lock (SyncRoot)
            {
                if (_isLoaded)
                    return;

                _isLoaded = true;
                LoadFromDiskInternal();
            }
        }
        public static int RecordMatch(
            string winnerUserId,
            string winnerName,
            string loserUserId,
            string loserName,
            string loadoutSetName,
            int xpEarned
        )
        {
            EnsureLoaded();

            lock (SyncRoot)
            {
                string nowIso = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                int updatedWinnerWins = 0;

                if (!string.IsNullOrEmpty(winnerUserId))
                {
                    ArenaPlayerStatsRecord winnerRec = GetOrCreateInternal(winnerUserId, winnerName);
                    winnerRec.Nickname = string.IsNullOrWhiteSpace(winnerName) ? winnerRec.Nickname : winnerName;
                    winnerRec.Wins += 1;
                    winnerRec.CurrentStreak += 1;
                    if (winnerRec.CurrentStreak > winnerRec.BestStreak)
                        winnerRec.BestStreak = winnerRec.CurrentStreak;
                    if (xpEarned > 0)
                        winnerRec.TotalXpEarned += xpEarned;
                    if (!string.IsNullOrWhiteSpace(loadoutSetName))
                        winnerRec.LastLoadoutSet = loadoutSetName;
                    winnerRec.LastPlayedUtc = nowIso;
                    updatedWinnerWins = winnerRec.Wins;
                }

                if (!string.IsNullOrEmpty(loserUserId) && !string.Equals(loserUserId, winnerUserId, StringComparison.OrdinalIgnoreCase))
                {
                    ArenaPlayerStatsRecord loserRec = GetOrCreateInternal(loserUserId, loserName);
                    loserRec.Nickname = string.IsNullOrWhiteSpace(loserName) ? loserRec.Nickname : loserName;
                    loserRec.Losses += 1;
                    loserRec.CurrentStreak = 0;
                    loserRec.LastPlayedUtc = nowIso;
                }

                SaveToDiskInternal();
                return updatedWinnerWins;
            }
        }

        public static ArenaPlayerStatsRecord GetPlayerStats(string userId, string fallbackNickname = "Player")
        {
            EnsureLoaded();
            lock (SyncRoot)
            {
                if (!string.IsNullOrEmpty(userId) && Records.TryGetValue(userId, out ArenaPlayerStatsRecord found))
                    return found;

                return new ArenaPlayerStatsRecord
                {
                    UserId = userId ?? string.Empty,
                    Nickname = fallbackNickname
                };
            }
        }

        public static List<ArenaPlayerStatsRecord> GetTopPlayers(int limit = 10)
        {
            EnsureLoaded();
            lock (SyncRoot)
            {
                return Records.Values
                    .OrderByDescending(r => r.Wins)
                    .ThenByDescending(r => r.WinRatePercent)
                    .ThenByDescending(r => r.BestStreak)
                    .Take(limit)
                    .ToList();
            }
        }

        public static int GetPlayerRank(string userId)
        {
            EnsureLoaded();
            lock (SyncRoot)
            {
                if (string.IsNullOrEmpty(userId) || !Records.ContainsKey(userId))
                    return 0;

                List<ArenaPlayerStatsRecord> ordered = Records.Values
                    .OrderByDescending(r => r.Wins)
                    .ThenByDescending(r => r.WinRatePercent)
                    .ThenByDescending(r => r.BestStreak)
                    .ToList();

                for (int i = 0; i < ordered.Count; i++)
                {
                    if (string.Equals(ordered[i].UserId, userId, StringComparison.OrdinalIgnoreCase))
                        return i + 1;
                }
                return 0;
            }
        }

        public static bool ResetStats(string userIdOrAll)
        {
            EnsureLoaded();
            lock (SyncRoot)
            {
                if (string.Equals(userIdOrAll, "all", StringComparison.OrdinalIgnoreCase))
                {
                    Records.Clear();
                    SaveToDiskInternal();
                    return true;
                }

                if (Records.Remove(userIdOrAll))
                {
                    SaveToDiskInternal();
                    return true;
                }

                return false;
            }
        }

        private static ArenaPlayerStatsRecord GetOrCreateInternal(string userId, string nickname)
        {
            if (!Records.TryGetValue(userId, out ArenaPlayerStatsRecord rec))
            {
                rec = new ArenaPlayerStatsRecord
                {
                    UserId = userId,
                    Nickname = string.IsNullOrWhiteSpace(nickname) ? "Player" : nickname
                };
                Records[userId] = rec;
            }
            return rec;
        }

        private static void LoadFromDiskInternal()
        {
            try
            {
                if (!File.Exists(DbFilePath))
                    return;

                string[] lines = File.ReadAllLines(DbFilePath, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                        continue;

                    string[] parts = line.Split('\t');
                    if (parts.Length < 8)
                        continue;

                    string uid = parts[0].Trim();
                    if (string.IsNullOrEmpty(uid))
                        continue;

                    int.TryParse(parts[2], out int wins);
                    int.TryParse(parts[3], out int losses);
                    int.TryParse(parts[4], out int curStreak);
                    int.TryParse(parts[5], out int bestStreak);
                    int.TryParse(parts[6], out int totalXp);

                    Records[uid] = new ArenaPlayerStatsRecord
                    {
                        UserId = uid,
                        Nickname = parts[1],
                        Wins = wins,
                        Losses = losses,
                        CurrentStreak = curStreak,
                        BestStreak = bestStreak,
                        TotalXpEarned = totalXp,
                        LastLoadoutSet = parts[7],
                        LastPlayedUtc = parts.Length >= 9 ? parts[8] : string.Empty
                    };
                }
            }
            catch (Exception ex)
            {
                LabApi.Features.Console.Logger.Error($"[ArenaStats] Error loading stats file: {ex.Message}");
            }
        }

        private static void SaveToDiskInternal()
        {
            try
            {
                List<ArenaPlayerStatsRecord> sorted = Records.Values
                    .OrderByDescending(r => r.Wins)
                    .ThenByDescending(r => r.WinRatePercent)
                    .ToList();

                StringBuilder dbSb = new StringBuilder();
                dbSb.AppendLine("# UserId\tNickname\tWins\tLosses\tCurrentStreak\tBestStreak\tTotalXpEarned\tLastLoadoutSet\tLastPlayedUtc");
                for (int i = 0; i < sorted.Count; i++)
                {
                    ArenaPlayerStatsRecord r = sorted[i];
                    string safeNick = (r.Nickname ?? "Player").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
                    string safeSet = (r.LastLoadoutSet ?? "None").Replace('\t', ' ');
                    dbSb.AppendLine($"{r.UserId}\t{safeNick}\t{r.Wins}\t{r.Losses}\t{r.CurrentStreak}\t{r.BestStreak}\t{r.TotalXpEarned}\t{safeSet}\t{r.LastPlayedUtc}");
                }
                File.WriteAllText(DbFilePath, dbSb.ToString(), Encoding.UTF8);

                StringBuilder jsonSb = new StringBuilder();
                jsonSb.AppendLine("{");
                jsonSb.AppendLine($"  \"updatedAtUtc\": \"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}\",");
                jsonSb.AppendLine($"  \"totalPlayers\": {sorted.Count},");
                jsonSb.AppendLine("  \"leaderboard\": [");
                for (int i = 0; i < sorted.Count; i++)
                {
                    ArenaPlayerStatsRecord r = sorted[i];
                    string comma = (i < sorted.Count - 1) ? "," : string.Empty;
                    jsonSb.AppendLine("    {");
                    jsonSb.AppendLine($"      \"rank\": {i + 1},");
                    jsonSb.AppendLine($"      \"userId\": \"{EscapeJson(r.UserId)}\",");
                    jsonSb.AppendLine($"      \"nickname\": \"{EscapeJson(r.Nickname)}\",");
                    jsonSb.AppendLine($"      \"wins\": {r.Wins},");
                    jsonSb.AppendLine($"      \"losses\": {r.Losses},");
                    jsonSb.AppendLine($"      \"winRatePercent\": {r.WinRatePercent.ToString(CultureInfo.InvariantCulture)},");
                    jsonSb.AppendLine($"      \"kdRatio\": {r.KdRatio.ToString(CultureInfo.InvariantCulture)},");
                    jsonSb.AppendLine($"      \"currentStreak\": {r.CurrentStreak},");
                    jsonSb.AppendLine($"      \"bestStreak\": {r.BestStreak},");
                    jsonSb.AppendLine($"      \"totalXpEarned\": {r.TotalXpEarned},");
                    jsonSb.AppendLine($"      \"lastLoadoutSet\": \"{EscapeJson(r.LastLoadoutSet)}\",");
                    jsonSb.AppendLine($"      \"lastPlayedUtc\": \"{EscapeJson(r.LastPlayedUtc)}\"");
                    jsonSb.AppendLine($"    }}{comma}");
                }
                jsonSb.AppendLine("  ]");
                jsonSb.AppendLine("}");
                File.WriteAllText(JsonFilePath, jsonSb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                LabApi.Features.Console.Logger.Error($"[ArenaStats] Error saving stats file: {ex.Message}");
            }
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
        }
    }
}
