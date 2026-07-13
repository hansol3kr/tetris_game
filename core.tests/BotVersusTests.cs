using System;
using System.Collections.Generic;
using System.Linq;
using Blockfall.Core;
using Xunit;

namespace Blockfall.Core.Tests;

public class BotVersusTests
{
    private const double Dt = 1.0 / 60.0;

    // ---- Board.Clone --------------------------------------------------------
    [Fact]
    public void BoardClone_IsIndependentCopy()
    {
        var b = new Board();
        b[39, 0] = PieceType.T;
        var c = b.Clone();
        Assert.Equal(PieceType.T, c[39, 0]);
        c[39, 1] = PieceType.I;          // mutate the clone
        Assert.Equal(PieceType.Empty, b[39, 1]); // original untouched
    }

    // ---- Spawn counter (bot stale-plan guard relies on this) ----------------
    [Fact]
    public void SpawnCount_IncrementsOnEverySpawn()
    {
        var g = Game.Create(GameModeId.Marathon, 1);
        Assert.Equal(0, g.SpawnCount);
        g.Start();
        Assert.Equal(1, g.SpawnCount);   // first piece
        g.HardDrop();
        Assert.Equal(2, g.SpawnCount);   // lock -> next spawns
        g.HoldPiece();
        Assert.Equal(3, g.SpawnCount);   // hold on empty -> another spawn
    }

    // ---- Bot competence: the real evaluator validation ----------------------
    [Fact]
    public void MasterBot_Completes_Sprint40()
    {
        var g = Game.Create(GameModeId.Sprint, seed: 2026);
        var bot = new BotPlayer(BotDifficulty.Master, new XorShiftRandom(1));
        g.Start();

        int guard = 0;
        while (g.Status == GameStatus.Running && guard++ < 200_000)
        {
            bot.Update(Dt, g);
            g.Update(Dt);
        }

        Assert.Equal(GameStatus.Completed, g.Status);
        Assert.True(g.Scoring.LinesCleared >= 40, $"cleared {g.Scoring.LinesCleared}");
    }

    [Fact]
    public void MasterBot_KeepsBoardClean_NoEarlyTopOut()
    {
        // In endless Zen, a strong bot should place many pieces without dying.
        var g = Game.Create(GameModeId.Zen, seed: 99);
        var bot = new BotPlayer(BotDifficulty.Master, new XorShiftRandom(7));
        g.Start();
        for (int i = 0; i < 30_000 && g.Status == GameStatus.Running; i++)
        {
            bot.Update(Dt, g);
            g.Update(Dt);
        }
        Assert.Equal(GameStatus.Running, g.Status);   // still alive
        Assert.True(g.Stats.PiecesPlaced > 50);
        Assert.True(g.Scoring.LinesCleared > 20);
    }

    // ---- Grandmaster (2-ply lookahead) --------------------------------------
    [Fact]
    public void GrandmasterBot_Completes_Sprint40()
    {
        var g = Game.Create(GameModeId.Sprint, seed: 314);
        var bot = new BotPlayer(BotDifficulty.Grandmaster, new XorShiftRandom(2));
        g.Start();
        int guard = 0;
        while (g.Status == GameStatus.Running && guard++ < 200_000)
        {
            bot.Update(Dt, g);
            g.Update(Dt);
        }
        Assert.Equal(GameStatus.Completed, g.Status);
        Assert.True(g.Scoring.LinesCleared >= 40);
    }

    [Fact]
    public void GrandmasterBot_IsDeterministic()
    {
        long RunZen()
        {
            var g = Game.Create(GameModeId.Zen, 88);
            var bot = new BotPlayer(BotDifficulty.Grandmaster, new XorShiftRandom(9));
            g.Start();
            for (int i = 0; i < 8000 && g.Status == GameStatus.Running; i++) { bot.Update(Dt, g); g.Update(Dt); }
            return g.Scoring.Score;
        }
        Assert.Equal(RunZen(), RunZen());
    }

    [Fact]
    public void DifficultyTiers_IncludeGrandmaster_WithLookahead()
    {
        Assert.Equal(5, BotDifficulty.All.Length);
        Assert.Contains(BotDifficulty.All, d => d.Name == "Grandmaster" && d.Lookahead);
    }

