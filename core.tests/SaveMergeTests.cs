using System.Collections.Generic;
using Blockfall.Core;
using Xunit;

namespace Blockfall.Core.Tests;

public class SaveMergeTests
{
    // "sprint" is our stand-in time-attack mode (lower is better); "marathon" a score mode.
    private static bool IsTimeAttack(string key) => key == "sprint" || key == "dig";

    [Fact]
    public void MergeBest_KeepsHigherScore_AndLowerTime()
    {
        var local = new Dictionary<string, double> { ["marathon"] = 5000, ["sprint"] = 42.0 };
        var cloud = new Dictionary<string, double> { ["marathon"] = 8000, ["sprint"] = 55.0, ["ultra"] = 999 };

        SaveMerge.MergeBest(local, cloud, IsTimeAttack);

        Assert.Equal(8000, local["marathon"]); // score: higher wins
        Assert.Equal(42.0, local["sprint"]);   // time: lower wins
        Assert.Equal(999, local["ultra"]);     // cloud-only key added
    }

    [Fact]
    public void MergeMax_TakesPerKeyMaximum()
    {
        var local = new Dictionary<string, double> { ["2026-07-10"] = 100 };
        var cloud = new Dictionary<string, double> { ["2026-07-10"] = 250, ["2026-07-11"] = 50 };

        SaveMerge.MergeMax(local, cloud);

        Assert.Equal(250, local["2026-07-10"]);
        Assert.Equal(50, local["2026-07-11"]);
    }

    [Fact]
    public void MergeUnion_AddsMissing_NoDuplicates()
    {
        var local = new List<string> { "a", "b" };
        SaveMerge.MergeUnion(local, new[] { "b", "c", "a", "d" });
        Assert.Equal(new[] { "a", "b", "c", "d" }, local);
    }

    [Fact]
    public void MergeCounts_TakesMax_NotSum()
    {
        var local = new Dictionary<string, int> { ["undo"] = 3 };
        var cloud = new Dictionary<string, int> { ["undo"] = 5, ["hint"] = 2 };

        SaveMerge.MergeCounts(local, cloud);

        Assert.Equal(5, local["undo"]); // max, not 8 — a synced grant isn't double-counted
        Assert.Equal(2, local["hint"]);
    }

    [Fact]
    public void MergeLifetime_TakesMaxOfEveryCounter()
    {
        var local = new LifetimeStats { GamesPlayed = 10, TotalScore = 5000, Tetrises = 3, BestCombo = 4 };
        local.ModeGames["marathon"] = 6;
        var cloud = new LifetimeStats { GamesPlayed = 8, TotalScore = 9000, Tetrises = 7, BestCombo = 2 };
        cloud.ModeGames["marathon"] = 9;
        cloud.ModeGames["sprint"] = 1;

        SaveMerge.MergeLifetime(local, cloud);

        Assert.Equal(10, local.GamesPlayed);   // local ahead
        Assert.Equal(9000, local.TotalScore);  // cloud ahead
        Assert.Equal(7, local.Tetrises);
        Assert.Equal(4, local.BestCombo);
        Assert.Equal(9, local.ModeGames["marathon"]);
        Assert.Equal(1, local.ModeGames["sprint"]);
    }

    [Fact]
    public void MergeLeaderboards_PoolsDedupesSortsAndTrims()
    {
        var shared = new LeaderboardEntry(5000, 0, 40, seed: 7, dateUnix: 100);
        var local = new Dictionary<string, List<LeaderboardEntry>>
        {
            ["marathon"] = new() { new LeaderboardEntry(9000, 0, 50, 1, 10), shared },
        };
        var cloud = new Dictionary<string, List<LeaderboardEntry>>
        {
            ["marathon"] = new()
            {
                shared, // exact duplicate — must not double
                new LeaderboardEntry(12000, 0, 60, 2, 20),
                new LeaderboardEntry(3000, 0, 30, 3, 30),
            },
        };

        SaveMerge.MergeLeaderboards(local, cloud, _ => false, capacity: 3);

        var board = local["marathon"];
        Assert.Equal(3, board.Count);                 // trimmed to capacity
        Assert.Equal(12000, board[0].Score);          // sorted, highest first
        Assert.Equal(9000, board[1].Score);
        Assert.Equal(5000, board[2].Score);           // shared kept once, 3000 trimmed off
    }

    [Fact]
    public void MergeRank_PrefersMoreGames_KeepsMaxPeak()
    {
        var local = new RankRating { Rating = 1100, Peak = 1150, Wins = 3, Losses = 2 };   // 5 games
        var cloud = new RankRating { Rating = 1300, Peak = 1400, Wins = 6, Losses = 4 };   // 10 games

        var merged = SaveMerge.MergeRank(local, cloud);

        Assert.Equal(1300, merged.Rating);  // cloud has more history
        Assert.Equal(1400, merged.Peak);    // highest peak across both
    }

    [Fact]
    public void MergeRank_SamerGames_KeepsLocal_ButMaxPeak()
    {
        var local = new RankRating { Rating = 1000, Peak = 1050, Wins = 2, Losses = 2 };
        var cloud = new RankRating { Rating = 900, Peak = 1200, Wins = 1, Losses = 3 };

        var merged = SaveMerge.MergeRank(local, cloud);

        Assert.Equal(1000, merged.Rating); // tie on games (4 each) -> local wins
        Assert.Equal(1200, merged.Peak);
    }
}
