using Blockfall.Core;
using Xunit;

namespace Blockfall.Core.Tests;

public class SeedCodeTests
{
    [Theory]
    [InlineData(0UL)]
    [InlineData(1UL)]
    [InlineData(31UL)]
    [InlineData(32UL)]
    [InlineData(20260709UL)]
    [InlineData(123456789012345UL)]
    [InlineData(ulong.MaxValue)]
    public void RoundTrips(ulong seed)
    {
        string code = SeedCode.Encode(seed);
        Assert.True(SeedCode.TryDecode(code, out var back));
        Assert.Equal(seed, back);
    }

    [Fact]
    public void Encode_Zero_IsZero() => Assert.Equal("0", SeedCode.Encode(0));

    [Fact]
    public void Decode_IsForgiving_AboutCaseSpacingAndConfusables()
    {
        string canonical = SeedCode.Encode(987654321);
        Assert.True(SeedCode.TryDecode(canonical.ToLowerInvariant(), out var lower));
        Assert.Equal(987654321UL, lower);

        // Hyphens/spaces ignored; O->0, I/L->1 corrected.
        Assert.True(SeedCode.TryDecode("  " + canonical[..2] + "-" + canonical[2..], out var spaced));
        Assert.Equal(987654321UL, spaced);
    }

    [Fact]
    public void Decode_RejectsGarbage()
        => Assert.False(SeedCode.TryDecode("!!!", out _));

    [Fact]
    public void FromText_IsStable_AndVaries()
    {
        Assert.Equal(SeedCode.FromText("hello"), SeedCode.FromText("hello"));
        Assert.NotEqual(SeedCode.FromText("hello"), SeedCode.FromText("world"));
    }
}

public class ModifierSetTests
{
    [Fact]
    public void EmptySet_LeavesConfigUnchanged()
    {
        var cfg = GameConfig.Default;
        var result = ModifierSet.Apply(cfg, System.Array.Empty<GameModifier>());
        Assert.Equal(cfg.HoldEnabled, result.HoldEnabled);
        Assert.Equal(cfg.BaseGravity, result.BaseGravity, 6);
    }

    [Fact]
    public void EachModifier_FlipsTheRightKnob()
    {
        Assert.False(ModifierSet.Apply(GameConfig.Default, new[] { GameModifier.NoHold }).HoldEnabled);
        Assert.False(ModifierSet.Apply(GameConfig.Default, new[] { GameModifier.NoGhost }).GhostEnabled);
        Assert.True(ModifierSet.Apply(GameConfig.Default, new[] { GameModifier.AllSpin }).AllSpin);
        Assert.Equal(0.35, ModifierSet.Apply(GameConfig.Default, new[] { GameModifier.Blitz }).BaseGravity, 6);
        Assert.Equal(3.0, ModifierSet.Apply(GameConfig.Default, new[] { GameModifier.LowGravity }).BaseGravity, 6);
        Assert.Equal(0.12, ModifierSet.Apply(GameConfig.Default, new[] { GameModifier.HardLock }).LockDelay, 6);
    }

    [Fact]
    public void Modifiers_Compose()
    {
        var cfg = ModifierSet.Apply(GameConfig.Default, new[] { GameModifier.NoHold, GameModifier.AllSpin });
        Assert.False(cfg.HoldEnabled);
        Assert.True(cfg.AllSpin);
    }
}

public class AllSpinTests
{
    // Encases a piece so it fits but cannot move in any direction (immobile).
    private static (Board, Piece) Encased(PieceType type)
    {
        var board = new Board();
        var piece = new Piece(type, RotationState.Spawn, new Vec2(20, 4));
        for (int r = 0; r < board.TotalRows; r++)
            for (int c = 0; c < board.Width; c++)
                board[r, c] = PieceType.Garbage;
        foreach (var cell in piece.Cells())
            board[cell.Row, cell.Col] = PieceType.Empty;
        Assert.True(board.CanPlace(piece));
        return (board, piece);
    }

    [Fact]
    public void ImmobileNonT_WithAllSpin_IsFullSpin()
    {
        var (board, piece) = Encased(PieceType.S);
        Assert.Equal(SpinType.Full, SpinDetector.Detect(board, piece, true, 0, allSpin: true));
    }

    [Fact]
    public void ImmobileNonT_WithoutAllSpin_IsNone()
    {
        var (board, piece) = Encased(PieceType.L);
        Assert.Equal(SpinType.None, SpinDetector.Detect(board, piece, true, 0, allSpin: false));
    }

    [Fact]
    public void ImmobileNonT_ButNotRotated_IsNone()
    {
        var (board, piece) = Encased(PieceType.J);
        Assert.Equal(SpinType.None, SpinDetector.Detect(board, piece, lastActionWasRotation: false, 0, allSpin: true));
    }

    [Fact]
    public void MobileNonT_WithAllSpin_IsNone()
    {
        // Free piece on an empty board can move, so it is not a spin.
        var board = new Board();
        var piece = new Piece(PieceType.Z, RotationState.Spawn, new Vec2(20, 4));
        Assert.Equal(SpinType.None, SpinDetector.Detect(board, piece, true, 0, allSpin: true));
    }
}
