using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using LabApi.Features.Wrappers;
using MEC;
using PlayerRoles;
using UnityEngine;

namespace Arena1v1
{
    public sealed partial class Arena1v1Plugin
    {
        public void SetCustomSpawnPoint(int playerNumber, Vector3 worldPos, float yaw)
        {
            if (playerNumber == 1)
            {
                _customP1WorldPos = worldPos;
                _customP1Yaw = yaw;
            }
            else
            {
                _customP2WorldPos = worldPos;
                _customP2Yaw = yaw;
            }
        }

        private List<ArenaLoadoutSet> GetActiveKitPool()
        {
            Config cfg = Config;
            List<ArenaLoadoutSet> list = new List<ArenaLoadoutSet>();
            if (cfg.LoadoutKits != null)
            {
                for (int i = 0; i < cfg.LoadoutKits.Count; i++)
                {
                    ArenaLoadoutSet kit = cfg.LoadoutKits[i];
                    if (kit != null && kit.Enabled)
                        list.Add(kit);
                }
            }

            if (list.Count == 0)
            {
                list = Config.CreateDefaultLoadoutKits();
            }

            return list;
        }
        private ArenaLoadoutSet PickRoundLoadoutSet()
        {
            Config cfg = Config;
            List<ArenaLoadoutSet> allKits = cfg.LoadoutKits != null && cfg.LoadoutKits.Count > 0
                ? cfg.LoadoutKits
                : Config.CreateDefaultLoadoutKits();

            if (_forcedLoadoutIndex >= 0 && _forcedLoadoutIndex < allKits.Count)
            {
                return allKits[_forcedLoadoutIndex];
            }

            if (!cfg.UseRandomLoadoutSets)
            {
                List<ItemType> items = new List<ItemType>();
                if (cfg.HealingItem != ItemType.None)
                    items.Add(cfg.HealingItem);

                return new ArenaLoadoutSet
                {
                    Id = "fallback",
                    Name = "Standard Kit",
                    Enabled = true,
                    PrimaryWeapon = cfg.PrimaryWeapon,
                    SecondaryWeapon = cfg.SecondaryWeapon,
                    Armor = cfg.ArmorItem,
                    HealingAndUtility = items,
                    PrimaryAmmoType = cfg.FallbackPrimaryAmmoType,
                    PrimaryAmmoAmount = cfg.FallbackPrimaryAmmoAmount,
                    SecondaryAmmoType = cfg.FallbackSecondaryAmmoType,
                    SecondaryAmmoAmount = cfg.FallbackSecondaryAmmoAmount
                };
            }

            List<ArenaLoadoutSet> enabledKits = GetActiveKitPool();
            int idx = _rng.Next(enabledKits.Count);
            if (cfg.PreventConsecutiveDuplicateKits && enabledKits.Count > 1 && enabledKits[idx].Id == _lastLoadoutId)
            {
                idx = (idx + 1 + _rng.Next(enabledKits.Count - 1)) % enabledKits.Count;
            }

            ArenaLoadoutSet chosen = enabledKits[idx];
            _lastLoadoutId = chosen.Id ?? chosen.Name;
            return chosen;
        }

        public bool ListConfiguredKits(out string response)
        {
            List<ArenaLoadoutSet> kits = Config.LoadoutKits != null && Config.LoadoutKits.Count > 0
                ? Config.LoadoutKits
                : Config.CreateDefaultLoadoutKits();

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[RA - Arena1v1] Configured Loadout Kits:");
            for (int i = 0; i < kits.Count; i++)
            {
                ArenaLoadoutSet k = kits[i];
                string status = k.Enabled ? "ENABLED" : "DISABLED";
                sb.AppendLine($"#{i + 1} [{k.Id}] {k.Name} ({status}) - {k.PrimaryWeapon} + {k.SecondaryWeapon} + {k.Armor}");
            }
            response = sb.ToString();
            return true;
        }

        public bool ForceSelectLoadoutSet(Player player, string query, out string response)
        {
            if (string.Equals(query, "random", StringComparison.OrdinalIgnoreCase) || query == "0")
            {
                _forcedLoadoutIndex = -1;
                response = "[RA - Arena1v1] Random kit rotation enabled for every round.";
                return true;
            }

            List<ArenaLoadoutSet> kits = Config.LoadoutKits != null && Config.LoadoutKits.Count > 0
                ? Config.LoadoutKits
                : Config.CreateDefaultLoadoutKits();

            int foundIndex = -1;
            if (int.TryParse(query, out int num) && num >= 1 && num <= kits.Count)
            {
                foundIndex = num - 1;
            }
            else
            {
                for (int i = 0; i < kits.Count; i++)
                {
                    if (string.Equals(kits[i].Id, query, StringComparison.OrdinalIgnoreCase) ||
                        kits[i].Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        foundIndex = i;
                        break;
                    }
                }
            }

            if (foundIndex < 0 || foundIndex >= kits.Count)
            {
                response = $"[RA - Arena1v1] Kit '{query}' not found. Use 1..{kits.Count}, kit ID, or 'random'.";
                return false;
            }

            _forcedLoadoutIndex = foundIndex;
            ArenaLoadoutSet chosen = kits[_forcedLoadoutIndex];
            if (_activeDuel != null)
            {
                _activeDuel.CurrentLoadout = chosen;
                if (_activeDuel.IsSoloSession && player != null && _activeDuel.Player1Id == player.PlayerId)
                {
                    SpawnSingleFighterOnArena(player, 1, _activeDuel.CurrentRound, chosen);
                }
            }

            response = $"[RA - Arena1v1] Forced loadout kit set to: {chosen.Name} ({chosen.Id})";
            return true;
        }

