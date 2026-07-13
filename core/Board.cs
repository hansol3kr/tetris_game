namespace Blockfall.Core;

/// <summary>
/// The playfield grid. 10 columns wide. The top <see cref="BufferRows"/> rows are
/// a hidden spawn buffer; the bottom <see cref="VisibleRows"/> rows are shown.
/// Row 0 is the very top; rows grow downward.
/// </summary>
public sealed class Board
{
    public const int DefaultWidth = 10;
    public const int DefaultVisibleRows = 20;
    public const int DefaultBufferRows = 20;

    public int Width { get; }
    public int VisibleRows { get; }
    public int BufferRows { get; }
    public int TotalRows => VisibleRows + BufferRows;

    /// <summary>First visible row index (rows below this are on-screen).</summary>
    public int VisibleTop => BufferRows;

    private readonly PieceType[] _cells; // row-major, length = TotalRows * Width

    public Board(int width = DefaultWidth, int visibleRows = DefaultVisibleRows, int bufferRows = DefaultBufferRows)
    {
        Width = width;
        VisibleRows = visibleRows;
        BufferRows = bufferRows;
        _cells = new PieceType[TotalRows * width];
    }

    public PieceType this[int row, int col]
    {
        get => _cells[row * Width + col];
        set => _cells[row * Width + col] = value;
    }

    public bool InBounds(int row, int col) => row >= 0 && row < TotalRows && col >= 0 && col < Width;

    public bool IsOccupied(int row, int col) => this[row, col] != PieceType.Empty;

    /// <summary>True if this cell is a valid, empty spot a piece may occupy.</summary>
    public bool IsFree(int row, int col)
        => col >= 0 && col < Width && row < TotalRows && (row < 0 || this[row, col] == PieceType.Empty);

    /// <summary>Can the piece exist here without overlap or going out of bounds?</summary>
    public bool CanPlace(Piece piece)
    {
        Span<Vec2> cells = stackalloc Vec2[4];
        piece.CellsInto(cells);
        foreach (var c in cells)
        {
            if (c.Col < 0 || c.Col >= Width || c.Row >= TotalRows) return false;
            if (c.Row >= 0 && this[c.Row, c.Col] != PieceType.Empty) return false;
        }
        return true;
    }

    /// <summary>Stamp the piece's cells permanently onto the board.</summary>
    public void Lock(Piece piece)
    {
        Span<Vec2> cells = stackalloc Vec2[4];
        piece.CellsInto(cells);
        foreach (var c in cells)
            if (c.Row >= 0 && c.Row < TotalRows && c.Col >= 0 && c.Col < Width)
                this[c.Row, c.Col] = piece.Type;
    }

    /// <summary>Detects and removes full rows, collapsing everything above. Returns cleared row indices (top-to-bottom).</summary>
    public IReadOnlyList<int> ClearFullRows()
    {
        var cleared = new List<int>();
        for (int row = 0; row < TotalRows; row++)
        {
            bool full = true;
            for (int col = 0; col < Width; col++)
            {
                if (this[row, col] == PieceType.Empty) { full = false; break; }
            }
            if (full) cleared.Add(row);
        }

        if (cleared.Count == 0) return cleared;

        // Rebuild: keep non-cleared rows, push them down so cleared gaps close.
        int writeRow = TotalRows - 1;
        for (int readRow = TotalRows - 1; readRow >= 0; readRow--)
        {
            if (cleared.Contains(readRow)) continue;
            if (writeRow != readRow)
                for (int col = 0; col < Width; col++)
                    this[writeRow, col] = this[readRow, col];
            writeRow--;
        }
        // Zero out the newly opened rows at the top.
        for (int row = writeRow; row >= 0; row--)
            for (int col = 0; col < Width; col++)
                this[row, col] = PieceType.Empty;

        return cleared;
    }

    /// <summary>True if the whole playfield is empty (used for Perfect Clear / All-Clear detection).</summary>
    public bool IsEmpty()
    {
        for (int i = 0; i < _cells.Length; i++)
            if (_cells[i] != PieceType.Empty) return false;
        return true;
    }

    /// <summary>Drops the piece straight down as far as it will go; returns the resting piece and the fall distance.</summary>
    public (Piece landed, int distance) HardDropTarget(Piece piece)
    {
        int distance = 0;
        var current = piece;
        while (true)
        {
            var next = current.Moved(1, 0);
            if (!CanPlace(next)) break;
            current = next;
            distance++;
        }
        return (current, distance);
    }

    /// <summary>Is the piece resting on the stack / floor (cannot move down)?</summary>
    public bool IsLanded(Piece piece) => !CanPlace(piece.Moved(1, 0));

    /// <summary>
    /// Raises the whole stack by <paramref name="count"/> garbage rows inserted at
    /// the bottom. Each new row is full except for one empty hole at
    /// <paramref name="holeColumn"/>. Returns true if any existing block was pushed
    /// off the top of the field (a top-out condition for the caller to act on).
    /// </summary>
    public bool InsertGarbageLines(int count, int holeColumn)
    {
        if (count <= 0) return false;
        if (count > TotalRows) count = TotalRows;

        // Any non-empty cell in the top `count` rows will be shifted off the field.
        bool overflowed = false;
        for (int row = 0; row < count; row++)
            for (int col = 0; col < Width; col++)
                if (this[row, col] != PieceType.Empty) { overflowed = true; break; }

        // Shift every surviving row up by `count`.
        for (int row = 0; row < TotalRows - count; row++)
            for (int col = 0; col < Width; col++)
                this[row, col] = this[row + count, col];

        // Fill the freed bottom rows with garbage + a single hole.
        for (int row = TotalRows - count; row < TotalRows; row++)
            for (int col = 0; col < Width; col++)
                this[row, col] = col == holeColumn ? PieceType.Empty : PieceType.Garbage;

        return overflowed;
    }

    /// <summary>Number of rows that contain at least one garbage cell (for Dig goals).</summary>
    public int GarbageRowCount()
    {
        int n = 0;
        for (int row = 0; row < TotalRows; row++)
            for (int col = 0; col < Width; col++)
                if (this[row, col] == PieceType.Garbage) { n++; break; }
        return n;
    }

    public void Clear()
    {
        Array.Clear(_cells, 0, _cells.Length);
    }

    /// <summary>Copy of the raw grid for rendering/serialization (read-only usage).</summary>
    public PieceType[] Snapshot() => (PieceType[])_cells.Clone();

    /// <summary>Deep copy of the board — used by the AI to evaluate hypothetical placements.</summary>
    public Board Clone()
    {
        var b = new Board(Width, VisibleRows, BufferRows);
        Array.Copy(_cells, b._cells, _cells.Length);
        return b;
    }
}
