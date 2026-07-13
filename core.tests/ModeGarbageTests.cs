using Blockfall.Core;
using Xunit;

namespace Blockfall.Core.Tests;

public class ModeGarbageTests
{
    [Fact]
    public void DigRace_PrefillsGarbage_AndIsTimeAttack()
    {
        var g = Game.Create(GameModeId.Dig, seed: 42);
        g.Start();
        Assert.Equal(10, g.Board.GarbageRowCount());
        Assert.True(GameMode.IsTimeAttack(GameModeId.Dig));
        Assert.Equal(GameStatus.Running, g.Status);
    }

    [Fact]
    public void DigRace_CompletesWhenBoardIsGarbageFree()
    {
        var g = Game.Create(GameModeId.Dig, seed: 42);
        g.Start();

        // Simulate having dug out every garbage cell, then lock a piece so the
        // completion check runs.
        for (int r = 0; r < g.Board.TotalRows; r++)
            for (int c = 0; c < g.Board.Width; c++)
                if (g.Board[r, c] == PieceType.Garbage) g.Board[r, c] = PieceType.Empty;

        g.HardDrop();
        Assert.Equal(GameStatus.Completed, g.Status);
    }

    [Fact]
    public void Survival_GarbageWellsUpOverTime_ButCommitsOnlyOnLock()
    {
        var g = Game.Create(GameModeId.Survival, seed: 123);
        g.Start();
        Assert.Equal(0, g.PendingGarbage);
        Assert.Equal(0, g.Board.GarbageRowCount());

        // 5 seconds in small steps: the first rise (interval 4s) queues garbage,
        // but nothing lands on the board until a piece locks.
        for (int i = 0; i < 100; i++) g.Update(0.05);

        Assert.True(g.PendingGarbage >= 1);
        Assert.Equal(0, g.Board.GarbageRowCount());
    }

    [Fact]
    public void Survival_LockingWithoutClearing_CommitsPendingGarbage()
    {
        var g = Game.Create(GameModeId.Survival, seed: 123);
        g.Start();
        for (int i = 0; i < 100; i++) g.Update(0.05);
        int pending = g.PendingGarbage;
        Assert.True(pending >= 1);

        g.HardDrop(); // a single piece can't clear a line -> incoming garbage commits

        Assert.True(g.Board.GarbageRowCount() >= 1);
        Assert.True(g.PendingGarbage < pending);
    }

    [Fact]
    public void Master_Is20G_FromTheStart()
    {
        var g = Game.Create(GameModeId.Master, seed: 7);
        Assert.Equal(1, g.Config.MaxGravityLevel);
        Assert.Equal(5, g.Scoring.Level);                                  // starts fast
        Assert.Equal(1.0 / 60.0, g.Config.GravityForLevel(g.Scoring.Level), 6); // instant gravity
    }

    [Fact]
    public void NonGarbageModes_TrackAttackReadout_WithoutSpawningGarbage()
    {
        // Attack readout works in any mode, but no garbage ever appears where the
        // mode has no garbage mechanics.
        var g = Game.Create(GameModeId.Marathon, seed: 5);
        g.Start();
        for (int i = 0; i < 20; i++) g.HardDrop(); // place pieces, guaranteed no garbage
        Assert.Equal(0, g.Board.GarbageRowCount());
        Assert.Equal(0, g.PendingGarbage);
    }
}
