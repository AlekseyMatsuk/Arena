using System.Collections.Generic;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Features.Wrappers;
using MEC;
using PlayerRoles;
using PlayerStatsSystem;
using UnityEngine;

namespace Arena1v1
{
    public sealed partial class Arena1v1Plugin
    {
        private void EnableArenaFriendlyFire()
        {
            if (!Config.IsolateFriendlyFireToArena)
                return;

            if (!_friendlyFireOverrideActive)
            {
                _wasServerFriendlyFireEnabled = Server.FriendlyFire;
                _friendlyFireOverrideActive = true;
            }
            Server.FriendlyFire = true;
        }

        private void RestoreServerFriendlyFire()
        {
            if (_friendlyFireOverrideActive)
            {
                Server.FriendlyFire = _wasServerFriendlyFireEnabled;
                _friendlyFireOverrideActive = false;
            }
        }

        private void OnPlayerHurting(PlayerHurtingEventArgs ev)
        {
            if (ev.Player == null)
                return;

            if (_activeDuel != null)
            {
                int victimId = ev.Player.PlayerId;
                bool victimInDuel = victimId == _activeDuel.Player1Id || victimId == _activeDuel.Player2Id;

                if (victimInDuel && (ev.Attacker == null || ev.Attacker == ev.Player) &&
                    (Time.realtimeSinceStartup - _activeDuel.RoundStartTime) < 1.5f)
                {
                    ev.IsAllowed = false;
                    return;
                }

                if (ev.Attacker != null && ev.Player != ev.Attacker)
                {
                    int attackerId = ev.Attacker.PlayerId;
                    bool attackerInDuel = attackerId == _activeDuel.Player1Id || attackerId == _activeDuel.Player2Id;
                    bool attackerInPool = _arenaParticipants.Contains(attackerId);

                    if (victimInDuel && (attackerInDuel || attackerInPool) && victimId != attackerId)
                    {
                        ev.IsAllowed = true;
                        StandardDamageHandler stdDamage = ev.DamageHandler as StandardDamageHandler;
                        if (stdDamage != null && stdDamage.Damage > 0f && stdDamage.Damage < 15f)
                        {
                            stdDamage.Damage *= Config.LowDamageCompensationMultiplier;
                        }
                        return;
                    }
                }
            }

            if (ev.Attacker == null || ev.Player == ev.Attacker)
                return;

            if (_friendlyFireOverrideActive && !_wasServerFriendlyFireEnabled)
            {
                if (ev.Attacker.Team == ev.Player.Team ||
                    (ev.Attacker.Faction == ev.Player.Faction && ev.Attacker.Faction != Faction.Unclassified))
                {
                    ev.IsAllowed = false;
                }
            }
        }

        private void OnPlayerChangedRole(PlayerChangedRoleEventArgs ev)
        {
            Player changedPlayer = ev.Player;
            if (changedPlayer == null || changedPlayer.IsDestroyed)
                return;

            int pid = changedPlayer.PlayerId;

            Timing.CallDelayed(0.05f, () =>
            {
                if (changedPlayer == null || changedPlayer.IsDestroyed)
                    return;

                if (_activeDuel != null && !_activeDuel.IsRespawningBetweenRounds &&
                    (pid == _activeDuel.Player1Id || pid == _activeDuel.Player2Id))
                {
                    if (IsSpectatorRole(changedPlayer.Role))
                    {
                        HandleArenaFighterEliminated(changedPlayer, null);
                    }
                    return;
                }

                if (_activeDuel != null && (pid == _activeDuel.Player1Id || pid == _activeDuel.Player2Id))
                    return;

                if (!Config.RemoveFromPoolOnMainRoundSpawn)
                    return;

                if (_forceAddedParticipants.Contains(pid) || IsBotOrDummy(changedPlayer))
                    return;

                if (IsSpectatorRole(changedPlayer.Role) ||
                    changedPlayer.Role == Config.DuelRole ||
                    changedPlayer.Role == RoleTypeId.Tutorial)
                {
                    return;
                }

                if (_arenaParticipants.Remove(pid))
                {
                    changedPlayer.SendHint("<color=#FACC15>[1v1 Arena]</color> You spawned in the main round and were removed from the arena pool.", 4f);
                }
            });
        }

