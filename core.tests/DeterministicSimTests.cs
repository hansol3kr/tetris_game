using System;
using System.Collections.Generic;
using Blockfall.Core;
using Xunit;

namespace Blockfall.Core.Tests;

public class DeterministicSimTests
{
    // Pseudo-random but reproducible button stream that actually plays the game.
    private static Buttons RandButtons(XorShiftRandom r)
    {
        Buttons b = Buttons.None;
        int move = r.Next(100);
        if (move < 22) b |= Buttons.Left;
        else if (move < 44) b |= Buttons.Right;
        if (r.Next(100) < 18) b |= Buttons.Soft;
        if (r.Next(100) < 12) b |= Buttons.RotateCw;
        if (r.Next(100) < 5) b |= Buttons.RotateCcw;
        if (r.Next(100) < 9) b |= Buttons.Hard;
        if (r.Next(100) < 4) b |= Buttons.Hold;
        return b;
    }

    private static (long score, int lines, int pieces, PieceType[] board, GameStatus status)
        RunButtons(GameModeId mode, ulong seed, IReadOnlyList<Buttons> stream)
    {
        var g = Game.Create(mode, seed);
        var p = new InputProcessor(g.Config);
        g.Start();
        foreach (var b in stream)
        {
            if (g.Status != GameStatus.Running) break;
            p.Step(b, g);
            g.Update(Sim.TickDt);
        }
        return (g.Scoring.Score, g.Scoring.LinesCleared, g.Stats.PiecesPlaced, g.Board.Snapshot(), g.Status);
    }

    [Fact]
    public void SameButtonStream_SameSeed_IsBitIdentical()
    {
        var rng = new XorShiftRandom(1);
        var stream = new List<Buttons>();
        for (int i = 0; i < 4000; i++) stream.Add(RandButtons(rng));

        var a = RunButtons(GameModeId.Marathon, 77, stream);
        var b = RunButtons(GameModeId.Marathon, 77, stream);

        Assert.Equal(a.score, b.score);
        Assert.Equal(a.lines, b.lines);
        Assert.Equal(a.pieces, b.pieces);
        Assert.Equal(a.status, b.status);
        Assert.Equal(a.board, b.board);
    }

    [Fact]
    public void InputProcessor_MovesAndDropsThePiece()
    {
        var g = Game.Create(GameModeId.Zen, 5);
        var p = new InputProcessor(g.Config);
        g.Start();
        int startCol = g.Current!.Origin.Col;

        p.Step(Buttons.Right, g); g.Update(Sim.TickDt);
        Assert.Equal(startCol + 1, g.Current!.Origin.Col);

        int piecesBefore = g.Stats.PiecesPlaced;
        p.Step(Buttons.Hard, g); g.Update(Sim.TickDt);
        Assert.Equal(piecesBefore + 1, g.Stats.PiecesPlaced); // hard drop locked it
    }

    [Fact]
    public void RapidConsecutiveEdgePresses_EachFire()
    {
        // Regression: two rotate pulses on ADJACENT ticks must both rotate. The old
        // rising-edge detection masked the second (b & ~_prev == 0).
        var g = Game.Create(GameModeId.Zen, 3);
        var p = new InputProcessor(g.Config);
        g.Start();
        int rotations = 0;
        g.PieceRotated += r => { if (r.Success) rotations++; };
        p.Step(Buttons.RotateCw, g); g.Update(Sim.TickDt);
        p.Step(Buttons.RotateCw, g); g.Update(Sim.TickDt);
        Assert.Equal(2, rotations);
    }

    // ---- Replay ------------------------------------------------------------
    [Fact]
    public void Replay_ReproducesRun_Exactly()
    {
        var g1 = Game.Create(GameModeId.Marathon, 4242);
        var proc = new InputProcessor(g1.Config);
        g1.Start();
        var rec = new ReplayRecorder(4242, GameModeId.Marathon,
            Array.Empty<GameModifier>(), g1.Config.Das, g1.Config.Arr);

        var rng = new XorShiftRandom(31337);
        for (int t = 0; t < 20000 && g1.Status == GameStatus.Running; t++)
        {
            var b = RandButtons(rng);
            rec.Record(b);
            proc.Step(b, g1);
            g1.Update(Sim.TickDt);
        }

        var replay = rec.Build(g1);
        var player = new ReplayPlayer(replay);
        var g2 = player.PlayToEnd();

        Assert.Equal(g1.Scoring.Score, g2.Scoring.Score);
        Assert.Equal(g1.Scoring.LinesCleared, g2.Scoring.LinesCleared);
        Assert.Equal(g1.Stats.PiecesPlaced, g2.Stats.PiecesPlaced);
        Assert.Equal(g1.Status, g2.Status);
        Assert.Equal(g1.Board.Snapshot(), g2.Board.Snapshot());
        // The baked metadata matches the actual outcome.
        Assert.Equal(replay.FinalScore, g2.Scoring.Score);
        Assert.Equal(replay.FinalLines, g2.Scoring.LinesCleared);
    }

    [Fact]
    public void Replay_SerializeRoundTrip_IsIdentical()
    {
        var g1 = Game.Create(GameModeId.Sprint, 9);
        var proc = new InputProcessor(g1.Config);
        g1.Start();
        var rec = new ReplayRecorder(9, GameModeId.Sprint,
            new[] { GameModifier.Blitz }, g1.Config.Das, g1.Config.Arr);
        var rng = new XorShiftRandom(2);
        for (int t = 0; t < 3000 && g1.Status == GameStatus.Running; t++)
        {
            var b = RandButtons(rng);
            rec.Record(b);
            proc.Step(b, g1);
            g1.Update(Sim.TickDt);
        }
        var replay = rec.Build(g1);

        var round = ReplayData.Deserialize(replay.Serialize());
        Assert.Equal(replay.Seed, round.Seed);
        Assert.Equal(replay.Mode, round.Mode);
        Assert.Equal(replay.Modifiers, round.Modifiers);
        Assert.Equal(replay.Inputs, round.Inputs);
        Assert.Equal(replay.Das, round.Das);

        // And it still plays back identically.
        var playA = new ReplayPlayer(replay).PlayToEnd();
        var playB = new ReplayPlayer(round).PlayToEnd();
        Assert.Equal(playA.Board.Snapshot(), playB.Board.Snapshot());
        Assert.Equal(playA.Scoring.Score, playB.Scoring.Score);
    }

}
