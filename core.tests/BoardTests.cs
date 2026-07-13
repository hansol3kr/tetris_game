using Blockfall.Core;
using Xunit;

namespace Blockfall.Core.Tests;

public class BoardTests
{
    [Fact]
    public void NewBoard_IsEmpty()
    {
        var b = new Board();
        Assert.True(b.IsEmpty());
        Assert.Equal(10, b.Width);
        Assert.Equal(40, b.TotalRows);
        Assert.Equal(20, b.VisibleTop);
    }

    [Fact]
    public void ClearFullRows_RemovesSingleFullRow_AndCollapses()
    {
        var b = new Board();
        int row = b.TotalRows - 1;
        for (int c = 0; c < b.Width; c++) b[row, c] = PieceType.J;
        // Put a marker block above so we can confirm it falls.
        b[row - 1, 0] = PieceType.T;

        var cleared = b.ClearFullRows();

        Assert.Single(cleared);
        Assert.Equal(row, cleared[0]);
        // The marker should have fallen from (row-1,0) to (row,0).
        Assert.Equal(PieceType.T, b[row, 0]);
        Assert.Equal(PieceType.Empty, b[row - 1, 0]);
    }

    [Fact]
    public void ClearFullRows_ClearsFour_ForTetris()
    {
        var b = new Board();
        for (int r = b.TotalRows - 4; r < b.TotalRows; r++)
            for (int c = 0; c < b.Width; c++)
                b[r, c] = PieceType.I;

        var cleared = b.ClearFullRows();
        Assert.Equal(4, cleared.Count);
        Assert.True(b.IsEmpty());
    }

    [Fact]
    public void ClearFullRows_IgnoresRowWithGap()
    {
        var b = new Board();
        int row = b.TotalRows - 1;
        for (int c = 0; c < b.Width - 1; c++) b[row, c] = PieceType.O; // leave last cell empty
        var cleared = b.ClearFullRows();
        Assert.Empty(cleared);
    }

    [Fact]
    public void CanPlace_RejectsOutOfBounds_AndOverlap()
    {
        var b = new Board();
        var piece = new Piece(PieceType.O, RotationState.Spawn, new Vec2(b.TotalRows - 2, 0));
        Assert.True(b.CanPlace(piece));

        // Push off the left wall.
        Assert.False(b.CanPlace(piece.Moved(0, -1)));

        // Overlap an existing block.
        b[b.TotalRows - 1, 0] = PieceType.Z;
        Assert.False(b.CanPlace(piece));
    }

    [Fact]
    public void HardDropTarget_LandsOnFloor()
    {
        var b = new Board();
        var piece = new Piece(PieceType.O, RotationState.Spawn, new Vec2(0, 4));
        var (landed, distance) = b.HardDropTarget(piece);
        // O occupies rows origin..origin+1; bottom cell should rest on last row.
        Assert.Equal(b.TotalRows - 2, landed.Origin.Row);
        Assert.Equal(b.TotalRows - 2, distance);
    }
}
