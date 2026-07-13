using System.Collections.Generic;
using Blockfall.Core;
using Xunit;

namespace Blockfall.Core.Tests;

public class ProgressionTests
{
    private static RunStats Stats(int lines = 0, int quads = 0, int tspins = 0, int combo = 0,
        int b2b = 0, int pieces = 0, double finish = 0, int perfect = 0)
        => new()
        {
            TotalLines = lines, Quads = quads, TSpins = tspins, MaxCombo = combo,
            MaxBackToBack = b2b, PiecesPlaced = pieces, FinishTime = finish, PerfectClears = perfect,
        };

    // ---- LifetimeStats ----------------------------------------------------
    [Fact]
    public void Lifetime_Fold_AccumulatesAndKeepsBests()
    {
        var life = new LifetimeStats();
        life.Fold(GameModeId.Marathon, Stats(lines: 10, quads: 2, tspins: 1, combo: 4, b2b: 3, pieces: 40, finish: 40), 5000, 40, completed: true);
        life.Fold(GameModeId.Sprint, Stats(lines: 40, quads: 4, tspins: 0, combo: 8, b2b: 2, pieces: 100, finish: 50), 0, 50, completed: true);

        Assert.Equal(2, life.GamesPlayed);
        Assert.Equal(50, life.TotalLines);
        Assert.Equal(140, life.TotalPieces);
        Assert.Equal(5000, life.TotalScore);
        Assert.Equal(90, life.TotalPlaytime);
        Assert.Equal(6, life.Tetrises);       // 2 + 4
        Assert.Equal(8, life.BestCombo);      // max(4,8)
        Assert.Equal(3, life.BestBackToBack); // max(3,2)
        Assert.Equal(1, life.GamesInMode(GameModeId.Marathon));
        Assert.Equal(1, life.CompletionsInMode(GameModeId.Sprint));
    }

    // ---- Achievements -----------------------------------------------------
    private static AchievementContext Ctx(LifetimeStats life, GameModeId mode, RunStats run, long score, double time, bool completed)
        => new() { Lifetime = life, Mode = mode, Run = run, Score = score, Time = time, Completed = completed };

    [Fact]
    public void Achievements_UnlockOnCondition_AndNeverRepeat()
    {
        var life = new LifetimeStats();
        var unlocked = new HashSet<string>();

        // A completed Marathon with a tetris + first line.
        life.Fold(GameModeId.Marathon, Stats(lines: 10, quads: 1), 3000, 60, true);
        var fresh = AchievementEngine.Evaluate(unlocked, Ctx(life, GameModeId.Marathon, Stats(lines: 10, quads: 1), 3000, 60, true));

        Assert.Contains("first_line", fresh);
        Assert.Contains("first_tetris", fresh);
        Assert.Contains("marathon", fresh);
        foreach (var id in fresh) unlocked.Add(id);

        // Evaluating the same context again yields nothing new.
        var again = AchievementEngine.Evaluate(unlocked, Ctx(life, GameModeId.Marathon, Stats(lines: 10, quads: 1), 3000, 60, true));
        Assert.Empty(again);
    }

    [Fact]
    public void Achievement_SprintSub1_RequiresCompletedFastSprint()
    {
        var life = new LifetimeStats();
        var slow = AchievementEngine.Evaluate(new HashSet<string>(), Ctx(life, GameModeId.Sprint, Stats(), 0, 75, true));
        Assert.Contains("sprint_sub2", slow);
        Assert.DoesNotContain("sprint_sub1", slow);

        var fast = AchievementEngine.Evaluate(new HashSet<string>(), Ctx(life, GameModeId.Sprint, Stats(), 0, 55, true));
        Assert.Contains("sprint_sub1", fast);

        // Not completed -> no sprint achievements.
        var dnf = AchievementEngine.Evaluate(new HashSet<string>(), Ctx(life, GameModeId.Sprint, Stats(), 0, 40, false));
        Assert.DoesNotContain("sprint_sub2", dnf);
    }

    [Fact]
    public void AllAchievements_HaveUniqueIds()
    {
        var ids = new HashSet<string>();
        foreach (var a in AchievementCatalog.All)
            Assert.True(ids.Add(a.Id), $"duplicate id {a.Id}");
        Assert.True(AchievementCatalog.All.Count >= 15);
    }

    // ---- Leaderboard ------------------------------------------------------
    [Fact]
    public void Leaderboard_ScoreMode_RanksHighestFirst_AndTrims()
    {
        var entries = new List<LeaderboardEntry>();
        for (int i = 0; i < 12; i++)
            LeaderboardLogic.Insert(entries, new LeaderboardEntry(i * 100, 0, 0, 0, i), timeAttack: false, capacity: 10);

        Assert.Equal(10, entries.Count);          // trimmed
        Assert.Equal(1100, entries[0].Score);     // highest first
        Assert.Equal(200, entries[9].Score);      // lowest survivor (0 and 100 dropped)
    }

    [Fact]
    public void Leaderboard_TimeMode_RanksFastestFirst()
    {
        var entries = new List<LeaderboardEntry>();
        LeaderboardLogic.Insert(entries, new LeaderboardEntry(0, 55.0, 40, 0, 1), timeAttack: true);
        LeaderboardLogic.Insert(entries, new LeaderboardEntry(0, 42.0, 40, 0, 2), timeAttack: true);
        int rank = LeaderboardLogic.Insert(entries, new LeaderboardEntry(0, 48.0, 40, 0, 3), timeAttack: true);

        Assert.Equal(42.0, entries[0].TimeSeconds);
        Assert.Equal(1, rank); // 48s slots between 42 and 55
    }

    [Fact]
    public void Leaderboard_Qualifies_TimeAttackRequiresCompletion()
    {
        // Score mode: any run that scored qualifies, completed or not.
        Assert.True(LeaderboardLogic.Qualifies(timeAttack: false, score: 100, completed: false, eligible: true));
        // Time-attack: an incomplete run must NOT qualify (its partial time would rank too high).
        Assert.False(LeaderboardLogic.Qualifies(timeAttack: true, score: 100, completed: false, eligible: true));
        Assert.True(LeaderboardLogic.Qualifies(timeAttack: true, score: 0, completed: true, eligible: true));
        // Ineligible (modified/revived/daily) is always rejected.
        Assert.False(LeaderboardLogic.Qualifies(timeAttack: false, score: 100, completed: true, eligible: false));
    }

    [Fact]
    public void Leaderboard_ReturnsMinusOne_WhenEntryDropsOff()
    {
        var entries = new List<LeaderboardEntry>();
        for (int i = 0; i < 10; i++)
            LeaderboardLogic.Insert(entries, new LeaderboardEntry(1000 + i, 0, 0, 0, i), timeAttack: false, capacity: 10);
        int rank = LeaderboardLogic.Insert(entries, new LeaderboardEntry(1, 0, 0, 0, 99), timeAttack: false, capacity: 10);
        Assert.Equal(-1, rank); // worse than all 10 -> trimmed away
    }
}
