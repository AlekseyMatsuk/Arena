using System;
using System.Collections.Generic;
using System.Text;
using LabApi.Features.Wrappers;
using MEC;
using PlayerRoles;
using UnityEngine;

namespace Arena1v1
{
    public sealed partial class Arena1v1Plugin
    {
        private static bool IsSpectatorRole(RoleTypeId role)
        {
            return role == RoleTypeId.Spectator || role == RoleTypeId.Overwatch;
        }

        private static bool IsBotOrDummy(Player player)
        {
            if (player == null)
                return false;
            return string.IsNullOrEmpty(player.UserId) ||
                   player.UserId == "ID_Dedicated" ||
                   player.UserId.StartsWith("Dummy", StringComparison.OrdinalIgnoreCase);
        }

        public Player GetPlayerById(int playerId)
        {
            if (playerId <= 0)
                return null;

            if (Player.TryGet(playerId, out Player byId) && byId != null && !byId.IsDestroyed)
                return byId;

            foreach (Player candidate in Player.List)
            {
                if (candidate != null && !candidate.IsDestroyed && candidate.PlayerId == playerId)
                    return candidate;
            }

            return null;
        }
        public Player FindTargetPlayer(string query, Player caller)
        {
            if (string.IsNullOrWhiteSpace(query))
                return null;

            string trimmed = query.Trim();
            if (trimmed.StartsWith("#") && trimmed.Length > 1)
                trimmed = trimmed.Substring(1);

            if (string.Equals(trimmed, "me", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "self", StringComparison.OrdinalIgnoreCase))
            {
                return caller;
            }

            if (int.TryParse(trimmed, out int pid))
            {
                Player byPid = GetPlayerById(pid);
                if (byPid != null)
                    return byPid;
            }

            if (string.Equals(trimmed, "bot", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "dummy", StringComparison.OrdinalIgnoreCase))
            {
                Player fallbackBot = null;
                foreach (Player candidate in Player.List)
                {
                    if (candidate == null || candidate.IsDestroyed || candidate == caller)
                        continue;

                    if (IsBotOrDummy(candidate))
                    {
                        if (!_arenaParticipants.Contains(candidate.PlayerId))
                            return candidate;
                        if (fallbackBot == null)
                            fallbackBot = candidate;
                    }
                }
                if (fallbackBot != null)
                    return fallbackBot;
            }

            if (Player.TryGet(trimmed, out Player byUserId) && byUserId != null && !byUserId.IsDestroyed)
            {
                return byUserId;
            }

            Player firstAlreadyInPool = null;
            foreach (Player candidate in Player.List)
            {
                if (candidate == null || candidate.IsDestroyed || string.IsNullOrEmpty(candidate.Nickname))
                    continue;

                if (string.Equals(candidate.Nickname, trimmed, StringComparison.OrdinalIgnoreCase) ||
                    candidate.Nickname.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (!_arenaParticipants.Contains(candidate.PlayerId))
                        return candidate;

                    if (firstAlreadyInPool == null)
                        firstAlreadyInPool = candidate;
                }
            }

            return firstAlreadyInPool;
        }

        private void CleanupInvalidParticipants()
        {
            _arenaParticipants.RemoveAll(pid =>
            {
                Player p = GetPlayerById(pid);
                if (p == null || p.IsDestroyed)
                {
                    _forceAddedParticipants.Remove(pid);
                    return true;
                }
                return false;
            });
        }

        public bool InspectArenaPool(out string response)
        {
            CleanupInvalidParticipants();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"[RA - Arena1v1] Pool Size: {_arenaParticipants.Count}");
            if (_activeDuel != null)
            {
                Player p1 = GetPlayerById(_activeDuel.Player1Id);
                Player p2 = GetPlayerById(_activeDuel.Player2Id);
                string p1Str = p1 != null ? $"{p1.Nickname} (ID:{p1.PlayerId})" : $"ID:{_activeDuel.Player1Id}";
                string p2Str = p2 != null ? $"{p2.Nickname} (ID:{p2.PlayerId})" : (_activeDuel.IsSoloSession ? "SOLO" : $"ID:{_activeDuel.Player2Id}");
                string kitStr = _activeDuel.CurrentLoadout != null ? _activeDuel.CurrentLoadout.Name : "Standard Kit";
                sb.AppendLine($"Active Duel (Round #{_activeDuel.CurrentRound}, Kit: {kitStr}): {p1Str} vs {p2Str}");
            }
            else
            {
                sb.AppendLine("Active Duel: None");
            }

