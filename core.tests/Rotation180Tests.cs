using Blockfall.Core;
using Xunit;

namespace Blockfall.Core.Tests;

/// <summary>
/// 180° rotation kick behaviour.
///
/// Original bug (shipped v1.0–v1.4): the R&lt;-&gt;L 180 table tested x = ±2 and contained
/// no diagonal candidate. On a ragged stack that made roughly 0.7% of 180 spins succeed
/// TWO columns away from where the player aimed — once every ~143 spins the piece simply
/// teleported sideways. Note the legacy table was more PERMISSIVE overall (~99.0% success
/// vs the community standard's ~98.5%); the defect was never the failures, it was that
/// success was unpredictable. A stacker can plan around a 180 that refuses; they cannot
/// plan around one that lands somewhere else.
///
/// V2 uses the community-standard table: at most one column of lateral displacement, with
/// diagonal tests, in every entry.
/// </summary>
public class Rotation180Tests
{
    /// <summary>Mirrors <c>Game.TryRotate</c>: first kick that fits wins.</summary>
    private static bool TryRotate(Board board, Piece piece, RotationState target,
        RulesVersion rules, out Piece result)
    {
        foreach (var k in Tetromino.KickSequence(piece.Type, piece.State, target, rules))
        {
            // SRS (x-right / y-up) -> board space: row -= y, col += x.
            var candidate = new Piece(piece.Type, target, piece.Origin.Offset(-k.Y, k.X));
            if (board.CanPlace(candidate)) { result = candidate; return true; }
        }
        result = piece;
        return false;
    }

    /// <summary>
    /// Reproduces the playtest harness: build a random ragged stack, drop a random piece
    /// onto it, attempt a 180, and count how often the piece ends up two or more columns
    /// from where it started.
    /// </summary>
    private static (int attempts, int succeeded, int displacedTwoPlus) Survey(RulesVersion rules, int trials)
    {
        var rng = new XorShiftRandom(0xB10CFA11UL);
        var types = new[] { PieceType.I, PieceType.O, PieceType.T, PieceType.S, PieceType.Z, PieceType.J, PieceType.L };
        int attempts = 0, succeeded = 0, displaced = 0;

        for (int trial = 0; trial < trials; trial++)
        {
            var board = new Board();
            // Ragged stack: per-column height 0..8 with scattered holes.
            for (int c = 0; c < board.Width; c++)
            {
                int h = rng.Next(9);
                for (int i = 0; i < h; i++)
                {
                    int row = board.TotalRows - 1 - i;
                    if (rng.Next(100) < 22) continue; // hole
                    board[row, c] = PieceType.Garbage;
                }
            }

            var type = types[rng.Next(types.Length)];
            var state = (RotationState)rng.Next(4);
            int col = rng.Next(board.Width) - 1;
            var spawn = new Piece(type, state, new Vec2(0, col));
            if (!board.CanPlace(spawn)) continue;

            var landed = board.HardDropTarget(spawn).landed;
            var target = Piece.Flip(landed.State);
            attempts++;
            if (TryRotate(board, landed, target, rules, out var rotated))
            {
                succeeded++;
                if (System.Math.Abs(rotated.Origin.Col - landed.Origin.Col) >= 2) displaced++;
            }
        }
        return (attempts, succeeded, displaced);
    }

    [Fact]
    public void Rotate180_NeverDisplacesTwoColumns_UnderV2()
    {
        var (attempts, succeeded, displaced) = Survey(RulesVersion.V2, trials: 60_000);
        Assert.True(attempts > 40_000, $"survey too small: {attempts} attempts");
        Assert.Equal(0, displaced);
        // The table must stay usable, not just predictable.
        Assert.True(succeeded / (double)attempts > 0.95,
            $"180 success rate collapsed to {succeeded / (double)attempts:P2}");
    }

    [Fact]
    public void Rotate180_DisplacedTwoColumns_UnderV1()
    {
        // Pins the original defect on the legacy branch so old replays keep resolving
        // their spins the way they actually resolved when the run was played.
        var (attempts, _, displaced) = Survey(RulesVersion.V1, trials: 60_000);
        Assert.True(displaced > 0,
            $"v1 must keep its two-column kicks for replay fidelity ({attempts} attempts, {displaced} displaced)");
    }

    [Fact]
    public void Kick180Table_CapsLateralDisplacementAtOneColumn_UnderV2()
    {
        // A direct property of the data, independent of any board survey: no v2 180 entry
        // may move the piece more than one column sideways.
        foreach (var type in new[] { PieceType.I, PieceType.O, PieceType.T, PieceType.S, PieceType.Z, PieceType.J, PieceType.L })
            for (int from = 0; from < 4; from++)
            {
                var f = (RotationState)from;
                var to = Piece.Flip(f);
                foreach (var k in Tetromino.KickSequence(type, f, to, RulesVersion.V2))
                    Assert.True(System.Math.Abs(k.X) <= 1,
                        $"180 kick {f}->{to} for {type} displaces {k.X} columns");
            }
    }

    [Fact]
    public void Kick180Table_IsDirectionallyMirrored_UnderV2()
    {
        // 0->2 kicks up / 2->0 kicks down, R->L kicks right / L->R kicks left. Without the
        // mirror the two ways into the same pocket resolve to different cells, which is the
        // same unpredictability the two-column kick caused.
        var up = Tetromino.KickSequence(PieceType.T, RotationState.Spawn, RotationState.Two, RulesVersion.V2);
        var down = Tetromino.KickSequence(PieceType.T, RotationState.Two, RotationState.Spawn, RulesVersion.V2);
        Assert.Equal(up.Length, down.Length);
        for (int i = 0; i < up.Length; i++)
        {
            Assert.Equal(up[i].X, -down[i].X);
            Assert.Equal(up[i].Y, -down[i].Y);
        }

        var right = Tetromino.KickSequence(PieceType.T, RotationState.Right, RotationState.Left, RulesVersion.V2);
        var left = Tetromino.KickSequence(PieceType.T, RotationState.Left, RotationState.Right, RulesVersion.V2);
        Assert.Equal(right.Length, left.Length);
        for (int i = 0; i < right.Length; i++)
        {
            Assert.Equal(right[i].X, -left[i].X);
            Assert.Equal(right[i].Y, left[i].Y); // vertical lift is shared, not mirrored
        }
    }

    [Fact]
    public void CwAndCcwKicks_AreUnchangedAcrossRulesVersions()
    {
        // Only the 180 table was ever wrong. Canonical SRS must be byte-identical in both
        // versions, or the version branch would silently perturb ordinary rotations too.
        foreach (var type in new[] { PieceType.I, PieceType.O, PieceType.T, PieceType.S, PieceType.Z, PieceType.J, PieceType.L })
            for (int from = 0; from < 4; from++)
                foreach (var to in new[] { Piece.Cw((RotationState)from), Piece.Ccw((RotationState)from) })
                {
                    var f = (RotationState)from;
                    var a = Tetromino.KickSequence(type, f, to, RulesVersion.V1);
                    var b = Tetromino.KickSequence(type, f, to, RulesVersion.V2);
                    Assert.Equal(a, b);
                }
    }
}