        private void ResolveArenaSpawnPoints(out Vector3 p1WorldPos, out float p1Yaw, out Vector3 p2WorldPos, out float p2Yaw)
        {
            Config cfg = Config;

            p1WorldPos = _customP1WorldPos.HasValue ? _customP1WorldPos.Value : (cfg.ArenaOrigin + cfg.Player1SpawnOffset);
            p2WorldPos = _customP2WorldPos.HasValue ? _customP2WorldPos.Value : (cfg.ArenaOrigin + cfg.Player2SpawnOffset);
            p1Yaw = _customP1WorldPos.HasValue ? _customP1Yaw : cfg.Player1SpawnYaw;
            p2Yaw = _customP2WorldPos.HasValue ? _customP2Yaw : cfg.Player2SpawnYaw;

            if (_spawnedArenaSchematic != null && !_customP1WorldPos.HasValue && !_customP2WorldPos.HasValue)
            {
                foreach (Transform child in _spawnedArenaSchematic.transform.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name == "Arena_Spawn_P1_BaseRing")
                        p1WorldPos = child.position + Vector3.up * 1.15f;
                    else if (child.name == "Arena_Spawn_P2_BaseRing")
                        p2WorldPos = child.position + Vector3.up * 1.15f;
                }
            }
        }

        private void EnsureArenaSchematicSpawned()
        {
            if (!Config.AutoSpawnSchematic || _spawnedArenaSchematic != null)
                return;

            Config cfg = Config;
            try
            {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type spawnerType = asm.GetType("ProjectMER.Features.ObjectSpawner");
                    if (spawnerType == null)
                        continue;

                    foreach (MethodInfo method in spawnerType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (method.Name != "SpawnSchematic")
                            continue;

                        ParameterInfo[] pars = method.GetParameters();
                        if (pars.Length >= 4 && pars[0].ParameterType == typeof(string) && pars[1].ParameterType == typeof(Vector3))
                        {
                            object[] invokeArgs = new object[pars.Length];
                            invokeArgs[0] = cfg.SchematicName;
                            invokeArgs[1] = cfg.ArenaOrigin;
                            invokeArgs[2] = Quaternion.identity;
                            invokeArgs[3] = Vector3.one;
                            for (int i = 4; i < pars.Length; i++)
                            {
                                invokeArgs[i] = pars[i].HasDefaultValue ? pars[i].DefaultValue : null;
                            }

                            _spawnedArenaSchematic = method.Invoke(null, invokeArgs) as UnityEngine.Component;
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LabApi.Features.Console.Logger.Error($"[Arena1v1] Failed to spawn ProjectMER schematic {cfg.SchematicName}: {ex.Message}");
            }
        }

        private void SpawnSingleFighterOnArena(Player fighter, int slotNumber, int roundNumber, ArenaLoadoutSet loadout)
        {
            Config cfg = Config;
            bool swap = cfg.SwapSidesEveryRound && (roundNumber % 2 == 0);

            ResolveArenaSpawnPoints(
                out Vector3 baseP1Pos,
                out float baseP1Yaw,
                out Vector3 baseP2Pos,
                out float baseP2Yaw
            );

            bool useP2Side = (slotNumber == 2) ? !swap : swap;
            Vector3 pos = useP2Side ? baseP2Pos : baseP1Pos;
            float yaw = useP2Side ? baseP2Yaw : baseP1Yaw;

            TeleportAndEquipFighter(fighter, cfg.DuelRole, pos, yaw, loadout);

            fighter.SendHint(
                $"<color=#00F0FF><b>[1v1 Arena - Round #{roundNumber}]</b></color>\n" +
                $"Kit: <color=#FACC15><b>{loadout.Name}</b></color>\n" +
                "<size=75%>Type <b>.arena</b> again to return to Spectator</size>",
                cfg.HintDurationSeconds
            );
        }

        private void SpawnBothFightersOnArena(Player p1, Player p2, ActiveDuelSession session)
        {
            Config cfg = Config;
            bool swap = cfg.SwapSidesEveryRound && (session.CurrentRound % 2 == 0);

            ResolveArenaSpawnPoints(
                out Vector3 baseP1Pos,
                out float baseP1Yaw,
                out Vector3 baseP2Pos,
                out float baseP2Yaw
            );

            Vector3 p1Pos = swap ? baseP2Pos : baseP1Pos;
            Vector3 p2Pos = swap ? baseP1Pos : baseP2Pos;

            float p1Yaw = swap ? baseP2Yaw : baseP1Yaw;
            float p2Yaw = swap ? baseP1Yaw : baseP2Yaw;

            ArenaLoadoutSet loadout = session.CurrentLoadout ?? PickRoundLoadoutSet();
            session.CurrentLoadout = loadout;
            session.RoundStartTime = Time.realtimeSinceStartup;

            TeleportAndEquipFighter(p1, cfg.DuelRole, p1Pos, p1Yaw, loadout);
            TeleportAndEquipFighter(p2, cfg.DuelRole, p2Pos, p2Yaw, loadout);

            int p1Wins = GetPlayerWins(p1);
            int p2Wins = GetPlayerWins(p2);

            string banner =
                $"<color=#00F0FF><b>[1v1 Arena - Round #{session.CurrentRound}]</b></color> - Kit: <color=#FACC15><b>{loadout.Name}</b></color>\n" +
                $"<color=#38BDF8>{p1.Nickname}</color> <b>({p1Wins})</b> vs <b>({p2Wins})</b> <color=#FB7185>{p2.Nickname}</color> " +
                $"<color=#34D399>(+{cfg.WinXpReward} XP)</color>\n" +
                $"<size=75%>Pool Size: {_arenaParticipants.Count} - Leave Arena: <b>.arena</b></size>";

            p1.SendHint(banner, cfg.HintDurationSeconds);
            p2.SendHint(banner, cfg.HintDurationSeconds);
        }

        private void TeleportAndEquipFighter(
            Player fighter,
            RoleTypeId targetRole,
            Vector3 worldPos,
            float yawDegrees,
            ArenaLoadoutSet loadout
        )
        {
            if (fighter == null || fighter.IsDestroyed)
                return;

            Config cfg = Config;
            if (fighter.Role != targetRole)
            {
                fighter.SetRole(targetRole, RoleChangeReason.RemoteAdmin, RoleSpawnFlags.None);
            }

            fighter.Position = worldPos;
            fighter.Rotation = Quaternion.Euler(0f, yawDegrees, 0f);

            Timing.CallDelayed(0.05f, () =>
            {
                if (fighter == null || fighter.IsDestroyed)
                    return;
                fighter.Position = worldPos;
                fighter.Rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            });

            float delay = cfg.EquipDelaySeconds > 0f ? cfg.EquipDelaySeconds : 0.15f;
            Timing.CallDelayed(delay, () =>
            {
                if (fighter == null || fighter.IsDestroyed)
                    return;

                fighter.ClearInventory();
                fighter.Position = worldPos;
                fighter.Rotation = Quaternion.Euler(0f, yawDegrees, 0f);

                float targetHp = loadout.HealthOverride > 0f ? loadout.HealthOverride : cfg.DefaultHealth;
                fighter.Health = targetHp > 0f ? targetHp : fighter.MaxHealth;

                float targetAhp = loadout.AhpOverride > 0f ? loadout.AhpOverride : cfg.DefaultAhp;
                if (targetAhp > 0f)
                {
                    fighter.ArtificialHealth = targetAhp;
                }

                if (loadout.Armor != ItemType.None)
                    fighter.AddItem(loadout.Armor);

                if (loadout.PrimaryWeapon != ItemType.None)
                    fighter.AddItem(loadout.PrimaryWeapon);

                if (loadout.SecondaryWeapon != ItemType.None)
                    fighter.AddItem(loadout.SecondaryWeapon);

                if (loadout.HealingAndUtility != null)
                {
                    for (int i = 0; i < loadout.HealingAndUtility.Count; i++)
                    {
                        if (loadout.HealingAndUtility[i] != ItemType.None)
                            fighter.AddItem(loadout.HealingAndUtility[i]);
                    }
                }

                if (loadout.PrimaryAmmoType != ItemType.None)
                    fighter.SetAmmo(loadout.PrimaryAmmoType, loadout.PrimaryAmmoAmount);

                if (loadout.SecondaryAmmoType != ItemType.None && loadout.SecondaryAmmoType != loadout.PrimaryAmmoType)
                    fighter.SetAmmo(loadout.SecondaryAmmoType, loadout.SecondaryAmmoAmount);
            });
        }

        private void CleanupDroppedArenaPickups()
        {
            Config cfg = Config;
            float radius = cfg.CleanupDroppedItemsRadius > 0f ? cfg.CleanupDroppedItemsRadius : 35f;
            foreach (Pickup pickup in Pickup.List)
            {
                if (pickup != null && Vector3.Distance(pickup.Position, cfg.ArenaOrigin) <= radius)
                {
                    pickup.Destroy();
                }
            }
        }
    }
}