            for (int i = 0; i < _arenaParticipants.Count; i++)
            {
                int pid = _arenaParticipants[i];
                Player p = GetPlayerById(pid);
                string name = p != null ? p.Nickname : "Offline";
                string role = p != null ? p.Role.ToString() : "None";
                bool isFighting = _activeDuel != null && (pid == _activeDuel.Player1Id || pid == _activeDuel.Player2Id);
                string stateTag = isFighting ? "FIGHTING" : "WAITING IN QUEUE";
                sb.AppendLine($"  {i + 1}. [ID:{pid}] {name} ({role}) - {stateTag}");
            }

            response = sb.ToString();
            return true;
        }
        public bool ToggleArenaParticipation(Player player, out string response)
        {
            Config cfg = Config;
            int pid = player.PlayerId;

            bool isCurrentlyFighting = _activeDuel != null &&
                                       (_activeDuel.Player1Id == pid || _activeDuel.Player2Id == pid);

            if (_arenaParticipants.Contains(pid) || isCurrentlyFighting)
            {
                _arenaParticipants.Remove(pid);
                _forceAddedParticipants.Remove(pid);

                if (isCurrentlyFighting)
                {
                    StopDuelAndOptOutPlayer(player);
                    response = "[1v1 Arena] You have left the arena and returned to Spectator.";
                    return true;
                }

                player.SendHint("<color=#F87171>[1v1 Arena]</color> You have left the arena pool.", cfg.HintDurationSeconds);
                response = "[1v1 Arena] You have left the arena pool (.arena).";
                return true;
            }

            bool allowedRole = IsSpectatorRole(player.Role) || player.Role == RoleTypeId.Tutorial || player.Role == cfg.DuelRole;
            if (cfg.OnlySpectatorsCanJoin && !allowedRole)
            {
                player.SendHint(
                    "<color=#F87171><b>[1v1 Arena]</b></color> You can only join <b>.arena</b> while in the <b>Spectator</b> role!",
                    cfg.HintDurationSeconds
                );
                response = "[1v1 Arena] You can only join .arena while you are a Spectator.";
                return false;
            }

            CleanupInvalidParticipants();
            if (cfg.MaxPoolSize > 0 && _arenaParticipants.Count >= cfg.MaxPoolSize)
            {
                response = $"[1v1 Arena] The arena pool is currently full ({cfg.MaxPoolSize}/{cfg.MaxPoolSize}).";
                return false;
            }

            _arenaParticipants.Add(pid);

            if (_activeDuel != null && _activeDuel.IsSoloSession && _activeDuel.Player1Id != pid)
            {
                StartNextRandomRoundFromPool(1);
                response = "[1v1 Arena] Opponent found - teleporting to the arena!";
                return true;
            }

            if (_activeDuel == null && _arenaParticipants.Count >= 2)
            {
                StartNextRandomRoundFromPool(1);
                response = "[1v1 Arena] Match found - teleporting to the arena!";
                return true;
            }

            if (_activeDuel != null && !_activeDuel.IsSoloSession)
            {
                int totalInPool = _arenaParticipants.Count;
                player.SendHint(
                    $"<color=#00F0FF><b>[1v1 Arena]</b></color> Added to the arena rotation pool (<b>{totalInPool}</b> players)!\n" +
                    "You will automatically rotate into the next round. Type <b>.arena</b> again to leave.",
                    cfg.HintDurationSeconds
                );

                Player f1 = GetPlayerById(_activeDuel.Player1Id);
                Player f2 = GetPlayerById(_activeDuel.Player2Id);
                string joinNotice = $"<color=#34D399>[1v1 Arena]</color> <b>{player.Nickname}</b> joined the arena pool (<b>{totalInPool}</b> players).";
                if (f1 != null && !f1.IsDestroyed) f1.SendHint(joinNotice, 3.5f);
                if (f2 != null && !f2.IsDestroyed) f2.SendHint(joinNotice, 3.5f);

                response = $"[1v1 Arena] Added to the arena pool ({totalInPool} players). You will rotate in next round!";
                return true;
            }

            player.SendHint(
                "<color=#00F0FF>[1v1 Arena]</color> You are queued in the arena pool (1/2).\n" +
                "Match starts as soon as a 2nd participant joins. Type <b>.arena</b> again to cancel.",
                cfg.HintDurationSeconds
            );
            response = "[1v1 Arena] Added to queue (1/2). Type .arena again to leave.";
            return true;
        }
        private void StartNextRandomRoundFromPool(int nextRoundNumber)
        {
            CleanupInvalidParticipants();

            if (_arenaParticipants.Count < 2)
            {
                int prevP1 = _activeDuel != null ? _activeDuel.Player1Id : -1;
                int prevP2 = _activeDuel != null ? _activeDuel.Player2Id : -1;
                _activeDuel = null;
                RestoreServerFriendlyFire();

                foreach (int pid in _arenaParticipants)
                {
                    Player waitPlayer = GetPlayerById(pid);
                    if (waitPlayer != null && !waitPlayer.IsDestroyed)
                    {
                        MoveParticipantToBench(waitPlayer);
                        waitPlayer.SendHint(
                            "<color=#FACC15>[1v1 Arena]</color> Waiting for a second opponent (1/2).\n" +
                            "Type <b>.arena</b> again to leave.",
                            Config.HintDurationSeconds
                        );
                    }
                }

                if (prevP1 > 0 && !_arenaParticipants.Contains(prevP1))
                {
                    Player p1Old = GetPlayerById(prevP1);
                    if (p1Old != null && !p1Old.IsDestroyed) ReturnPlayerToSpectator(p1Old);
                }
                if (prevP2 > 0 && !_arenaParticipants.Contains(prevP2))
                {
                    Player p2Old = GetPlayerById(prevP2);
                    if (p2Old != null && !p2Old.IsDestroyed) ReturnPlayerToSpectator(p2Old);
                }
                return;
            }

            int lastP1 = _activeDuel != null ? _activeDuel.Player1Id : -1;
            int lastP2 = _activeDuel != null ? _activeDuel.Player2Id : -1;
            int lastWinner = _activeDuel != null ? _activeDuel.LastWinnerId : -1;

            List<int> waitingBench = new List<int>();
            List<int> previousFighters = new List<int>();

            for (int i = 0; i < _arenaParticipants.Count; i++)
            {
                int pid = _arenaParticipants[i];
                if (pid == lastP1 || pid == lastP2)
                    previousFighters.Add(pid);
                else
                    waitingBench.Add(pid);
            }

            int chosenP1Id;
            int chosenP2Id;

            if (Config.PrioritizeWaitingBenchPlayers && waitingBench.Count >= 2)
            {
                chosenP1Id = waitingBench[0];
                chosenP2Id = waitingBench[1];
            }
            else if (Config.PrioritizeWaitingBenchPlayers && waitingBench.Count == 1)
            {
                chosenP1Id = waitingBench[0];
                if (lastWinner > 0 && previousFighters.Contains(lastWinner))
                {
                    chosenP2Id = lastWinner;
                }
                else if (previousFighters.Count > 0)
                {
                    chosenP2Id = previousFighters[0];
                }
                else
                {
                    chosenP2Id = _arenaParticipants[0] != chosenP1Id ? _arenaParticipants[0] : _arenaParticipants[1];
                }
            }
            else
            {
                chosenP1Id = _arenaParticipants[0];
                chosenP2Id = _arenaParticipants[1];
            }

            Player p1 = GetPlayerById(chosenP1Id);
            Player p2 = GetPlayerById(chosenP2Id);

            if (p1 == null || p1.IsDestroyed || p2 == null || p2.IsDestroyed)
            {
                CleanupInvalidParticipants();
                return;
            }

            EnsureArenaSchematicSpawned();
            EnableArenaFriendlyFire();

            ArenaLoadoutSet roundLoadout = PickRoundLoadoutSet();

            _activeDuel = new ActiveDuelSession
            {
                Player1Id = p1.PlayerId,
                Player2Id = p2.PlayerId,
                Player1UserId = GetStatsKey(p1),
                Player2UserId = GetStatsKey(p2),
                CurrentRound = nextRoundNumber,
                RoundStartTime = Time.realtimeSinceStartup,
                IsRespawningBetweenRounds = false,
                CurrentLoadout = roundLoadout
            };

            for (int i = 0; i < _arenaParticipants.Count; i++)
            {
                int pid = _arenaParticipants[i];
                if (pid == chosenP1Id || pid == chosenP2Id)
                    continue;

                Player benchPlayer = GetPlayerById(pid);
                if (benchPlayer != null && !benchPlayer.IsDestroyed)
                {
                    MoveParticipantToBench(benchPlayer);
                    benchPlayer.SendHint(
                        $"<color=#00F0FF><b>[1v1 Arena - Round #{nextRoundNumber}]</b></color>\n" +
                        $"Fighting now: <color=#38BDF8>{p1.Nickname}</color> vs <color=#FB7185>{p2.Nickname}</color> (<b>{roundLoadout.Name}</b>)\n" +
                        $"<size=75%>You are next in the pool ({_arenaParticipants.Count} players) and will rotate in next round.</size>",
                        Config.HintDurationSeconds
                    );
                }
            }

            SpawnBothFightersOnArena(p1, p2, _activeDuel);
        }

