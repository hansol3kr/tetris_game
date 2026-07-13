using Blockfall.Core;
using Xunit;

namespace Blockfall.Core.Tests;

public class ScoringTests
{
    [Fact]
    public void SingleLine_ScoresHundredTimesLevel()
    {
        var s = new Scoring(startLevel: 1);
        var r = s.ApplyLock(1, SpinType.None, false);
        Assert.Equal(100, r.ScoreGained);
        Assert.Equal(100, s.Score);
    }

    [Fact]
    public void Tetris_ScoresEightHundred()
    {
        var s = new Scoring(1);
        var r = s.ApplyLock(4, SpinType.None, false);
        Assert.Equal(800, r.ScoreGained);
    }

    [Fact]
    public void TSpinDouble_ScoresTwelveHundred()
    {
        var s = new Scoring(1);
        var r = s.ApplyLock(2, SpinType.Full, false);
        Assert.Equal(1200, r.ScoreGained);
    }

    [Fact]
    public void BackToBackTetris_AppliesOnePointFiveMultiplier()
    {
        var s = new Scoring(1);
        var first = s.ApplyLock(4, SpinType.None, false);
        Assert.False(first.BackToBack);
        Assert.Equal(800, first.ScoreGained);

        var second = s.ApplyLock(4, SpinType.None, false);
        Assert.True(second.BackToBack);
        // 800 * 1.5 = 1200 (combo is +1 here: 50*1*level added on top).
        // combo after two consecutive clears: ComboCount = 1 -> +50.
        Assert.Equal((int)System.Math.Round(800 * 1.5) + 50, second.ScoreGained);
    }

    [Fact]
    public void NonDifficultClear_BreaksBackToBack()
    {
        var s = new Scoring(1);
        s.ApplyLock(4, SpinType.None, false); // start B2B
        Assert.True(s.BackToBackActive);
        s.ApplyLock(1, SpinType.None, false); // single breaks it
        Assert.False(s.BackToBackActive);
    }

    [Fact]
    public void SpinWithoutLines_DoesNotBreakBackToBack()
    {
        var s = new Scoring(1);
        s.ApplyLock(4, SpinType.None, false);       // B2B on
        Assert.True(s.BackToBackActive);
        s.ApplyLock(0, SpinType.Full, false);       // T-spin no lines
        Assert.True(s.BackToBackActive);            // still active
    }

    [Fact]
    public void Combo_AddsFiftyPerComboPerLevel()
    {
        var s = new Scoring(1);
        s.ApplyLock(1, SpinType.None, false);           // combo 0
        var r2 = s.ApplyLock(1, SpinType.None, false);  // combo 1 -> +50
        Assert.Equal(100 + 50, r2.ScoreGained);
        var r3 = s.ApplyLock(1, SpinType.None, false);  // combo 2 -> +100
        Assert.Equal(100 + 100, r3.ScoreGained);
    }

    [Fact]
    public void Combo_ResetsWhenNoLineCleared()
    {
        var s = new Scoring(1);
        s.ApplyLock(1, SpinType.None, false);
        s.ApplyLock(1, SpinType.None, false); // combo building
        s.ApplyLock(0, SpinType.None, false); // drop with no clear resets combo
        Assert.Equal(-1, s.ComboCounter);
    }

    [Fact]
    public void LevelIncreases_EveryTenLines()
    {
        var s = new Scoring(1);
        for (int i = 0; i < 5; i++) s.ApplyLock(2, SpinType.None, false); // 10 lines
        Assert.Equal(2, s.Level);
    }

    [Fact]
    public void HardDrop_ScoresTwoPerCell_SoftOnePerCell()
    {
        var s = new Scoring(1);
        s.AddHardDrop(5);
        Assert.Equal(10, s.Score);
        s.AddSoftDrop(3);
        Assert.Equal(13, s.Score);
    }

    [Fact]
    public void PerfectClearTetris_AddsBigBonus()
    {
        var s = new Scoring(1);
        var r = s.ApplyLock(4, SpinType.None, perfectClear: true);
        Assert.True(r.PerfectClear);
        // 800 (tetris) + 2000 (non-b2b PC quad) = 2800 at level 1.
        Assert.Equal(2800, r.ScoreGained);
    }
}
