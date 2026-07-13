namespace Blockfall.Core;

/// <summary>
/// The seven standard tetromino shapes plus board-cell markers.
/// We deliberately use our own naming and never ship the trademarked
/// "Tetrimino" term or the official color mapping in shipped builds.
/// </summary>
public enum PieceType : byte
{
    Empty = 0, // no block in this cell
    I = 1,
    O = 2,
    T = 3,
    S = 4,
    Z = 5,
    J = 6,
    L = 7,
    Garbage = 8, // filled cell that is not part of any active piece (used by future versus mode)
}

/// <summary>
/// Rotation states, laid out so that (state + 1) mod 4 is a clockwise turn
/// and (state + 3) mod 4 is a counter-clockwise turn. Matches SRS ordering.
/// </summary>
public enum RotationState : byte
{
    Spawn = 0, // 0
    Right = 1, // R  (clockwise from spawn)
    Two   = 2, // 2  (180 from spawn)
    Left  = 3, // L  (counter-clockwise from spawn)
}

/// <summary>Integer 2D coordinate. Row increases downward, Column increases rightward.</summary>
public readonly struct Vec2 : IEquatable<Vec2>
{
    public readonly int Row;
    public readonly int Col;

    public Vec2(int row, int col)
    {
        Row = row;
        Col = col;
    }

    public Vec2 Offset(int dRow, int dCol) => new(Row + dRow, Col + dCol);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.Row + b.Row, a.Col + b.Col);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.Row - b.Row, a.Col - b.Col);

    public bool Equals(Vec2 other) => Row == other.Row && Col == other.Col;
    public override bool Equals(object? obj) => obj is Vec2 v && Equals(v);
    public override int GetHashCode() => (Row * 397) ^ Col;
    public override string ToString() => $"(r{Row}, c{Col})";
}

/// <summary>
/// A wall-kick offset in the canonical SRS coordinate system where X is
/// rightward and Y is UP. We store the reference data verbatim (so it can be
/// checked against public SRS tables) and convert to board space at apply time:
///     testRow = originRow - Y   (because board rows grow downward)
///     testCol = originCol + X
/// </summary>
public readonly struct Kick
{
    public readonly int X; // right = +
    public readonly int Y; // up = +

    public Kick(int x, int y)
    {
        X = x;
        Y = y;
    }
}
