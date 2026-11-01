using System;
using System.Reflection;
using LabApi.Features.Wrappers;

namespace Arena1v1
{
    public sealed partial class Arena1v1Plugin
    {
        public static string GetStatsKey(Player player)
        {
            if (player == null)
                return string.Empty;
            if (!string.IsNullOrEmpty(player.UserId) && player.UserId != "ID_Dedicated")
                return player.UserId;
            return $"bot_{player.PlayerId}_{player.Nickname ?? "Dummy"}";
        }

        private int GetPlayerWins(Player player)
        {
            string key = GetStatsKey(player);
            if (string.IsNullOrEmpty(key))
                return 0;
            return _playerWins.TryGetValue(key, out int w) ? w : 0;
        }

        private int IncrementPlayerWins(Player player)
        {
            string key = GetStatsKey(player);
            if (string.IsNullOrEmpty(key))
                return 0;
            int next = GetPlayerWins(player) + 1;
            _playerWins[key] = next;
            return next;
        }
        private int TryRecordMatchInArenaStats(Player winner, Player loser, string loadoutSetName, int xpAwarded)
        {
            string winnerId = GetStatsKey(winner);
            string winnerName = winner != null ? winner.Nickname : "Player";
            string loserId = GetStatsKey(loser);
            string loserName = loser != null ? loser.Nickname : "Opponent";

            int sessionWins = IncrementPlayerWins(winner);
            if (!Config.EnableStatsRecording)
                return sessionWins;

            try
            {
                if (!_arenaStatsSearched)
                {
                    _arenaStatsSearched = true;
                    foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        Type statsType = asm.GetType("Arena1v1.ArenaStatsManager");
                        if (statsType == null)
                            continue;

                        _arenaStatsRecordMethod = statsType.GetMethod(
                            "RecordMatch",
                            BindingFlags.Public | BindingFlags.Static,
                            null,
                            new Type[]
                            {
                                typeof(string),
                                typeof(string),
                                typeof(string),
                                typeof(string),
                                typeof(string),
                                typeof(int)
                            },
                            null
                        );

                        if (_arenaStatsRecordMethod != null)
                            break;
                    }
                }

                if (_arenaStatsRecordMethod != null)
                {
                    object result = _arenaStatsRecordMethod.Invoke(
                        null,
                        new object[] { winnerId, winnerName, loserId, loserName, loadoutSetName ?? "Standard Kit", xpAwarded }
                    );
                    if (result is int totalSavedWins && totalSavedWins > 0)
                    {
                        _playerWins[winnerId] = totalSavedWins;
                        return totalSavedWins;
                    }
                }
            }
            catch (Exception ex)
            {
                LabApi.Features.Console.Logger.Error($"[Arena1v1] Failed to record match in ArenaStats: {ex.Message}");
            }

            return sessionWins;
        }
        private bool TryAwardXpSystemReward(Player target, int xpAmount, string reason)
        {
            if (!Config.EnableXpRewards || target == null || target.IsDestroyed || IsBotOrDummy(target) || xpAmount <= 0)
                return false;

            try
            {
                if (!_xpSystemSearched)
                {
                    _xpSystemSearched = true;
                    foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        Type xpManagerType = asm.GetType("XpSystem.XpManager");
                        if (xpManagerType == null)
                            continue;

                        _xpSystemAddXpMethod = xpManagerType.GetMethod(
                            "AddXp",
                            BindingFlags.Public | BindingFlags.Static,
                            null,
                            new Type[] { typeof(Player), typeof(int), typeof(string) },
                            null
                        );

                        if (_xpSystemAddXpMethod != null)
                            break;
                    }
                }

                if (_xpSystemAddXpMethod != null)
                {
                    _xpSystemAddXpMethod.Invoke(null, new object[] { target, xpAmount, reason });
                    return true;
                }
            }
            catch (Exception ex)
            {
                LabApi.Features.Console.Logger.Error($"[Arena1v1] Failed to award XP via XpSystem: {ex.Message}");
            }

            return false;
        }
    }
}