        private void OnPlayerDying(PlayerDyingEventArgs ev)
        {
            if (_activeDuel == null || ev.Player == null)
                return;

            int victimId = ev.Player.PlayerId;
            if (victimId != _activeDuel.Player1Id && victimId != _activeDuel.Player2Id)
                return;

            if ((ev.Attacker == null || ev.Attacker == ev.Player) &&
                (Time.realtimeSinceStartup - _activeDuel.RoundStartTime) < 1.2f)
            {
                ev.IsAllowed = false;
                ev.Player.Health = ev.Player.MaxHealth;
                ResolveArenaSpawnPoints(out Vector3 p1Pos, out float p1Yaw, out Vector3 p2Pos, out float p2Yaw);
                ev.Player.Position = victimId == _activeDuel.Player1Id ? p1Pos : p2Pos;
                return;
            }

            ev.IsAllowed = false;
            HandleArenaFighterEliminated(ev.Player, ev.Attacker);
        }

        private void HandleArenaFighterEliminated(Player victim, Player attacker)
        {
            if (_activeDuel == null || victim == null || _activeDuel.IsRespawningBetweenRounds)
                return;

            int victimId = victim.PlayerId;
            if (victimId != _activeDuel.Player1Id && victimId != _activeDuel.Player2Id)
                return;

            _activeDuel.IsRespawningBetweenRounds = true;
            victim.ClearInventory();
            victim.Health = victim.MaxHealth;

            Config cfg = Config;

            if (_activeDuel.IsSoloSession && _arenaParticipants.Count < 2)
            {
                _activeDuel.CurrentRound++;
                _activeDuel.RematchCoroutine = Timing.RunCoroutine(RematchRespawnCoroutine(_activeDuel));
                return;
            }

            bool victimIsP1 = victimId == _activeDuel.Player1Id;
            Player p1 = GetPlayerById(_activeDuel.Player1Id);
            Player p2 = GetPlayerById(_activeDuel.Player2Id);
            Player winner = victimIsP1 ? p2 : p1;
            Player loser = victimIsP1 ? p1 : p2;

            int winnerId = winner != null ? winner.PlayerId : -1;
            int loserId = loser != null ? loser.PlayerId : victimId;

            _activeDuel.LastWinnerId = winnerId;
            _activeDuel.LastLoserId = loserId;

            if (loserId > 0 && _arenaParticipants.Remove(loserId))
            {
                _arenaParticipants.Add(loserId);
            }

            if (_arenaParticipants.Count >= 3 && loser != null && !loser.IsDestroyed)
            {
                MoveParticipantToBench(loser);
            }

            string winnerName = winner != null ? winner.Nickname : "Player";
            string loadoutName = _activeDuel.CurrentLoadout != null ? _activeDuel.CurrentLoadout.Name : "Standard Kit";

            int winnerWins = TryRecordMatchInArenaStats(winner, loser, loadoutName, cfg.WinXpReward);

            string winMsg =
                $"<color=#FACC15><b>[1v1 Arena]</b></color> Round #{_activeDuel.CurrentRound} Winner: " +
                $"<color=#34D399><b>{winnerName} (+{cfg.WinXpReward} XP | Wins: {winnerWins})</b></color>!\n" +
                $"Next match and kit in {cfg.RematchDelaySeconds:0.0}s...";

            if (loser != null && !loser.IsDestroyed)
            {
                if (cfg.LossXpReward > 0)
                {
                    TryAwardXpSystemReward(loser, cfg.LossXpReward, cfg.LossXpReason);
                }
                loser.SendHint(winMsg, cfg.RematchDelaySeconds);
            }

            bool xpSent = false;
            if (winner != null && !winner.IsDestroyed && cfg.WinXpReward > 0)
            {
                string reasonWithWins = $"{cfg.WinXpReason} (Wins: {winnerWins})";
                xpSent = TryAwardXpSystemReward(winner, cfg.WinXpReward, reasonWithWins);
            }

            if (!xpSent && winner != null && !winner.IsDestroyed)
            {
                winner.SendHint(winMsg, cfg.RematchDelaySeconds);
            }

            _activeDuel.CurrentRound++;
            _activeDuel.RematchCoroutine = Timing.RunCoroutine(RematchRespawnCoroutine(_activeDuel));
        }

