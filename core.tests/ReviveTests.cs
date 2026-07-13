using Blockfall.Core;
using Xunit;

namespace Blockfall.Core.Tests;

public class ReviveTests
{
    /// <summary>Hard-drop until the stack tops out (no line ever completes with one column untouched? — we just drop everything in place, which eventually block-outs).</summary>
    private static Game MakeToppedOutGame(ulong seed = 7)
    {
        var g = Game.Create(GameModeId.Marathon, seed);
        g.Start();
        for (int i = 0; i < 400 && g.Status == GameStatus.Running; i++)
            g.HardDrop(); // stacking straight down can clear the odd line but must eventually top out
        Assert.Equal(GameStatus.GameOver, g.Status);
        return g;
    }

    [Fact]
    public void Revive_WhileRunning_IsRejected()
    {
        var g = Game.Create(GameModeId.Marathon, 1);
        g.Start();
        Assert.False(g.Revive());
        Assert.Equal(GameStatus.Running, g.Status);
    }

    [Fact]
    public void Revive_AfterTopOut_ClearsBoardAndResumes()
    {
        var g = MakeToppedOutGame();
        long score = g.Scoring.Score;
        int lines = g.Scoring.LinesCleared;
        double elapsed = g.Elapsed;

        Assert.True(g.Revive());

        Assert.Equal(GameStatus.Running, g.Status);
        Assert.True(g.Board.IsEmpty());
        Assert.Equal(0, g.PendingGarbage);
        Assert.NotNull(g.Current);
        // Progress is kept — a revive is a continuation, not a restart.
        Assert.Equal(score, g.Scoring.Score);
        Assert.Equal(lines, g.Scoring.LinesCleared);
        Assert.Equal(elapsed, g.Elapsed);
        // The chain is broken.
        Assert.Equal(-1, g.Scoring.ComboCounter);
        Assert.False(g.Scoring.BackToBackActive);
    }

    [Fact]
    public void Revive_ThenPlay_LocksNormally()
    {
        var g = MakeToppedOutGame();
        Assert.True(g.Revive());
        int placed = g.Stats.PiecesPlaced;
        Assert.True(g.HardDrop());
        Assert.Equal(placed + 1, g.Stats.PiecesPlaced);
        Assert.Equal(GameStatus.Running, g.Status);
        Assert.False(g.Board.IsEmpty());
    }

    [Fact]
    public void Revive_AfterBlockOutAtSpawn_RespawnsTheBlockedPieceType()
    {
        // Deterministic block-out: pre-fill the spawn area (one column left open
        // so nothing clears), then Start — the very first spawn is blocked.
        var g = new Game(GameMode.ById(GameModeId.Marathon),
            new SevenBagGenerator(new XorShiftRandom(5)), new XorShiftRandom(5));
        for (int row = 0; row < g.Board.VisibleTop + 2; row++)
            for (int col = 0; col < g.Board.Width - 1; col++)
                g.Board[row, col] = PieceType.Garbage;
        g.Start();
        Assert.Equal(GameStatus.GameOver, g.Status);

        // The blocked piece was consumed from the bag but never played — the
        // revive gives that exact piece back instead of skipping it.
        var blockedType = g.Current!.Type;
        Assert.True(g.Revive());
        Assert.Equal(GameStatus.Running, g.Status);
        Assert.Equal(blockedType, g.Current!.Type);
    }

    [Fact]
    public void Revive_Twice_RequiresASecondGameOver()
    {
        var g = MakeToppedOutGame();
        Assert.True(g.Revive());
        Assert.False(g.Revive()); // running again — no double revive
    }

    [Fact]
    public void Revive_InSurvival_DropsPendingGarbage()
    {
        var g = Game.Create(GameModeId.Survival, 3);
        g.Start();
        g.ReceiveGarbage(6);
        Assert.True(g.PendingGarbage > 0);
        for (int i = 0; i < 500 && g.Status == GameStatus.Running; i++)
            g.HardDrop();
        Assert.Equal(GameStatus.GameOver, g.Status);
        Assert.True(g.Revive());
        Assert.Equal(0, g.PendingGarbage);
        Assert.True(g.Board.IsEmpty());
    }
}
