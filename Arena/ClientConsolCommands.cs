using System;
using System.Collections.Generic;
using System.Text;
using CommandSystem;
using LabApi.Features.Wrappers;
using RemoteAdmin;

namespace Arena1v1
{
    [CommandHandler(typeof(ClientCommandHandler))]
    public sealed class ArenaCommand : CommandSystem.ICommand
    {
        public string Command => "arena";
        public string[] Aliases => new[] { "1v1", "duel", "pvp" };
        public string Description => "Join or leave the 1v1 PvP Arena pool (.arena)";

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            if (!Player.TryGet(sender, out Player player) || player == null)
            {
                response = "The .arena command can only be used by in-game players.";
                return false;
            }

            if (Arena1v1Plugin.Instance == null)
            {
                response = "Arena1v1 plugin is not initialized.";
                return false;
            }

            return Arena1v1Plugin.Instance.ToggleArenaParticipation(player, out response);
        }
    }
    [CommandHandler(typeof(ClientCommandHandler))]
    public sealed class ArenaStatsClientCommand : CommandSystem.ICommand
    {
        public string Command => "stats";
        public string[] Aliases => new[] { "arenastats", "1v1stats", "mystats" };
        public string Description => "Display your personal 1v1 Arena statistics (.stats)";

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            if (!Player.TryGet(sender, out Player player) || player == null)
            {
                response = "The .stats command can only be used by in-game players.";
                return false;
            }

            ArenaPlayerStatsRecord rec = ArenaStatsManager.GetPlayerStats(player.UserId, player.Nickname);
            int rank = ArenaStatsManager.GetPlayerRank(player.UserId);
            string rankStr = rank > 0 ? $"#{rank}" : "Unranked";

            player.SendHint(
                $"<color=#00F0FF><b>[1v1 Arena Stats - {player.Nickname}]</b></color>\n" +
                $"Rank: <color=#FACC15><b>{rankStr}</b></color> | Wins: <color=#34D399><b>{rec.Wins}</b></color> | Losses: <color=#FB7185><b>{rec.Losses}</b></color>\n" +
                $"Win Rate: <b>{rec.WinRatePercent}%</b> (K/D: <b>{rec.KdRatio}</b>) | Streak: <b>{rec.CurrentStreak}</b> (Best: <b>{rec.BestStreak}</b>) | XP: <color=#38BDF8><b>{rec.TotalXpEarned} XP</b></color>",
                7f
            );

            response =
                $"=== 1v1 Arena Statistics ({player.Nickname}) ===\n" +
                $"- Server Rank: {rankStr}\n" +
                $"- Wins / Losses: {rec.Wins} / {rec.Losses} (Matches: {rec.TotalMatches})\n" +
                $"- Win Rate: {rec.WinRatePercent}% | K/D Ratio: {rec.KdRatio}\n" +
                $"- Current Win Streak: {rec.CurrentStreak} (Best Streak: {rec.BestStreak})\n" +
                $"- Total Arena XP Earned: {rec.TotalXpEarned} XP\n" +
                $"- Last Winning Kit: {rec.LastLoadoutSet}";
            return true;
        }
    }
    [CommandHandler(typeof(ClientCommandHandler))]
    public sealed class ArenaTopClientCommand : CommandSystem.ICommand
    {
        public string Command => "top";
        public string[] Aliases => new[] { "arenatop", "1v1top", "leaderboard" };
        public string Description => "Display the Top-10 1v1 Arena leaderboard (.top)";

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            List<ArenaPlayerStatsRecord> top = ArenaStatsManager.GetTopPlayers(10);
            if (top.Count == 0)
            {
                response = "[1v1 Arena - Top 10] No matches recorded yet. Join a duel via .arena!";
                return true;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== TOP 10 1V1 ARENA LEADERBOARD ===");
            for (int i = 0; i < top.Count; i++)
            {
                ArenaPlayerStatsRecord r = top[i];
                sb.AppendLine(
                    $"#{i + 1}. {r.Nickname} - Wins: {r.Wins} | Losses: {r.Losses} | WR: {r.WinRatePercent}% | Best Streak: {r.BestStreak} | {r.TotalXpEarned} XP"
                );
            }

            if (Player.TryGet(sender, out Player player) && player != null)
            {
                StringBuilder hintSb = new StringBuilder();
                hintSb.AppendLine("<color=#FACC15><b>[1v1 Arena Leaderboard]</b></color>");
                int showCount = Math.Min(5, top.Count);
                for (int i = 0; i < showCount; i++)
                {
                    ArenaPlayerStatsRecord r = top[i];
                    hintSb.AppendLine($"<b>#{i + 1}</b> <color=#38BDF8>{r.Nickname}</color> - <color=#34D399><b>{r.Wins} Wins</b></color> ({r.WinRatePercent}% WR)");
                }
                player.SendHint(hintSb.ToString(), 7f);
            }

            response = sb.ToString();
            return true;
        }
    }
}
