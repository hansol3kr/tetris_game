using System.Collections.Generic;
using Blockfall.Core;
using Blockfall.Core.BlockFit;
using Xunit;

namespace Blockfall.Core.Tests;

public class BlockFitGameTests
{
    // Board dimensions come from the single core constant, so a size change
    // (8 → 10 → …) re-parameterises every fixture below instead of silently
    // breaking hardcoded 8/64 indices.
    private const int N = BlockFitGame.Size;
    private static PieceType[] EmptyGrid() => new PieceType[N * N];
    private static int Idx(int r, int c) => r * N + c;

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
        var g = new BlockFitGame(EmptyGrid(), new BlockPiece?[] { single, other, other });

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
        // Pre-fill row 0 except its last column; a single completes it → the row clears.
        var grid = EmptyGrid();
        for (int c = 0; c < N - 1; c++) grid[Idx(0, c)] = PieceType.O;
        var single = new BlockPiece(new[] { (0, 0) }, PieceType.T);
        var g = new BlockFitGame(grid, new BlockPiece?[] { single, null, null });

        Assert.True(g.TryPlace(0, 0, N - 1));
        // Entire row 0 must now be empty (cleared), and the clear is recorded.
        for (int c = 0; c < N; c++) Assert.Equal(PieceType.Empty, g.At(0, c));
        Assert.Equal(1, g.LastClearedRows);
        Assert.Equal(1, g.Streak);
        Assert.True(g.Score > 1); // placement point + line-clear bonus
    }

    [Fact]
    public void NoFittingPiece_IsGameOver()
    {
        // Completely full board + a piece that cannot fit anywhere.
        var grid = EmptyGrid();
        for (int i = 0; i < grid.Length; i++) grid[i] = PieceType.Z;
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
        var g = new BlockFitGame(EmptyGrid(), new BlockPiece?[] { big, big, big });
        double before = g.Difficulty;
        Assert.True(g.TryPlace(0, 0, 0));
        Assert.True(g.Difficulty > before);
    }

    [Fact]
    public void LowDifficulty_FreshDeal_KeepsAMoveWhileRoomRemains()
    {
        // Board full of Z except a 2×2 empty pocket at rows 3-4, cols 3-4.
        var grid = EmptyGrid();
        for (int i = 0; i < grid.Length; i++) grid[i] = PieceType.Z;
        grid[Idx(3, 3)] = PieceType.Empty; grid[Idx(3, 4)] = PieceType.Empty;
        grid[Idx(4, 3)] = PieceType.Empty; grid[Idx(4, 4)] = PieceType.Empty;
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
    public void LinesClearedBy_PreviewsRowAndColumnCompletions_WithoutMutating()
    {
        // Row 0 filled except its last column; column 0 filled except its last row.
        var grid = EmptyGrid();
        for (int c = 0; c < N - 1; c++) grid[Idx(0, c)] = PieceType.O;
        for (int r = 0; r < N - 1; r++) grid[Idx(r, 0)] = PieceType.O;
        var single = new BlockPiece(new[] { (0, 0) }, PieceType.T);
        var g = new BlockFitGame(grid, new BlockPiece?[] { single, null, null });

        var rows = new List<int>();
        var cols = new List<int>();

        g.LinesClearedBy(single, 0, N - 1, rows, cols); // completes row 0 only
        Assert.Equal(new[] { 0 }, rows);
        Assert.Empty(cols);

        g.LinesClearedBy(single, N - 1, 0, rows, cols); // completes column 0 only
        Assert.Empty(rows);
        Assert.Equal(new[] { 0 }, cols);

        // Preview is pure — the board is untouched.
        Assert.Equal(PieceType.Empty, g.At(0, N - 1));
        Assert.Equal(PieceType.O, g.At(0, 0));
    }

    [Fact]
    public void LinesClearedBy_InvalidOrNoCompletion_YieldsEmpty()
    {
        var single = new BlockPiece(new[] { (0, 0) }, PieceType.T);
        var rows = new List<int>();
        var cols = new List<int>();

        // Valid placement on an empty board completes nothing.
        var g = new BlockFitGame(EmptyGrid(), new BlockPiece?[] { single, null, null });
        g.LinesClearedBy(single, 3, 3, rows, cols);
        Assert.Empty(rows);
        Assert.Empty(cols);

        // Invalid placement (cell occupied) yields nothing.
        var full = EmptyGrid();
        for (int i = 0; i < full.Length; i++) full[i] = PieceType.Z;
        var g2 = new BlockFitGame(full, new BlockPiece?[] { single, null, null });
        g2.LinesClearedBy(single, 0, 0, rows, cols);
        Assert.Empty(rows);
        Assert.Empty(cols);
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
