using System;
using System.Collections.Generic;

namespace Blockfall.Core;

/// <summary>
/// Conflict-free merge of two save snapshots — used when a cloud save and the local
/// save diverge (the player played offline on two devices). Engine-agnostic and
/// unit-tested. The guiding rule is "never lose the player's best": scores keep the
/// better value, collections union, and cumulative career counters take the MAX of
/// the two rather than summing (summing would double-count the shared history both
/// devices started from). The game layer calls these field-by-field on its save DTO.
/// </summary>
public static class SaveMerge
{
    /// <summary>Best-score map: for each mode keep the better result. Time-attack modes
    /// (lower time is better) keep the minimum; score modes keep the maximum.</summary>
    public static void MergeBest(IDictionary<string, double> local,
                                 IReadOnlyDictionary<string, double> cloud,
                                 Func<string, bool> isTimeAttack)
    {
        foreach (var (key, val) in cloud)
        {
            if (!local.TryGetValue(key, out var cur)) { local[key] = val; continue; }
            local[key] = isTimeAttack(key) ? Math.Min(cur, val) : Math.Max(cur, val);
        }
    }

    /// <summary>Per-key maximum (e.g. daily-challenge best score by date).</summary>
    public static void MergeMax(IDictionary<string, double> local, IReadOnlyDictionary<string, double> cloud)
    {
        foreach (var (key, val) in cloud)
            local[key] = local.TryGetValue(key, out var cur) ? Math.Max(cur, val) : val;
    }

    /// <summary>Union a set of ids (achievements, owned items) into <paramref name="local"/>, preserving order.</summary>
    public static void MergeUnion(IList<string> local, IEnumerable<string> cloud)
    {
        var seen = new HashSet<string>(local);
        foreach (var id in cloud)
            if (seen.Add(id)) local.Add(id);
    }

    /// <summary>Consumable counts: take the per-key MAX (not sum) so a synced grant isn't double-counted.</summary>
    public static void MergeCounts(IDictionary<string, int> local, IReadOnlyDictionary<string, int> cloud)
    {
        foreach (var (key, val) in cloud)
            local[key] = Math.Max(local.TryGetValue(key, out var cur) ? cur : 0, val);
    }

    /// <summary>
    /// Career totals: MAX of every counter. Summing would double-count the overlap
    /// both devices share; MAX keeps whichever device is further along without inflation.
    /// </summary>
    public static void MergeLifetime(LifetimeStats local, LifetimeStats cloud)
    {
        local.GamesPlayed = Math.Max(local.GamesPlayed, cloud.GamesPlayed);
        local.TotalScore = Math.Max(local.TotalScore, cloud.TotalScore);
        local.TotalLines = Math.Max(local.TotalLines, cloud.TotalLines);
        local.TotalPieces = Math.Max(local.TotalPieces, cloud.TotalPieces);
        local.TotalPlaytime = Math.Max(local.TotalPlaytime, cloud.TotalPlaytime);

        local.Singles = Math.Max(local.Singles, cloud.Singles);
        local.Doubles = Math.Max(local.Doubles, cloud.Doubles);
        local.Triples = Math.Max(local.Triples, cloud.Triples);
        local.Tetrises = Math.Max(local.Tetrises, cloud.Tetrises);
        local.TSpins = Math.Max(local.TSpins, cloud.TSpins);
        local.TSpinMinis = Math.Max(local.TSpinMinis, cloud.TSpinMinis);
        local.PerfectClears = Math.Max(local.PerfectClears, cloud.PerfectClears);

        local.BestCombo = Math.Max(local.BestCombo, cloud.BestCombo);
        local.BestBackToBack = Math.Max(local.BestBackToBack, cloud.BestBackToBack);
        local.BestPps = Math.Max(local.BestPps, cloud.BestPps);

        MergeCountMap(local.ModeGames, cloud.ModeGames);
        MergeCountMap(local.ModeCompletions, cloud.ModeCompletions);
    }

    private static void MergeCountMap(IDictionary<string, int> local, IReadOnlyDictionary<string, int> cloud)
    {
        foreach (var (key, val) in cloud)
            local[key] = Math.Max(local.TryGetValue(key, out var cur) ? cur : 0, val);
    }

    /// <summary>
    /// Per-mode leaderboards: pool both sides, drop exact duplicates (the same run
    /// synced twice), re-sort each board, and trim to capacity.
    /// </summary>
    public static void MergeLeaderboards(IDictionary<string, List<LeaderboardEntry>> local,
                                         IReadOnlyDictionary<string, List<LeaderboardEntry>> cloud,
                                         Func<string, bool> isTimeAttack,
                                         int capacity = LeaderboardLogic.Capacity)
    {
        foreach (var (key, cloudList) in cloud)
        {
            if (!local.TryGetValue(key, out var board))
                local[key] = board = new List<LeaderboardEntry>();

            var seen = new HashSet<(long, double, ulong, long)>();
            foreach (var e in board) seen.Add(Identity(e));
            foreach (var e in cloudList)
                if (seen.Add(Identity(e))) board.Add(e);

            LeaderboardLogic.Sort(board, isTimeAttack(key));
            if (board.Count > capacity) board.RemoveRange(capacity, board.Count - capacity);
        }
    }

    private static (long, double, ulong, long) Identity(LeaderboardEntry e)
        => (e.Score, e.TimeSeconds, e.Seed, e.DateUnix);

    /// <summary>
    /// Reconcile two competitive ratings. The one with more games played is the more
    /// authoritative history, so it wins the live rating and record; peak is the max
    /// of both so a high-water mark reached on either device is never lost.
    /// </summary>
    public static RankRating MergeRank(RankRating local, RankRating cloud)
    {
        var winner = cloud.GamesPlayed > local.GamesPlayed ? cloud : local;
        winner.Peak = Math.Max(local.Peak, cloud.Peak);
        return winner;
    }
}
