using System;
using System.Collections.Generic;
using System.Text;
using CommandSystem;
using LabApi.Features.Wrappers;
using RemoteAdmin;

namespace Arena1v1
{
    [CommandHandler(typeof(RemoteAdminCommandHandler))]
    public sealed class ArenaAdminCommand : CommandSystem.ICommand
    {
        public string Command => "arena";
        public string[] Aliases => new[] { "arenaadmin", "1v1admin" };
        public string Description => "Manage the 1v1 Arena (solo, add, pool, win, set, kits, setp1, setp2, stop)";

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            if (Arena1v1Plugin.Instance == null)
            {
                response = "Arena1v1 plugin is not initialized.";
                return false;
            }

            Player.TryGet(sender, out Player adminPlayer);

            if (arguments.Count == 0)
            {
                response =
                    "[RemoteAdmin - Arena1v1] Available subcommands:\n" +
                    "- arena solo [ID/Name]       : Spawn in solo arena test mode\n" +
                    "- arena add <ID/Name|bot>    : Force-add a player or bot to the arena or rotation pool\n" +
                    "- arena pool                 : View current active fighters and waiting pool queue\n" +
                    "- arena win [ID/Name]        : Trigger round victory (+XP and stats record)\n" +
                    "- arena set <id|num|random>  : Force a specific loadout kit or reset to random\n" +
                    "- arena kits                 : List all configured loadout kits\n" +
                    "- arena setp1 / setp2        : Save Fighter 1 or Fighter 2 spawn at your current position\n" +
                    "- arena stop                 : Stop active duel and clear the arena pool";
                return true;
            }

            string sub = arguments.At(0).ToLowerInvariant();

            if (sub == "kits" || sub == "list")
            {
                return Arena1v1Plugin.Instance.ListConfiguredKits(out response);
            }

            if (sub == "pool" || sub == "status" || sub == "queue")
            {
                return Arena1v1Plugin.Instance.InspectArenaPool(out response);
            }

            if (sub == "solo" || sub == "test" || sub == "1")
            {
                Player target = adminPlayer;
                if (arguments.Count >= 2)
                {
                    target = Arena1v1Plugin.Instance.FindTargetPlayer(arguments.At(1), adminPlayer);
                }

                if (target == null)
                {
                    response = "[RA - Arena1v1] Target player not found. Usage: arena solo <ID/Name>";
                    return false;
                }

                return Arena1v1Plugin.Instance.StartOrJoinSoloArena(target, out response);
            }

            if (sub == "add" || sub == "force" || sub == "tp" || sub == "put" || sub == "spawn")
            {
                if (arguments.Count < 2)
                {
                    response = "[RA - Arena1v1] Please specify a player ID, nickname, or 'bot': arena add <ID/Name|bot>";
                    return false;
                }

                string targetQuery = arguments.At(1);
                Player target = Arena1v1Plugin.Instance.FindTargetPlayer(targetQuery, adminPlayer);
                if (target == null)
                {
                    response = $"[RA - Arena1v1] Player or bot '{targetQuery}' was not found.";
                    return false;
                }

                return Arena1v1Plugin.Instance.ForceAddPlayerToArena(target, out response);
            }

            if (sub == "win" || sub == "xp")
            {
                Player target = adminPlayer;
                if (arguments.Count >= 2)
                {
                    target = Arena1v1Plugin.Instance.FindTargetPlayer(arguments.At(1), adminPlayer);
                }

                if (target == null)
                {
                    response = "[RA - Arena1v1] Target player not found. Usage: arena win <ID/Name>";
                    return false;
                }

                return Arena1v1Plugin.Instance.SimulateRoundWinForTesting(target, out response);
            }

            if (sub == "set" && arguments.Count >= 2)
            {
                return Arena1v1Plugin.Instance.ForceSelectLoadoutSet(adminPlayer, arguments.At(1), out response);
            }

            if (sub == "stop" || sub == "clear" || sub == "reset")
            {
                return Arena1v1Plugin.Instance.ForceStopAllDuelsFromAdmin(out response);
            }

            if (sub == "setp1")
            {
                if (adminPlayer == null)
                {
                    response = "[RA - Arena1v1] You must be in-game to run setp1.";
                    return false;
                }
                Arena1v1Plugin.Instance.SetCustomSpawnPoint(1, adminPlayer.Position, adminPlayer.Rotation.eulerAngles.y);
                response = $"[RA - Arena1v1] Fighter 1 spawn point saved at {adminPlayer.Position}";
                return true;
            }

