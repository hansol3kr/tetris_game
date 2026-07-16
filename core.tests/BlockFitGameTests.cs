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
    public void DifficultyCurve_ZeroAtStart_Monotonic_MaxesOut()
    {
        Assert.Equal(0.0, BlockFitGame.DifficultyFor(0));
        Assert.True(BlockFitGame.DifficultyFor(600) > BlockFitGame.DifficultyFor(150));
        Assert.Equal(1.0, BlockFitGame.DifficultyFor(1_000_000));
        Assert.InRange(BlockFitGame.DifficultyFor(750), 0.0, 1.0);
    }

    [Fact]
    public void Difficulty_RisesAsScoreGrows()
    {
        // A 3×3 block (9 cells) on an empty board scores 9 and completes no line → no clear.
        var big = new BlockPiece(new[] { (0, 0), (0, 1), (0, 2), (1, 0), (1, 1), (1, 2), (2, 0), (2, 1), (2, 2) }, PieceType.O);
        var g = new BlockFitGame(new PieceType[64], new BlockPiece?[] { big, big, big });
        double before = g.Difficulty;
        Assert.True(g.TryPlace(0, 0, 0));
        Assert.True(g.Difficulty > before);
    }

    [Fact]
    public void LowDifficulty_FreshDeal_KeepsAMoveWhileRoomRemains()
    {
        // Board full of Z except a 2×2 empty pocket at rows 3-4, cols 3-4.
        var grid = new PieceType[64];
        for (int i = 0; i < 64; i++) grid[i] = PieceType.Z;
        grid[3 * 8 + 3] = PieceType.Empty; grid[3 * 8 + 4] = PieceType.Empty;
        grid[4 * 8 + 3] = PieceType.Empty; grid[4 * 8 + 4] = PieceType.Empty;
        // The one tray slot holds a single: placing it empties the tray → forces a fresh Deal.
        var single = new BlockPiece(new[] { (0, 0) }, PieceType.I);
        var g = new BlockFitGame(grid, new BlockPiece?[] { single, null, null });

        Assert.True(g.TryPlace(0, 3, 3)); // fills one pocket cell, completes no line
        // Score is tiny → difficulty well under the safety-net threshold → the refill must
        // still leave a playable move while the board has room (max solvability early).
        Assert.False(g.GameOver);
        Assert.True(g.AnyMovePossible());
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
