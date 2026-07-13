namespace Blockfall.Core;

/// <summary>
/// A live, falling piece: its type, rotation state, and bounding-box origin
/// (top-left cell of the box in board coordinates). Immutable-by-convention:
/// movement/rotation return NEW pieces so the caller can validate against the
/// board before committing.
/// </summary>
public sealed class Piece
{
    public PieceType Type { get; }
    public RotationState State { get; }
    public Vec2 Origin { get; } // box top-left in board space

    public Piece(PieceType type, RotationState state, Vec2 origin)
    {
        Type = type;
        State = state;
        Origin = origin;
    }

    /// <summary>Absolute board cells this piece currently occupies.</summary>
    public IEnumerable<Vec2> Cells()
    {
        foreach (var c in Tetromino.Cells(Type, State))
            yield return new Vec2(Origin.Row + c.Row, Origin.Col + c.Col);
    }

    /// <summary>Fills a caller-provided span (length 4) to avoid per-frame allocations.</summary>
    public void CellsInto(Span<Vec2> dst)
    {
        var cells = Tetromino.Cells(Type, State);
        for (int i = 0; i < 4; i++)
            dst[i] = new Vec2(Origin.Row + cells[i].Row, Origin.Col + cells[i].Col);
    }

    public Piece Moved(int dRow, int dCol) => new(Type, State, Origin.Offset(dRow, dCol));

    public Piece WithState(RotationState state, Vec2 origin) => new(Type, state, origin);

    public static RotationState Cw(RotationState s) => (RotationState)(((int)s + 1) & 3);
    public static RotationState Ccw(RotationState s) => (RotationState)(((int)s + 3) & 3);
    public static RotationState Flip(RotationState s) => (RotationState)(((int)s + 2) & 3);
}
