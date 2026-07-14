using System;
using System.Collections.Generic;

namespace Blockfall.Core.BlockFit;

/// <summary>One fixed-orientation tray piece: its cell offsets (top-left based) and colour.</summary>
public sealed class BlockPiece
{
    public IReadOnlyList<(int r, int c)> Cells { get; }
    public PieceType Color { get; }
    public int Width { get; }
    public int Height { get; }

    public BlockPiece(IReadOnlyList<(int r, int c)> cells, PieceType color)
    {
        Cells = cells;
        Color = color;
        int w = 0, h = 0;
        foreach (var (r, c) in cells) { if (c + 1 > w) w = c + 1; if (r + 1 > h) h = r + 1; }
        Width = w;
        Height = h;
    }
}

/// <summary>The fixed-orientation polyomino set the tray draws from (Block Blast-style variety).</summary>
public static class BlockShapes
{
    public static readonly PieceType[] Colors =
        { PieceType.I, PieceType.O, PieceType.T, PieceType.S, PieceType.Z, PieceType.J, PieceType.L };

    public static readonly IReadOnlyList<IReadOnlyList<(int r, int c)>> All = new List<IReadOnlyList<(int, int)>>
    {
        new[] { (0, 0) },                                                     // single
        new[] { (0, 0), (0, 1) },                                            // 1x2
        new[] { (0, 0), (1, 0) },                                            // 2x1
        new[] { (0, 0), (0, 1), (0, 2) },                                    // 1x3
        new[] { (0, 0), (1, 0), (2, 0) },                                    // 3x1
        new[] { (0, 0), (0, 1), (0, 2), (0, 3) },                            // 1x4
        new[] { (0, 0), (1, 0), (2, 0), (3, 0) },                            // 4x1
        new[] { (0, 0), (0, 1), (0, 2), (0, 3), (0, 4) },                    // 1x5
        new[] { (0, 0), (1, 0), (2, 0), (3, 0), (4, 0) },                    // 5x1
        new[] { (0, 0), (0, 1), (1, 0), (1, 1) },                            // 2x2
        new[] { (0, 0), (0, 1), (0, 2), (1, 0), (1, 1), (1, 2), (2, 0), (2, 1), (2, 2) }, // 3x3
        new[] { (0, 0), (1, 0), (1, 1) },                                    // corner ┗
        new[] { (0, 1), (1, 0), (1, 1) },                                    // corner ┛
        new[] { (0, 0), (0, 1), (1, 1) },                                    // corner ┓
        new[] { (0, 0), (0, 1), (1, 0) },                                    // corner ┏
        new[] { (0, 0), (1, 0), (2, 0), (2, 1), (2, 2) },                    // big L
        new[] { (0, 2), (1, 2), (2, 0), (2, 1), (2, 2) },                    // big J
        new[] { (0, 0), (1, 0), (1, 1), (2, 1) },                            // S
        new[] { (0, 1), (1, 0), (1, 1), (2, 0) },                            // Z
        new[] { (0, 0), (0, 1), (0, 2), (1, 1) },                            // T
    };
}

/// <summary>
/// Free-placement block puzzle (Block Blast style): an 8×8 grid and a tray of 3
/// fixed-orientation pieces you drag anywhere they fit — no gravity, no rotation,
/// no timer. Filling a full row OR column clears it; multi-line clears and
/// consecutive-clear streaks score more. Game over when none of the tray pieces
/// can be placed anywhere. Pure engine (no Godot) so it is unit-tested like core.
/// </summary>
public sealed class BlockFitGame
{
    public const int Size = 8;

    private readonly PieceType[] _grid = new PieceType[Size * Size];
    private readonly Random _rng;

    public BlockPiece?[] Tray { get; } = new BlockPiece?[3];
    public long Score { get; private set; }
    public int Streak { get; private set; }          // consecutive placements that cleared ≥1 line
    public bool GameOver { get; private set; }
    public int LastClearedRows { get; private set; } // for the clear animation / feedback
    public int LastClearedCols { get; private set; }

    public BlockFitGame(int seed = 0)
    {
        _rng = seed == 0 ? new Random() : new Random(seed);
        Deal();
    }

