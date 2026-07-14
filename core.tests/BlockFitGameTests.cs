using System.Collections.Generic;
using Blockfall.Core;
using Blockfall.Core.BlockFit;
using Xunit;

namespace Blockfall.Core.Tests;

public class BlockFitGameTests
{
    [Fact]
    public void FreshGame_HasThreePieces_NotOver()
    {
        var g = new BlockFitGame(seed: 42);
        Assert.False(g.GameOver);
        Assert.Equal(0, g.Score);
        int pieces = 0;
        foreach (var p in g.Tray) if (p is not null) pieces++;
        Assert.Equal(3, pieces);
    }

    [Fact]
    public void TryPlace_CommitsCells_AndScores()
    {
        // Empty board; 3 pieces so placing slot 0 doesn't empty the tray (no refill).
        var single = new BlockPiece(new[] { (0, 0) }, PieceType.I);
        var other = new BlockPiece(new[] { (0, 0) }, PieceType.O);
        var g = new BlockFitGame(new PieceType[64], new BlockPiece?[] { single, other, other });

        Assert.True(g.TryPlace(0, 3, 4));
        Assert.Equal(PieceType.I, g.At(3, 4));
        Assert.True(g.Score >= 1);
        Assert.Null(g.Tray[0]);            // slot 0 emptied; slots 1&2 remain → no refill
        Assert.NotNull(g.Tray[1]);
        Assert.False(g.TryPlace(0, 0, 0)); // slot 0 is empty now
    }

    [Fact]
    public void FillingARow_ClearsIt()
    {
        // Pre-fill row 0 cols 0..6; a single piece completes col 7 → the row clears.
        var grid = new PieceType[64];
        for (int c = 0; c < 7; c++) grid[0 * 8 + c] = PieceType.O;
        var single = new BlockPiece(new[] { (0, 0) }, PieceType.T);
        var g = new BlockFitGame(grid, new BlockPiece?[] { single, null, null });

        Assert.True(g.TryPlace(0, 0, 7));
        // Entire row 0 must now be empty (cleared), and the clear is recorded.
        for (int c = 0; c < 8; c++) Assert.Equal(PieceType.Empty, g.At(0, c));
        Assert.Equal(1, g.LastClearedRows);
        Assert.Equal(1, g.Streak);
        Assert.True(g.Score > 1); // placement point + line-clear bonus
    }

    [Fact]
    public void NoFittingPiece_IsGameOver()
    {
        // Completely full board + a piece that cannot fit anywhere.
        var grid = new PieceType[64];
        for (int i = 0; i < 64; i++) grid[i] = PieceType.Z;
        var single = new BlockPiece(new[] { (0, 0) }, PieceType.I);
        var g = new BlockFitGame(grid, new BlockPiece?[] { single, null, null });

        Assert.False(g.AnyMovePossible());
        Assert.False(g.TryPlace(0, 0, 0)); // nothing fits
    }

    [Fact]
    public void SameSeed_IsDeterministic()
    {
        var a = new BlockFitGame(seed: 7);
        var b = new BlockFitGame(seed: 7);
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(a.Tray[i]!.Color, b.Tray[i]!.Color);
            Assert.Equal(a.Tray[i]!.Cells.Count, b.Tray[i]!.Cells.Count);
        }
    }
}
