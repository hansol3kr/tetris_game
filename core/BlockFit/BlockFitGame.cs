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
/// Free-placement block puzzle (Block Blast style): a 10×10 grid and a tray of 3
/// fixed-orientation pieces you drag anywhere they fit — no gravity, no rotation,
/// no timer. Filling a full row OR column clears it; multi-line clears and
/// consecutive-clear streaks score more. Game over when none of the tray pieces
/// can be placed anywhere. Pure engine (no Godot) so it is unit-tested like core.
/// </summary>
public sealed class BlockFitGame
{
    public const int Size = 10;

    private readonly PieceType[] _grid = new PieceType[Size * Size];
    private readonly Random _rng;

    public BlockPiece?[] Tray { get; } = new BlockPiece?[3];
    public long Score { get; private set; }
    public int Streak { get; private set; }          // consecutive placements that cleared ≥1 line
    public bool GameOver { get; private set; }
    public int LastClearedRows { get; private set; } // for the clear animation / feedback
    public int LastClearedCols { get; private set; }

    /// <summary>0 at the start → 1 once the score saturates. Ramps the deal toward bigger,
    /// trickier pieces and retires the early-game "always a move" safety net as it climbs.</summary>
    public double Difficulty => DifficultyFor(Score);

    // Tunable difficulty curve. Below <see cref="SafetyNetBelow"/> the deal guarantees a
    // placeable piece while the board still has room (so early play never dead-ends); above
    // it, a crowded board can finally end the run.
    // Scaled for the 10×10 board: placement scores per-cell, so points accumulate
    // ~board-area faster than at 8×8 — stretch the ramp so the difficulty curve rises
    // at the same rate per unit of player progress rather than spiking early.
    private const long DifficultyScoreSpan = 2400;   // score at which difficulty saturates to 1
    private const double SafetyNetBelow = 0.5;       // guarantee a move while difficulty < this

