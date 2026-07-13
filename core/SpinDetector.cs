namespace Blockfall.Core;

/// <summary>
/// Classifies T-spins using the standard 3-corner rule.
///
/// A lock counts as a T-spin only when the piece is a T, the last successful
/// action was a rotation, and at least 3 of the 4 corners of the T's 3x3 box
/// are occupied (blocks, walls, or floor). It is a FULL T-spin when both
/// "front" corners (on the side the T points toward) are occupied, OR when the
/// rotation used the special far wall-kick (last kick test); otherwise MINI.
/// </summary>
public static class SpinDetector
{
    // Corner offsets inside the 3x3 T box.
    private static readonly Vec2 TopLeft = new(0, 0);
    private static readonly Vec2 TopRight = new(0, 2);
    private static readonly Vec2 BottomLeft = new(2, 0);
    private static readonly Vec2 BottomRight = new(2, 2);

    /// <summary>Which two corners are "front" (point direction) for each T rotation state.</summary>
    private static (Vec2 a, Vec2 b) FrontCorners(RotationState state) => state switch
    {
        RotationState.Spawn => (TopLeft, TopRight),       // nub up
        RotationState.Right => (TopRight, BottomRight),   // nub right
        RotationState.Two   => (BottomLeft, BottomRight), // nub down
        RotationState.Left  => (TopLeft, BottomLeft),     // nub left
        _ => (TopLeft, TopRight),
    };

    /// <param name="lastActionWasRotation">Was the most recent successful move a rotation?</param>
    /// <param name="lastKickIndex">Index of the wall-kick test that succeeded (0 = no offset).</param>
    /// <param name="was180">Was the last rotation a 180 flip? Its kick table is unrelated to the TST far kick.</param>
    /// <param name="allSpin">If true, immobile spins of non-T pieces also score (S/Z/L/J/I/O).</param>
    public static SpinType Detect(Board board, Piece piece, bool lastActionWasRotation, int lastKickIndex,
        bool was180 = false, bool allSpin = false)
    {
        if (!lastActionWasRotation) return SpinType.None;

        if (piece.Type != PieceType.T)
        {
            // All-spin: any non-T piece rotated into a spot where it cannot move in
            // any of the four directions is a spin (the "immobility" rule).
            return allSpin && IsImmobile(board, piece) ? SpinType.Full : SpinType.None;
        }

        var (fa, fb) = FrontCorners(piece.State);

        bool tl = CornerFilled(board, piece.Origin, TopLeft);
        bool tr = CornerFilled(board, piece.Origin, TopRight);
        bool bl = CornerFilled(board, piece.Origin, BottomLeft);
        bool br = CornerFilled(board, piece.Origin, BottomRight);

        int total = (tl ? 1 : 0) + (tr ? 1 : 0) + (bl ? 1 : 0) + (br ? 1 : 0);
        if (total < 3) return SpinType.None;

        bool frontA = CornerFilled(board, piece.Origin, fa);
        bool frontB = CornerFilled(board, piece.Origin, fb);
        int frontCount = (frontA ? 1 : 0) + (frontB ? 1 : 0);

        // Both front corners filled => full. The standard SRS TST far kick (index 4 of
        // the 5-test CW/CCW tables) promotes a mini to full; 180 kick offsets never do.
        bool full = frontCount == 2 || (lastKickIndex == 4 && !was180);
        return full ? SpinType.Full : SpinType.Mini;
    }

    /// <summary>True if the piece cannot move up, down, left, or right from here.</summary>
    private static bool IsImmobile(Board board, Piece piece)
        => !board.CanPlace(piece.Moved(-1, 0))
        && !board.CanPlace(piece.Moved(1, 0))
        && !board.CanPlace(piece.Moved(0, -1))
        && !board.CanPlace(piece.Moved(0, 1));

    private static bool CornerFilled(Board board, Vec2 origin, Vec2 cornerOffset)
    {
        int r = origin.Row + cornerOffset.Row;
        int c = origin.Col + cornerOffset.Col;
        // Walls (left/right) and floor count as filled; open ceiling (above buffer) does not.
        if (c < 0 || c >= board.Width) return true;
        if (r >= board.TotalRows) return true;
        if (r < 0) return false;
        return board.IsOccupied(r, c);
    }
}