        public bool StartOrJoinSoloArena(Player player, out string response)
        {
            int pid = player.PlayerId;
            _forceAddedParticipants.Add(pid);
            if (!_arenaParticipants.Contains(pid))
                _arenaParticipants.Add(pid);

            EnsureArenaSchematicSpawned();
            EnableArenaFriendlyFire();

            ArenaLoadoutSet loadout = PickRoundLoadoutSet();

            if (_activeDuel == null)
            {
                _activeDuel = new ActiveDuelSession
                {
                    Player1Id = pid,
                    Player2Id = -1,
                    Player1UserId = GetStatsKey(player),
                    Player2UserId = string.Empty,
                    CurrentRound = 1,
                    RoundStartTime = Time.realtimeSinceStartup,
                    IsRespawningBetweenRounds = false,
                    CurrentLoadout = loadout
                };

                SpawnSingleFighterOnArena(player, 1, _activeDuel.CurrentRound, loadout);
                response = $"[RA - Arena1v1] {player.Nickname} (ID:{pid}) spawned on the arena in solo mode ({loadout.Name}).";
                return true;
            }

            if (_activeDuel.IsSoloSession && _activeDuel.Player1Id == pid)
            {
                _activeDuel.CurrentRound++;
                _activeDuel.RoundStartTime = Time.realtimeSinceStartup;
                _activeDuel.CurrentLoadout = loadout;
                CleanupDroppedArenaPickups();
                SpawnSingleFighterOnArena(player, 1, _activeDuel.CurrentRound, loadout);
                response = $"[RA - Arena1v1] Solo round #{_activeDuel.CurrentRound} started ({loadout.Name}).";
                return true;
            }

            if (_activeDuel.IsSoloSession && _activeDuel.Player1Id != pid)
            {
                Player p1 = GetPlayerById(_activeDuel.Player1Id);
                if (p1 == null || p1.IsDestroyed)
                {
                    _activeDuel.Player1Id = pid;
                    _activeDuel.Player1UserId = GetStatsKey(player);
                    _activeDuel.RoundStartTime = Time.realtimeSinceStartup;
                    _activeDuel.CurrentLoadout = loadout;
                    SpawnSingleFighterOnArena(player, 1, 1, loadout);
                    response = $"[RA - Arena1v1] {player.Nickname} (ID:{pid}) spawned on the arena ({loadout.Name}).";
                    return true;
                }

                _activeDuel.Player2Id = pid;
                _activeDuel.Player2UserId = GetStatsKey(player);
                _activeDuel.CurrentRound = 1;
                _activeDuel.RoundStartTime = Time.realtimeSinceStartup;
                _activeDuel.CurrentLoadout = loadout;
                SpawnBothFightersOnArena(p1, player, _activeDuel);
                response = $"[RA - Arena1v1] Duel started: {p1.Nickname} (ID:{p1.PlayerId}) vs {player.Nickname} (ID:{pid}) ({loadout.Name})!";
                return true;
            }

            response = $"[RA - Arena1v1] {player.Nickname} (ID:{pid}) added to the arena pool (Pool size: {_arenaParticipants.Count}).";
            return true;
        }