    public static double DifficultyFor(long score) =>
        Math.Min(1.0, Math.Max(0.0, score / (double)DifficultyScoreSpan));

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
    /// Preview: the rows and columns that WOULD clear if <paramref name="p"/> were
    /// placed with its top-left origin at (row,col) — without mutating the board.
    /// Fills the caller-owned lists; both stay empty when the placement is invalid or
    /// completes no line. Drives the drag-time "this drop clears these lines" highlight
    /// so the player sees the payoff before releasing. Pure and allocation-free.
    /// </summary>
    public void LinesClearedBy(BlockPiece p, int row, int col, List<int> rows, List<int> cols)
    {
        rows.Clear();
        cols.Clear();
        if (p is null || !CanPlace(p, row, col)) return;

        Span<bool> covered = stackalloc bool[Size * Size];
        Span<bool> touchedRow = stackalloc bool[Size];
        Span<bool> touchedCol = stackalloc bool[Size];
        foreach (var (dr, dc) in p.Cells)
        {
            int r = row + dr, c = col + dc;
            covered[r * Size + c] = true;
            touchedRow[r] = true;
            touchedCol[c] = true;
        }

        // Only lines the piece touches can newly complete: the board carries no
        // pre-existing full line (those clear the instant they are completed).
        for (int r = 0; r < Size; r++)
        {
            if (!touchedRow[r]) continue;
            bool full = true;
            for (int c = 0; c < Size; c++)
                if (_grid[r * Size + c] == PieceType.Empty && !covered[r * Size + c]) { full = false; break; }
            if (full) rows.Add(r);
        }
        for (int c = 0; c < Size; c++)
        {
            if (!touchedCol[c]) continue;
            bool full = true;
            for (int r = 0; r < Size; r++)
                if (_grid[r * Size + c] == PieceType.Empty && !covered[r * Size + c]) { full = false; break; }
            if (full) cols.Add(c);
        }
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

    /// <summary>
    /// Fuse tray[<paramref name="srcIndex"/>] into tray[<paramref name="dstIndex"/>] with the
    /// source placed at (<paramref name="srcRowOffset"/>, <paramref name="srcColOffset"/>) cells
    /// relative to the destination's top-left — so the player controls EXACTLY where the two
    /// pieces join. The union (destination's colour) is normalised to top-left and replaces the
    /// destination slot; the source slot empties. Refuses (returns false, tray untouched) when
    /// the indices are invalid/equal, either slot is empty, the two pieces would overlap, or the
    /// result is too big to ever fit the board. Flips GameOver when the tray can no longer move.
    /// </summary>
    public bool TryMerge(int srcIndex, int dstIndex, int srcRowOffset, int srcColOffset)
    {
        if (!ResolveMerge(srcIndex, dstIndex, out var src, out var dst)) return false;
        var merged = BuildMerge(src!, dst!, srcRowOffset, srcColOffset);
        if (merged is null) return false;

        Tray[dstIndex] = new BlockPiece(merged, dst!.Color);
        Tray[srcIndex] = null;
        if (!AnyMovePossible()) GameOver = true;
        return true;
    }

    /// <summary>Back-compat / default: fuse by butting the source to the right of the
    /// destination's bounding box (the original horizontal concatenation).</summary>
    public bool TryMerge(int srcIndex, int dstIndex)
    {
        if (dstIndex < 0 || dstIndex >= Tray.Length || Tray[dstIndex] is null) return false;
        return TryMerge(srcIndex, dstIndex, 0, Tray[dstIndex]!.Width);
    }

    /// <summary>Would a merge at this offset be legal (no overlap, fits the board)? Pure —
    /// drives the drag-time merge preview so the player sees the shape before releasing.</summary>
    public bool CanMerge(int srcIndex, int dstIndex, int srcRowOffset, int srcColOffset)
    {
        if (!ResolveMerge(srcIndex, dstIndex, out var src, out var dst)) return false;
        return BuildMerge(src!, dst!, srcRowOffset, srcColOffset) is not null;
    }

    private bool ResolveMerge(int srcIndex, int dstIndex, out BlockPiece? src, out BlockPiece? dst)
    {
        src = null; dst = null;
        if (srcIndex == dstIndex) return false;
        if (srcIndex < 0 || srcIndex >= Tray.Length) return false;
        if (dstIndex < 0 || dstIndex >= Tray.Length) return false;
        src = Tray[srcIndex];
        dst = Tray[dstIndex];
        return src is not null && dst is not null;
    }

    /// <summary>Build the normalised (top-left based) union of dst + src-at-offset, or null if
    /// the cells overlap or the bounding box exceeds the board.</summary>
    private static List<(int r, int c)>? BuildMerge(BlockPiece src, BlockPiece dst, int rowOff, int colOff)
    {
        var used = new HashSet<(int, int)>();
        foreach (var (r, c) in dst.Cells) used.Add((r, c));
        foreach (var (r, c) in src.Cells)
            if (!used.Add((r + rowOff, c + colOff))) return null;   // overlap → illegal merge

        int minR = int.MaxValue, minC = int.MaxValue, maxR = int.MinValue, maxC = int.MinValue;
        foreach (var (r, c) in used)
        {
            if (r < minR) minR = r; if (c < minC) minC = c;
            if (r > maxR) maxR = r; if (c > maxC) maxC = c;
        }
        if (maxR - minR + 1 > Size || maxC - minC + 1 > Size) return null; // could never fit

        var norm = new List<(int r, int c)>(used.Count);
        foreach (var (r, c) in used) norm.Add((r - minR, c - minC));
        return norm;
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

    /// <summary>
    /// Suggest a placement to surface when the player stalls (the 5-second hint). Prefers a
    /// placement that clears at least one line; otherwise returns the first valid fit found
    /// (smallest pieces are scanned first, so the suggestion is the easiest to eyeball).
    /// Returns false only when nothing in the tray fits anywhere — i.e. the run is over.
    /// Pure: never mutates the board or tray.
    /// </summary>
    public bool FindHint(out int trayIndex, out int row, out int col)
    {
        trayIndex = -1; row = -1; col = -1;
        int fbIndex = -1, fbRow = -1, fbCol = -1;
        var rows = new List<int>();
        var cols = new List<int>();
        for (int i = 0; i < Tray.Length; i++)
        {
            var p = Tray[i];
            if (p is null) continue;
            for (int r = 0; r < Size; r++)
                for (int c = 0; c < Size; c++)
                {
                    if (!CanPlace(p, r, c)) continue;
                    if (fbIndex < 0) { fbIndex = i; fbRow = r; fbCol = c; }
                    LinesClearedBy(p, r, c, rows, cols);
                    if (rows.Count + cols.Count > 0)   // a clearing placement is the best hint
                    {
                        trayIndex = i; row = r; col = c;
                        return true;
                    }
                }
        }
        if (fbIndex >= 0) { trayIndex = fbIndex; row = fbRow; col = fbCol; return true; }
        return false;
    }

    /// <summary>
    /// Versus attack: scatter up to <paramref name="count"/> garbage blockers into random
    /// empty cells, crowding the board. Garbage arrives as loose blockers (it does not
    /// itself complete or clear a line) and flips GameOver if no tray piece can be placed
    /// afterward. Used only by the LIVE Block Fit versus — it intentionally consumes the
    /// game RNG, so it carries no replay/determinism contract.
    /// </summary>
    public void AddGarbage(int count)
    {
        if (count <= 0 || GameOver) return;
        var empties = new List<int>();
        for (int i = 0; i < _grid.Length; i++) if (_grid[i] == PieceType.Empty) empties.Add(i);

        int add = Math.Min(count, empties.Count);
        for (int k = 0; k < add; k++)
        {
            int j = _rng.Next(empties.Count);
            _grid[empties[j]] = PieceType.Garbage;
            empties[j] = empties[empties.Count - 1];   // swap-remove the chosen index
            empties.RemoveAt(empties.Count - 1);
        }

        bool hasPiece = false;
        foreach (var t in Tray) if (t is not null) { hasPiece = true; break; }
        if (hasPiece && !AnyMovePossible()) GameOver = true;
    }

    private void Deal()
    {
        double d = Difficulty;
        for (int i = 0; i < Tray.Length; i++) Tray[i] = WeightedRandomPiece(d);

        // Max solvability early: while the game is still forgiving, never hand a fully dead
        // set if the board still has an empty cell — swap one slot for a piece that fits.
        // Past the threshold this net is gone, so a crowded board can finally end the run.
        if (d < SafetyNetBelow && HasEmptyCell() && !AnyMovePossible())
        {
            var fit = FittingPiece();
            if (fit is not null) Tray[0] = fit;
        }

        if (!AnyMovePossible()) GameOver = true;
    }

    /// <summary>Pick a shape weighted by difficulty: easy favours small pieces (easy to
    /// place), hard favours big, awkward ones. Colour stays uniform.</summary>
    private BlockPiece WeightedRandomPiece(double d)
    {
        double total = 0;
        for (int i = 0; i < BlockShapes.All.Count; i++) total += ShapeWeight(i, d);
        double roll = _rng.NextDouble() * total;
        int idx = BlockShapes.All.Count - 1;
        for (int i = 0; i < BlockShapes.All.Count; i++)
        {
            roll -= ShapeWeight(i, d);
            if (roll <= 0) { idx = i; break; }
        }
        var color = BlockShapes.Colors[_rng.Next(BlockShapes.Colors.Length)];
        return new BlockPiece(BlockShapes.All[idx], color);
    }

    // size = cell count (1..9). Easy (d→0): weight ∝ (10 − size), so small pieces are common.
    // Hard (d→1): weight ∝ size·1.6, so big awkward pieces dominate. Floor keeps every shape
    // reachable at any difficulty.
    private static double ShapeWeight(int shapeIndex, double d)
    {
        int size = BlockShapes.All[shapeIndex].Count;
        double w = (1 - d) * (10 - size) + d * (size * 1.6);
        return w < 0.05 ? 0.05 : w;
    }

    private bool HasEmptyCell()
    {
        for (int i = 0; i < _grid.Length; i++) if (_grid[i] == PieceType.Empty) return true;
        return false;
    }

    /// <summary>The first (smallest) shape that fits somewhere on the current board, coloured
    /// randomly. A single always fits while any cell is empty, so this is null only on a full
    /// board. Powers the early-game safety net.</summary>
    private BlockPiece? FittingPiece()
    {
        for (int i = 0; i < BlockShapes.All.Count; i++)
        {
            var probe = new BlockPiece(BlockShapes.All[i], PieceType.I);
            for (int r = 0; r < Size; r++)
                for (int c = 0; c < Size; c++)
                    if (CanPlace(probe, r, c))
                        return new BlockPiece(BlockShapes.All[i], BlockShapes.Colors[_rng.Next(BlockShapes.Colors.Length)]);
        }
        return null;
    }
}