        private IEnumerator<float> RematchRespawnCoroutine(ActiveDuelSession session)
        {
            Config cfg = Config;
            yield return Timing.WaitForSeconds(cfg.RematchDelaySeconds);

            if (_activeDuel != session)
                yield break;

            CleanupDroppedArenaPickups();
            session.IsRespawningBetweenRounds = false;

            if (session.IsSoloSession && _arenaParticipants.Count < 2)
            {
                Player solo = GetPlayerById(session.Player1Id);
                if (solo == null || solo.IsDestroyed)
                {
                    HandleUnexpectedDisconnect();
                    yield break;
                }
                ArenaLoadoutSet nextLoadout = PickRoundLoadoutSet();
                session.CurrentLoadout = nextLoadout;
                SpawnSingleFighterOnArena(solo, 1, session.CurrentRound, nextLoadout);
                yield break;
            }

            StartNextRandomRoundFromPool(session.CurrentRound);
        }

        private void StopDuelAndOptOutPlayer(Player quittingPlayer)
        {
            if (_activeDuel == null)
                return;

            Timing.KillCoroutines(_activeDuel.RematchCoroutine);

            int nextRound = _activeDuel.CurrentRound + 1;
            _activeDuel = null;
            CleanupDroppedArenaPickups();

            ReturnPlayerToSpectator(quittingPlayer);
            quittingPlayer.SendHint("<color=#F87171>[1v1 Arena]</color> You left the arena and returned to Spectator.", Config.HintDurationSeconds);

            StartNextRandomRoundFromPool(nextRound);
        }

        private void MoveParticipantToBench(Player player)
        {
            if (player == null || player.IsDestroyed)
                return;

            player.ClearInventory();
            if (IsBotOrDummy(player))
            {
                if (player.Role != Config.DuelRole)
                {
                    player.SetRole(Config.DuelRole, RoleChangeReason.RemoteAdmin, RoleSpawnFlags.None);
                }
                player.Position = Config.ArenaOrigin + new Vector3(45f, 1.5f, 0f);
                player.Health = player.MaxHealth;
                return;
            }

            if (player.Role != RoleTypeId.Spectator)
            {
                player.SetRole(RoleTypeId.Spectator, RoleChangeReason.RemoteAdmin, RoleSpawnFlags.None);
            }
        }

        private void ReturnPlayerToSpectator(Player player)
        {
            if (player == null || player.IsDestroyed)
                return;

            player.ClearInventory();
            if (player.Role != RoleTypeId.Spectator)
            {
                player.SetRole(RoleTypeId.Spectator, RoleChangeReason.RemoteAdmin, RoleSpawnFlags.None);
            }
        }

        private void OnPlayerLeft(PlayerLeftEventArgs ev)
        {
            if (ev.Player == null)
                return;

            int pid = ev.Player.PlayerId;
            _arenaParticipants.Remove(pid);
            _forceAddedParticipants.Remove(pid);

            if (_activeDuel != null &&
                (pid == _activeDuel.Player1Id || pid == _activeDuel.Player2Id))
            {
                HandleUnexpectedDisconnect();
            }
        }

        private void HandleUnexpectedDisconnect()
        {
            if (_activeDuel == null)
                return;

            Timing.KillCoroutines(_activeDuel.RematchCoroutine);

            int nextRound = _activeDuel.CurrentRound + 1;
            _activeDuel = null;
            CleanupDroppedArenaPickups();

            StartNextRandomRoundFromPool(nextRound);
        }
    }
}
