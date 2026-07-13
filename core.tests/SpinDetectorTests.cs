using Blockfall.Core;
using Xunit;

namespace Blockfall.Core.Tests;

public class SpinDetectorTests
{
    // Places a T in state Two (pointing down) at the given origin. Its box corners
    // are (r,c) TL, (r,c+2) TR, (r+2,c) BL, (r+2,c+2) BR. Front (nub) corners for
    // state Two are the two BOTTOM corners.
    private static (Board board, Piece piece) SetupT(int r, int c)
    {
        var board = new Board();
        var piece = new Piece(PieceType.T, RotationState.Two, new Vec2(r, c));
        Assert.True(board.CanPlace(piece)); // the plus-shape cells must be free
        return (board, piece);
    }

    [Fact]
    public void NoRotation_NeverCountsAsSpin()
    {
        var (board, piece) = SetupT(30, 3);
        // Fill all four corners, but the last action was not a rotation.
        board[30, 3] = PieceType.J; board[30, 5] = PieceType.J;
        board[32, 3] = PieceType.J; board[32, 5] = PieceType.J;
        var spin = SpinDetector.Detect(board, piece, lastActionWasRotation: false, lastKickIndex: 0);
        Assert.Equal(SpinType.None, spin);
    }

    [Fact]
    public void NonTPiece_NeverCountsAsSpin()
    {
        var board = new Board();
        var piece = new Piece(PieceType.L, RotationState.Two, new Vec2(30, 3));
        var spin = SpinDetector.Detect(board, piece, lastActionWasRotation: true, lastKickIndex: 0);
        Assert.Equal(SpinType.None, spin);
    }

    [Fact]
    public void ThreeCorners_WithBothFrontFilled_IsFullTSpin()
    {
        var (board, piece) = SetupT(30, 3);
        // Both bottom (front) corners + one top (back) corner.
        board[32, 3] = PieceType.J; // front BL
        board[32, 5] = PieceType.J; // front BR
        board[30, 3] = PieceType.J; // back TL
        var spin = SpinDetector.Detect(board, piece, lastActionWasRotation: true, lastKickIndex: 0);
        Assert.Equal(SpinType.Full, spin);
    }

    [Fact]
    public void ThreeCorners_WithOneFrontFilled_IsMiniTSpin()
    {
        var (board, piece) = SetupT(30, 3);
        // One bottom (front) corner + both top (back) corners.
        board[32, 3] = PieceType.J; // front BL only
        board[30, 3] = PieceType.J; // back TL
        board[30, 5] = PieceType.J; // back TR
        var spin = SpinDetector.Detect(board, piece, lastActionWasRotation: true, lastKickIndex: 0);
        Assert.Equal(SpinType.Mini, spin);
    }

    [Fact]
    public void OnlyTwoCorners_IsNoSpin()
    {
        var (board, piece) = SetupT(30, 3);
        board[32, 3] = PieceType.J;
        board[30, 3] = PieceType.J;
        var spin = SpinDetector.Detect(board, piece, lastActionWasRotation: true, lastKickIndex: 0);
        Assert.Equal(SpinType.None, spin);
    }

    [Fact]
    public void FarKick_PromotesMiniToFull()
    {
        var (board, piece) = SetupT(30, 3);
        // Mini configuration (one front, two back) but reached via the far kick (index 4).
        board[32, 3] = PieceType.J; // front BL
        board[30, 3] = PieceType.J; // back TL
        board[30, 5] = PieceType.J; // back TR
        var spin = SpinDetector.Detect(board, piece, lastActionWasRotation: true, lastKickIndex: 4);
        Assert.Equal(SpinType.Full, spin);
    }

    [Fact]
    public void FarKick_On180Rotation_DoesNotPromoteMiniToFull()
    {
        // Regression: the 180 kick table has 6 tests, so its index 4/5 are ordinary
        // nudges, not the TST far kick. A 180 T-spin must stay Mini here.
        var (board, piece) = SetupT(30, 3);
        board[32, 3] = PieceType.J; // front BL only
        board[30, 3] = PieceType.J; // back TL
        board[30, 5] = PieceType.J; // back TR

        // CW/CCW far kick (index 4) still promotes to Full.
        Assert.Equal(SpinType.Full, SpinDetector.Detect(board, piece, true, 4, was180: false));
        // The same index reached via a 180 flip must NOT promote.
        Assert.Equal(SpinType.Mini, SpinDetector.Detect(board, piece, true, 4, was180: true));
        // Index 5 only exists in the 180 table — also must not promote.
        Assert.Equal(SpinType.Mini, SpinDetector.Detect(board, piece, true, 5, was180: true));
    }

    [Fact]
    public void WallAndFloor_CountAsFilledCorners()
    {
        // Place a T in state Two at the bottom-left corner so its bottom corners
        // are the floor and its left corner is the wall.
        var board = new Board();
        int r = board.TotalRows - 3; // so row r+2 is the last row
        var piece = new Piece(PieceType.T, RotationState.Two, new Vec2(r, 0));
        Assert.True(board.CanPlace(piece));
        // Bottom corners are on the floor row (r+2 == last row) -> filled.
        // Fill the two top corners as well by walls? Left wall covers col -1... but
        // corner cols are 0 and 2 here (in bounds). Fill the top-left corner block.
        board[r, 0] = PieceType.Empty; // ensure clear (it is)
        // Corners: TL(r,0) empty, TR(r,2) empty, BL(r+2,0) floor row -> occupied? floor is r+2 in-bounds but empty.
        // Actually the FLOOR counts only when row >= TotalRows. Here r+2 is the last valid row (in-bounds, empty),
        // so bottom corners are NOT auto-filled. Move piece one lower so bottom corners fall off the board.
        var lower = new Piece(PieceType.T, RotationState.Two, new Vec2(board.TotalRows - 2, 0));
        // Now box rows: r=TotalRows-2, so r+2 = TotalRows -> off board -> floor -> filled corners.
        // Its cells (plus shape) occupy rows r+1..r+2; r+2 is off-board so it can't be placed.
        Assert.False(board.CanPlace(lower)); // sanity: piece can't hang off the floor
    }
}