    // ---- Garbage delivery / cancellation ------------------------------------
    [Fact]
    public void ReceiveGarbage_CommitsOnLock_InVersus()
    {
        var g = new Game(GameMode.Versus,
            new SevenBagGenerator(new XorShiftRandom(3)), new XorShiftRandom(4))
        { CommitPendingGarbageOnLock = true };
        int received = 0;
        g.GarbageReceived += n => received += n;
        g.Start();

        g.ReceiveGarbage(4);
        Assert.Equal(4, g.PendingGarbage);

        g.HardDrop(); // a non-clearing lock on an empty board commits the queue

        Assert.Equal(4, received);
        Assert.Equal(0, g.PendingGarbage);
        Assert.Equal(4, g.Board.GarbageRowCount());
    }

    [Fact]
    public void OutgoingAttack_IsFullyCancelled_ByLargeIncoming()
    {
        var g = new Game(GameMode.Versus,
            new SevenBagGenerator(new XorShiftRandom(11)), new XorShiftRandom(12))
        { CommitPendingGarbageOnLock = true };
        int sent = 0;
        g.GarbageSentToOpponent += n => sent += n;
        g.Start();

        // Force the piece to clear every row it lands in (so it produces an attack).
        ForceClearOnDrop(g);
        g.ReceiveGarbage(20); // swamp any outgoing attack
        g.HardDrop();

        Assert.Equal(0, sent); // the whole attack was eaten by incoming garbage
    }

    [Fact]
    public void OutgoingAttack_EqualsAttackTable_WhenNoIncoming()
    {
        var g = new Game(GameMode.Versus,
            new SevenBagGenerator(new XorShiftRandom(13)), new XorShiftRandom(14))
        { CommitPendingGarbageOnLock = true };
        int sent = 0;
        ClearResult res = default;
        g.GarbageSentToOpponent += n => sent += n;
        g.PieceLocked += ev => res = ev.Result;
        g.Start();

        ForceClearOnDrop(g);
        g.HardDrop();

        Assert.Equal(AttackTable.LinesSent(res), sent); // no incoming → full net goes out
    }

    // ---- VersusMatch --------------------------------------------------------
    [Fact]
    public void VersusMatch_IsDeterministic_ForSameSeed()
    {
        var m1 = Play(new VersusMatch(BotDifficulty.Hard, 4242), 6000);
        var m2 = Play(new VersusMatch(BotDifficulty.Hard, 4242), 6000);

        Assert.Equal(m1.BotGame.Scoring.LinesCleared, m2.BotGame.Scoring.LinesCleared);
        Assert.Equal(m1.BotGame.Stats.PiecesPlaced, m2.BotGame.Stats.PiecesPlaced);
        Assert.Equal(m1.PlayerGame.Stats.PiecesPlaced, m2.PlayerGame.Stats.PiecesPlaced);
        Assert.Equal(m1.Winner, m2.Winner);
    }

    [Fact]
    public void VersusMatch_IdlePlayer_LosesToBot()
    {
        var m = new VersusMatch(BotDifficulty.Normal, 55);
        m.Start();
        VersusSide? ended = null;
        m.MatchEnded += s => ended = s;

        int guard = 0;
        while (!m.IsOver && guard++ < 100_000)
            m.Update(Dt); // player never inputs -> stack rises by gravity -> tops out

        Assert.True(m.IsOver);
        Assert.Equal(VersusSide.Bot, m.Winner);
        Assert.Equal(VersusSide.Bot, ended);
    }

    [Fact]
    public void VersusMode_IsRegistered()
    {
        Assert.Equal(GameModeId.Versus, GameMode.ById(GameModeId.Versus).Id);
    }

    // ---- helpers ------------------------------------------------------------
    private static VersusMatch Play(VersusMatch m, int steps)
    {
        m.Start();
        for (int i = 0; i < steps && !m.IsOver; i++) m.Update(Dt);
        return m;
    }

    // Fills the complement of the current piece's footprint in every row it will
    // occupy, so hard-dropping completes those rows (a multi-line clear).
    private static void ForceClearOnDrop(Game g)
    {
        var landed = g.Board.HardDropTarget(g.Current!).landed;
        var cells = landed.Cells().ToList();
        foreach (var row in cells.Select(c => c.Row).Distinct())
        {
            var pieceCols = cells.Where(c => c.Row == row).Select(c => c.Col).ToHashSet();
            for (int col = 0; col < g.Board.Width; col++)
                if (!pieceCols.Contains(col))
                    g.Board[row, col] = PieceType.Garbage;
        }
    }
}
