using Blockfall.Core;
using Xunit;

namespace Blockfall.Core.Tests;

public class AttackTableTests
{
    private static ClearResult Clear(int lines, SpinType spin = SpinType.None,
                                     bool b2b = false, int combo = 0, bool pc = false)
        => new()
        {
            LinesCleared = lines,
            Spin = spin,
            BackToBack = b2b,
            ComboCount = combo,
            PerfectClear = pc,
        };

    [Theory]
    [InlineData(1, 0)] // single sends nothing
    [InlineData(2, 1)] // double
    [InlineData(3, 2)] // triple
    [InlineData(4, 4)] // tetris
    public void PlainClears_MatchGuideline(int lines, int expected)
        => Assert.Equal(expected, AttackTable.LinesSent(Clear(lines)));

    [Fact]
    public void TetrisBackToBack_AddsOne() => Assert.Equal(5, AttackTable.LinesSent(Clear(4, b2b: true)));

    [Theory]
    [InlineData(1, 2)] // T-spin single
    [InlineData(2, 4)] // T-spin double
    [InlineData(3, 6)] // T-spin triple
    public void TSpins_MatchGuideline(int lines, int expected)
        => Assert.Equal(expected, AttackTable.LinesSent(Clear(lines, SpinType.Full)));

    [Fact]
    public void TSpinDouble_B2B_Is5() => Assert.Equal(5, AttackTable.LinesSent(Clear(2, SpinType.Full, b2b: true)));

    [Fact]
    public void MiniTSpin_SendsLittle()
    {
        Assert.Equal(0, AttackTable.LinesSent(Clear(1, SpinType.Mini)));
        Assert.Equal(1, AttackTable.LinesSent(Clear(2, SpinType.Mini)));
    }

    [Fact]
    public void SpinWithNoLines_SendsNothing()
        => Assert.Equal(0, AttackTable.LinesSent(Clear(0, SpinType.Full)));

    [Fact]
    public void Combo_AddsToAttack()
    {
        // Double on a 4-chain (comboCount 3) => base 1 + combo 2 = 3.
        Assert.Equal(3, AttackTable.LinesSent(Clear(2, combo: 3)));
        // Long chain caps at +5.
        Assert.Equal(1 + 5, AttackTable.LinesSent(Clear(2, combo: 12)));
    }

    [Fact]
    public void PerfectClear_IsAHugeSwing()
    {
        // B2B tetris perfect clear: base 4 + b2b 1 + pc 10 = 15.
        Assert.Equal(15, AttackTable.LinesSent(Clear(4, b2b: true, pc: true)));
    }
}

public class GarbageTests
{
    [Fact]
    public void InsertGarbage_RaisesStack_AndAddsHoledRows()
    {
        var b = new Board();
        int floor = b.TotalRows - 1;
        b[floor, 5] = PieceType.T; // a block resting on the floor

        bool overflow = b.InsertGarbageLines(2, holeColumn: 3);

        Assert.False(overflow);
        Assert.Equal(PieceType.T, b[floor - 2, 5]);      // the block rose by 2
        Assert.Equal(2, b.GarbageRowCount());

        // Bottom two rows: garbage everywhere except the hole column.
        for (int row = floor - 1; row <= floor; row++)
        {
            Assert.Equal(PieceType.Empty, b[row, 3]);
            Assert.Equal(PieceType.Garbage, b[row, 0]);
            Assert.Equal(PieceType.Garbage, b[row, 9]);
        }
    }

    [Fact]
    public void InsertGarbage_PushingBlocksOffTop_ReportsOverflow()
    {
        var b = new Board();
        b[0, 0] = PieceType.Garbage; // occupy the very top row
        Assert.True(b.InsertGarbageLines(1, holeColumn: 4));
    }

    [Fact]
    public void FillingTheHole_ClearsTheGarbageRow()
    {
        var b = new Board();
        b.InsertGarbageLines(1, holeColumn: 2);
        Assert.Equal(1, b.GarbageRowCount());

        b[b.TotalRows - 1, 2] = PieceType.O; // plug the hole
        var cleared = b.ClearFullRows();

        Assert.Single(cleared);
        Assert.Equal(0, b.GarbageRowCount());
        Assert.True(b.IsEmpty());
    }
}
