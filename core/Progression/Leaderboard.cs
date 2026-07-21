using System.Collections.Generic;

namespace Blockfall.Core;

/// <summary>One local leaderboard row. A reference type so identity is stable for ranking.</summary>
public sealed class LeaderboardEntry
{
    public long Score { get; set; }
    public double TimeSeconds { get; set; }
    public int Lines { get; set; }
    public ulong Seed { get; set; }
    public long DateUnix { get; set; }
    public string? ReplayPath { get; set; } // if a replay was saved for this run

    /// <summary>Descent: strata cleared (0 elsewhere). Old saves deserialize to 0 —
    /// the JSON-append-compatible pattern the whole SaveData relies on.</summary>
    public int Depth { get; set; }

    public LeaderboardEntry() { }

    public LeaderboardEntry(long score, double time, int lines, ulong seed, long dateUnix, string? replayPath = null)
    {
        Score = score; TimeSeconds = time; Lines = lines; Seed = seed; DateUnix = dateUnix; ReplayPath = replayPath;
    }
}

/// <summary>
/// Sorting + trimming for a single per-mode leaderboard. Score modes rank highest-
/// first; time-attack modes (Sprint / Dig) rank fastest-first. Pure and testable.
/// </summary>
public static class LeaderboardLogic
{
    public const int Capacity = 10;

    /// <summary>Insert an entry, re-sort, trim to capacity, and return its final rank (0-based) or -1 if it dropped off.</summary>
    public static int Insert(List<LeaderboardEntry> entries, LeaderboardEntry entry, bool timeAttack, int capacity = Capacity)
    {
        entries.Add(entry);
        Sort(entries, timeAttack);
        if (entries.Count > capacity)
            entries.RemoveRange(capacity, entries.Count - capacity);
        return entries.IndexOf(entry); // reference identity; -1 if trimmed away
    }

    /// <summary>
    /// Whether a run may be posted to a leaderboard. Time-attack boards rank by time
    /// ascending, so an incomplete run's short partial time must NOT qualify; score
    /// boards accept any run that actually scored. <paramref name="eligible"/> folds in
    /// the run-level gates (unmodified, non-revived, ranked mode).
    /// </summary>
    public static bool Qualifies(bool timeAttack, long score, bool completed, bool eligible)
    {
        if (!eligible) return false;
        return timeAttack ? completed : (score > 0 || completed);
    }

    public static void Sort(List<LeaderboardEntry> entries, bool timeAttack)
    {
        // A board containing depth-carrying entries (Descent) ranks by depth first —
        // descending IS the achievement — with banked score as the tiebreaker.
        // Auto-detected per board so merge/insert paths need no extra plumbing;
        // legacy entries (depth 0) are unaffected.
        bool depthFirst = entries.Exists(e => e.Depth > 0);
        entries.Sort((a, b) =>
        {
            if (depthFirst)
            {
                int d = b.Depth.CompareTo(a.Depth);
                if (d != 0) return d;
                return b.Score.CompareTo(a.Score);
            }
            return timeAttack
                ? a.TimeSeconds.CompareTo(b.TimeSeconds)
                : b.Score.CompareTo(a.Score);
        });
    }
}