        public bool ForceAddPlayerToArena(Player target, out string response)
        {
            int targetId = target.PlayerId;
            _forceAddedParticipants.Add(targetId);

            CleanupInvalidParticipants();

            if (!_arenaParticipants.Contains(targetId))
                _arenaParticipants.Add(targetId);

            EnsureArenaSchematicSpawned();
            EnableArenaFriendlyFire();

            ArenaLoadoutSet loadout = PickRoundLoadoutSet();

            if (_activeDuel == null)
            {
                if (_arenaParticipants.Count >= 2)
                {
                    StartNextRandomRoundFromPool(1);
                    response = $"[RA - Arena1v1] Player {target.Nickname} (ID:{targetId}) added. 2 participants ready - 1v1 duel started!";
                    return true;
                }

                _activeDuel = new ActiveDuelSession
                {
                    Player1Id = targetId,
                    Player2Id = -1,
                    Player1UserId = GetStatsKey(target),
                    Player2UserId = string.Empty,
                    CurrentRound = 1,
                    RoundStartTime = Time.realtimeSinceStartup,
                    IsRespawningBetweenRounds = false,
                    CurrentLoadout = loadout
                };

                SpawnSingleFighterOnArena(target, 1, 1, loadout);
                response = $"[RA - Arena1v1] Player {target.Nickname} (ID:{targetId}) teleported to the arena ({loadout.Name}). Add a 2nd player/bot to start 1v1.";
                return true;
            }

            if (_activeDuel.IsSoloSession && _activeDuel.Player1Id != targetId)
            {
                Player p1 = GetPlayerById(_activeDuel.Player1Id);
                if (p1 == null || p1.IsDestroyed)
                {
                    _activeDuel.Player1Id = targetId;
                    _activeDuel.Player1UserId = GetStatsKey(target);
                    _activeDuel.RoundStartTime = Time.realtimeSinceStartup;
                    _activeDuel.CurrentLoadout = loadout;
                    SpawnSingleFighterOnArena(target, 1, 1, loadout);
                    response = $"[RA - Arena1v1] Player {target.Nickname} (ID:{targetId}) teleported to the arena ({loadout.Name}).";
                    return true;
                }

                _activeDuel.Player2Id = targetId;
                _activeDuel.Player2UserId = GetStatsKey(target);
                _activeDuel.CurrentRound = 1;
                _activeDuel.RoundStartTime = Time.realtimeSinceStartup;
                _activeDuel.CurrentLoadout = loadout;
                SpawnBothFightersOnArena(p1, target, _activeDuel);
                response = $"[RA - Arena1v1] Player {target.Nickname} (ID:{targetId}) matched against {p1.Nickname} (ID:{p1.PlayerId})! 1v1 duel started.";
                return true;
            }

            if (_activeDuel.Player1Id != targetId && _activeDuel.Player2Id != targetId)
            {
                MoveParticipantToBench(target);
            }

            response = $"[RA - Arena1v1] Player {target.Nickname} (ID:{targetId}) added to the arena pool ({_arenaParticipants.Count} in pool). They will rotate in next round.";
            target.SendHint(
                $"<color=#00F0FF><b>[1v1 Arena]</b></color> You have been added to the arena pool (<b>{_arenaParticipants.Count}</b> players).\n" +
                "You will automatically spawn in the next round!",
                Config.HintDurationSeconds
            );
            return true;
        }

