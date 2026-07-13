using System.Linq;
using Blockfall.Core;
using Xunit;

namespace Blockfall.Core.Tests;

public class GameTests
{
    [Fact]
    public void Start_SpawnsAPiece_AndRuns()
    {
        var g = Game.Create(GameModeId.Marathon, 1);
        g.Start();
        Assert.Equal(GameStatus.Running, g.Status);
        Assert.NotNull(g.Current);
    }

    [Fact]
    public void HardDrop_LocksPiece_AndAdvances()
    {
        var g = Game.Create(GameModeId.Marathon, 1);
        g.Start();
        var first = g.Current!.Type;
        g.HardDrop();
        Assert.Equal(1, g.Stats.PiecesPlaced);
        Assert.NotNull(g.Current);            // next piece spawned
        Assert.False(g.Board.IsEmpty());      // blocks remain on the board
    }

    [Fact]
    public void HardDrop_CompletingARow_ClearsIt_AndScores()
    {
        var g = Game.Create(GameModeId.Marathon, 12345);
        g.Start();

        // Compute where the current piece lands on the empty board, then fill the
        // rest of its bottom row. Filling only the *complement* columns does not
        // change the piece's descent, so the row completes on hard drop.
        var landed = g.Board.HardDropTarget(g.Current!).landed;
        var cells = landed.Cells().ToList();
        int bottomRow = cells.Max(c => c.Row);
        var pieceCols = cells.Where(c => c.Row == bottomRow).Select(c => c.Col).ToHashSet();
        for (int col = 0; col < g.Board.Width; col++)
            if (!pieceCols.Contains(col))
                g.Board[bottomRow, col] = PieceType.Garbage;

        int before = g.Scoring.LinesCleared;
        g.HardDrop();

        Assert.True(g.Scoring.LinesCleared > before, "expected at least one line to clear");
        Assert.True(g.Scoring.Score > 0);
    }

    [Fact]
    public void Hold_SwapsPiece_AndBlocksASecondHoldUntilLock()
    {
        var g = Game.Create(GameModeId.Marathon, 55);
        g.Start();
        var original = g.Current!.Type;

        Assert.True(g.HoldPiece());
        Assert.Equal(original, g.Hold);
        Assert.False(g.HoldPiece());          // cannot hold twice before locking

        g.HardDrop();                          // lock re-enables hold
        Assert.True(g.HoldPiece());
    }

    [Fact]
    public void GhostPiece_IsAtOrBelowCurrent_AndOnValidGround()
    {
        var g = Game.Create(GameModeId.Marathon, 7);
        g.Start();
        var ghost = g.GhostPiece();
        Assert.NotNull(ghost);
        Assert.True(ghost!.Origin.Row >= g.Current!.Origin.Row);
        Assert.True(g.Board.CanPlace(ghost));
        Assert.True(g.Board.IsLanded(ghost));  // ghost rests on the stack/floor
    }

    [Fact]
    public void SameSeed_ProducesIdenticalPieceStream()
    {
        var a = Game.Create(GameModeId.Marathon, 2026);
        var b = Game.Create(GameModeId.Marathon, 2026);
        var seqA = new List<PieceType>();
        var seqB = new List<PieceType>();
        a.PieceSpawned += p => seqA.Add(p.Type);
        b.PieceSpawned += p => seqB.Add(p.Type);
        a.Start();
        b.Start();
        for (int i = 0; i < 20; i++) { a.HardDrop(); b.HardDrop(); }
        Assert.Equal(seqA, seqB);
    }

    [Fact]
    public void MoveLeftRight_ShiftsWithinWalls()
    {
        var g = Game.Create(GameModeId.Zen, 3);
        g.Start();
        int startCol = g.Current!.Origin.Col;
        Assert.True(g.MoveRight());
        Assert.Equal(startCol + 1, g.Current!.Origin.Col);
        Assert.True(g.MoveLeft());
        Assert.Equal(startCol, g.Current!.Origin.Col);

        // Slam left until the wall stops us; must not throw or move out of bounds.
        for (int i = 0; i < 20; i++) g.MoveLeft();
        foreach (var c in g.Current!.Cells())
            Assert.True(c.Col >= 0);
    }

    [Fact]
    public void Update_AppliesGravity_OverTime()
    {
        var g = Game.Create(GameModeId.Marathon, 9);
        g.Start();
        int startRow = g.Current!.Origin.Row;
        // Advance several seconds of game time in small steps at level 1 (~1s/cell).
        for (int i = 0; i < 300; i++) g.Update(1.0 / 60.0);
        Assert.True(g.Current!.Origin.Row > startRow || g.Stats.PiecesPlaced > 0,
            "piece should have fallen or already locked");
    }

    [Fact]
    public void Sprint_Completes_WhenLineGoalReached()
    {
        // Drive a Sprint game by repeatedly completing rows until the 40-line goal.
        var g = Game.Create(GameModeId.Sprint, 100);
        g.Start();

        int safety = 0;
        while (g.Status == GameStatus.Running && safety++ < 500)
        {
            // Reset the stack each iteration so exactly one line clears per piece
            // (keeps the test deterministic regardless of piece shape / residue).
            g.Board.Clear();
            var landed = g.Board.HardDropTarget(g.Current!).landed;
            var cells = landed.Cells().ToList();
            int bottomRow = cells.Max(c => c.Row);
            var pieceCols = cells.Where(c => c.Row == bottomRow).Select(c => c.Col).ToHashSet();
            for (int col = 0; col < g.Board.Width; col++)
                if (!pieceCols.Contains(col))
                    g.Board[bottomRow, col] = PieceType.Garbage;
            g.HardDrop();
        }

        Assert.Equal(GameStatus.Completed, g.Status);
        Assert.True(g.Scoring.LinesCleared >= 40);
    }
}