    /// <summary>Test seam: start from an explicit board + tray, skipping the random deal.</summary>
    public BlockFitGame(PieceType[] grid, IReadOnlyList<BlockPiece?> tray)
    {
        _rng = new Random(1);
        if (grid.Length == _grid.Length) Array.Copy(grid, _grid, grid.Length);
        for (int i = 0; i < Tray.Length && i < tray.Count; i++) Tray[i] = tray[i];
    }

    public PieceType At(int r, int c) => _grid[r * Size + c];
    private void SetCell(int r, int c, PieceType t) => _grid[r * Size + c] = t;

    /// <summary>Does <paramref name="p"/> fit with its top-left origin at (row,col)?</summary>
    public bool CanPlace(BlockPiece p, int row, int col)
    {
        foreach (var (dr, dc) in p.Cells)
        {
            int r = row + dr, c = col + dc;
            if (r < 0 || r >= Size || c < 0 || c >= Size) return false;
            if (_grid[r * Size + c] != PieceType.Empty) return false;
        }
        return true;
    }

    /// <summary>
    /// Place tray[<paramref name="trayIndex"/>] at (row,col). Returns false if the
    /// slot is empty or it doesn't fit. On success it commits the cells, clears any
    /// full lines, scores, refills the tray when all three are placed, and flips
    /// GameOver when no remaining piece can be placed.
    /// </summary>
    public bool TryPlace(int trayIndex, int row, int col)
    {
        if (trayIndex < 0 || trayIndex >= Tray.Length) return false;
        var p = Tray[trayIndex];
        if (p is null || !CanPlace(p, row, col)) return false;

        foreach (var (dr, dc) in p.Cells)
            SetCell(row + dr, col + dc, p.Color);
        Tray[trayIndex] = null;
        Score += p.Cells.Count;          // 1 point per cell placed

        ClearFullLines();

        bool empty = true;
        foreach (var t in Tray) if (t is not null) { empty = false; break; }
        if (empty) Deal();               // fresh set of 3 (Deal flags GameOver if none fit)
        else if (!AnyMovePossible()) GameOver = true;
        return true;
    }

    private void ClearFullLines()
    {
        var fullRows = new List<int>();
        var fullCols = new List<int>();
        for (int r = 0; r < Size; r++)
        {
            bool full = true;
            for (int c = 0; c < Size; c++) if (_grid[r * Size + c] == PieceType.Empty) { full = false; break; }
            if (full) fullRows.Add(r);
        }
        for (int c = 0; c < Size; c++)
        {
            bool full = true;
            for (int r = 0; r < Size; r++) if (_grid[r * Size + c] == PieceType.Empty) { full = false; break; }
            if (full) fullCols.Add(c);
        }

        LastClearedRows = fullRows.Count;
        LastClearedCols = fullCols.Count;
        int lines = fullRows.Count + fullCols.Count;

        foreach (int r in fullRows) for (int c = 0; c < Size; c++) SetCell(r, c, PieceType.Empty);
        foreach (int c in fullCols) for (int r = 0; r < Size; r++) SetCell(r, c, PieceType.Empty);

        if (lines > 0)
        {
            Streak++;
            // Reward multi-line combos superlinearly, plus a streak bonus (Block Blast feel).
            Score += 10L * lines * (lines + 1) + 5L * (Streak - 1);
        }
        else
        {
            Streak = 0;
        }
    }

    public bool AnyMovePossible()
    {
        foreach (var p in Tray)
        {
            if (p is null) continue;
            for (int r = 0; r < Size; r++)
                for (int c = 0; c < Size; c++)
                    if (CanPlace(p, r, c)) return true;
        }
        return false;
    }

    private void Deal()
    {
        for (int i = 0; i < Tray.Length; i++) Tray[i] = RandomPiece();
        if (!AnyMovePossible()) GameOver = true;
    }

    private BlockPiece RandomPiece()
    {
        var shape = BlockShapes.All[_rng.Next(BlockShapes.All.Count)];
        var color = BlockShapes.Colors[_rng.Next(BlockShapes.Colors.Length)];
        return new BlockPiece(shape, color);
    }
}