        public bool SimulateRoundWinForTesting(Player player, out string response)
        {
            Config cfg = Config;
            string currentSetName = _activeDuel != null && _activeDuel.CurrentLoadout != null
                ? _activeDuel.CurrentLoadout.Name
                : "Standard Kit";

            Player loser = null;
            if (_activeDuel != null && !_activeDuel.IsSoloSession)
            {
                int loserId = _activeDuel.Player1Id == player.PlayerId ? _activeDuel.Player2Id : _activeDuel.Player1Id;
                loser = GetPlayerById(loserId);
                _activeDuel.LastWinnerId = player.PlayerId;
                _activeDuel.LastLoserId = loserId;
                if (loserId > 0 && _arenaParticipants.Remove(loserId))
                {
                    _arenaParticipants.Add(loserId);
                }
            }

            int totalWins = TryRecordMatchInArenaStats(player, loser, currentSetName, cfg.WinXpReward);
            string xpReason = $"{cfg.WinXpReason} (Wins: {totalWins})";
            bool xpAwarded = TryAwardXpSystemReward(player, cfg.WinXpReward, xpReason);

            if (!xpAwarded)
            {
                player.SendHint(
                    $"<color=#FACC15><b>[1v1 Arena - Victory]</b></color> Awarded <b>+{cfg.WinXpReward} XP</b> (Total Wins: {totalWins})!",
                    cfg.HintDurationSeconds
                );
            }

            if (_activeDuel != null)
            {
                Timing.KillCoroutines(_activeDuel.RematchCoroutine);
                _activeDuel.IsRespawningBetweenRounds = false;
                _activeDuel.CurrentRound++;
                CleanupDroppedArenaPickups();
                if (_activeDuel.IsSoloSession && _arenaParticipants.Count < 2)
                {
                    ArenaLoadoutSet nextSet = PickRoundLoadoutSet();
                    _activeDuel.CurrentLoadout = nextSet;
                    SpawnSingleFighterOnArena(player, 1, _activeDuel.CurrentRound, nextSet);
                }
                else
                {
                    StartNextRandomRoundFromPool(_activeDuel.CurrentRound);
                }
            }

            response = $"[RA - Arena1v1] Round victory awarded to {player.Nickname} (+{cfg.WinXpReward} XP, Total Wins: {totalWins}).";
            return true;
        }

        public bool ForceStopAllDuelsFromAdmin(out string response)
        {
            if (_activeDuel != null)
            {
                Timing.KillCoroutines(_activeDuel.RematchCoroutine);
                Player p1 = GetPlayerById(_activeDuel.Player1Id);
                Player p2 = GetPlayerById(_activeDuel.Player2Id);
                _activeDuel = null;
                if (p1 != null && !p1.IsDestroyed) ReturnPlayerToSpectator(p1);
                if (p2 != null && !p2.IsDestroyed) ReturnPlayerToSpectator(p2);
            }

            foreach (int pid in _arenaParticipants)
            {
                Player p = GetPlayerById(pid);
                if (p != null && !p.IsDestroyed)
                    ReturnPlayerToSpectator(p);
            }

            _arenaParticipants.Clear();
            _forceAddedParticipants.Clear();
            CleanupDroppedArenaPickups();
            RestoreServerFriendlyFire();

            response = "[RA - Arena1v1] Active duel stopped, all participants moved to Spectator, and pool cleared.";
            return true;
        }
    }
}