            if (sub == "setp2")
            {
                if (adminPlayer == null)
                {
                    response = "[RA - Arena1v1] You must be in-game to run setp2.";
                    return false;
                }
                Arena1v1Plugin.Instance.SetCustomSpawnPoint(2, adminPlayer.Position, adminPlayer.Rotation.eulerAngles.y);
                response = $"[RA - Arena1v1] Fighter 2 spawn point saved at {adminPlayer.Position}";
                return true;
            }

            if (adminPlayer != null)
            {
                return Arena1v1Plugin.Instance.ToggleArenaParticipation(adminPlayer, out response);
            }

            response = "[RA - Arena1v1] Unknown subcommand. Type 'arena' with no arguments for help.";
            return false;
        }
    }
    [CommandHandler(typeof(RemoteAdminCommandHandler))]
    public sealed class ArenaStatsRemoteAdminCommand : CommandSystem.ICommand
    {
        public string Command => "arenastats";
        public string[] Aliases => new[] { "astats", "1v1statsadmin" };
        public string Description => "Manage 1v1 Arena statistics file (top, info, reset, path)";

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            if (arguments.Count == 0)
            {
                response =
                    "[RemoteAdmin - ArenaStats] Available subcommands:\n" +
                    "- arenastats top                  : Show Top-10 arena players\n" +
                    "- arenastats info <ID/Name/UserId>: View statistics for a specific player\n" +
                    "- arenastats reset <UserId | all> : Reset stats for one player or wipe the entire file\n" +
                    $"- arenastats path                 : Show path to JSON file ({ArenaStatsManager.JsonFilePath})";
                return true;
            }

            string sub = arguments.At(0).ToLowerInvariant();

            if (sub == "path" || sub == "file")
            {
                response =
                    $"[RA - ArenaStats] Server statistics files:\n" +
                    $"- JSON: {ArenaStatsManager.JsonFilePath}\n" +
                    $"- DB:   {ArenaStatsManager.DbFilePath}";
                return true;
            }

            if (sub == "top")
            {
                List<ArenaPlayerStatsRecord> top = ArenaStatsManager.GetTopPlayers(10);
                if (top.Count == 0)
                {
                    response = "[RA - ArenaStats] The leaderboard is currently empty.";
                    return true;
                }

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("=== [RA] TOP 10 1V1 ARENA LEADERBOARD ===");
                for (int i = 0; i < top.Count; i++)
                {
                    ArenaPlayerStatsRecord r = top[i];
                    sb.AppendLine($"#{i + 1}. {r.Nickname} ({r.UserId}) - W:{r.Wins} L:{r.Losses} ({r.WinRatePercent}%) | Streak:{r.BestStreak} | {r.TotalXpEarned} XP");
                }
                response = sb.ToString();
                return true;
            }

            if (sub == "reset" && arguments.Count >= 2)
            {
                string targetKey = arguments.At(1);
                bool ok = ArenaStatsManager.ResetStats(targetKey);
                response = ok
                    ? $"[RA - ArenaStats] Statistics for '{targetKey}' have been reset."
                    : $"[RA - ArenaStats] Record '{targetKey}' was not found.";
                return ok;
            }

            if (sub == "info" && arguments.Count >= 2)
            {
                string query = arguments.At(1);
                Player target = Arena1v1Plugin.Instance != null
                    ? Arena1v1Plugin.Instance.FindTargetPlayer(query, null)
                    : null;

                string userId = target != null ? target.UserId : query;
                string nick = target != null ? target.Nickname : query;
                ArenaPlayerStatsRecord rec = ArenaStatsManager.GetPlayerStats(userId, nick);
                int rank = ArenaStatsManager.GetPlayerRank(userId);

                response =
                    $"[RA - Player Stats: {rec.Nickname} ({rec.UserId})]\n" +
                    $"- Rank: #{rank} | Wins: {rec.Wins} | Losses: {rec.Losses} | WR: {rec.WinRatePercent}% | K/D: {rec.KdRatio}\n" +
                    $"- Streak: {rec.CurrentStreak} (Best: {rec.BestStreak}) | XP: {rec.TotalXpEarned} XP | Last Kit: {rec.LastLoadoutSet}";
                return true;
            }

            response = "[RA - ArenaStats] Unknown subcommand. Type 'arenastats' for help.";
            return false;
        }
    }
}
