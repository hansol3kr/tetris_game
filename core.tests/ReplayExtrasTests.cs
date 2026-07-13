using System;
using Blockfall.Core;
using Xunit;

namespace Blockfall.Core.Tests;

public class ReplayExtrasTests
{
    // Record a short deterministic run and return its ReplayData.
    private static ReplayData RecordRun(ulong seed = 4242, GameModeId mode = GameModeId.Marathon)
    {
        var g = Game.Create(mode, seed);
        var proc = new InputProcessor(g.Config);
        g.Start();
        var rec = new ReplayRecorder(seed, mode, Array.Empty<GameModifier>(), g.Config.Das, g.Config.Arr);
        var rng = new XorShiftRandom(999);
        for (int t = 0; t < 4000 && g.Status == GameStatus.Running; t++)
        {
            Buttons b = Buttons.None;
            int m = rng.Next(100);
            if (m < 22) b |= Buttons.Left; else if (m < 44) b |= Buttons.Right;
            if (rng.Next(100) < 15) b |= Buttons.RotateCw;
            if (rng.Next(100) < 10) b |= Buttons.Hard;
            rec.Record(b);
            proc.Step(b, g);
            g.Update(Sim.TickDt);
        }
        return rec.Build(g);
    }

    private static ReplayData WithScore(ReplayData r, long score) => new()
    {
        Version = r.Version, Seed = r.Seed, Mode = r.Mode, Modifiers = r.Modifiers,
        Das = r.Das, Arr = r.Arr, FinalScore = score, FinalLines = r.FinalLines,
        Duration = r.Duration, Inputs = r.Inputs,
    };

    // ---- Anti-cheat validation --------------------------------------------
    [Fact]
    public void Validate_GenuineReplay_Passes()
    {
        var r = RecordRun();
        var result = ReplayValidator.Validate(r);
        Assert.True(result.Valid, result.Reason);
        Assert.Equal(r.FinalScore, result.ActualScore);
        Assert.Equal(r.FinalLines, result.ActualLines);
    }

    [Fact]
    public void Validate_TamperedScore_Fails()
    {
        var r = RecordRun();
        var forged = WithScore(r, r.FinalScore + 9_999_999);
        var result = ReplayValidator.Validate(forged);
        Assert.False(result.Valid);
        Assert.Equal(r.FinalScore, result.ActualScore); // the honest re-sim value
    }

    [Fact]
    public void IsAuthentic_MatchesClaimedScore_Only()
    {
        var r = RecordRun();
        Assert.True(ReplayValidator.IsAuthentic(r, r.FinalScore));
        Assert.False(ReplayValidator.IsAuthentic(r, r.FinalScore + 1));
    }

    [Fact]
    public void Validate_EmptyReplay_Fails()
    {
        var empty = new ReplayData { Seed = 1, Mode = GameModeId.Marathon };
        Assert.False(ReplayValidator.Validate(empty).Valid);
    }

    // ---- Compression + share codes ----------------------------------------
    [Fact]
    public void CompressedRoundTrip_IsIdentical()
    {
        var r = RecordRun();
        var back = ReplayData.DeserializeCompressed(r.SerializeCompressed());
        Assert.Equal(r.Seed, back.Seed);
        Assert.Equal(r.Inputs, back.Inputs);
        Assert.Equal(r.FinalScore, back.FinalScore);
    }

    [Fact]
    public void Compression_ShrinksTheStream()
    {
        var r = RecordRun();
        Assert.True(r.SerializeCompressed().Length < r.Serialize().Length,
            "button streams should compress");
    }

    [Fact]
    public void ShareCode_RoundTrips_AndPlaysIdentically()
    {
        var r = RecordRun();
        var code = r.ToShareCode();
        Assert.True(ReplayData.TryFromShareCode(code, out var back));
        Assert.NotNull(back);
        Assert.Equal(r.Inputs, back!.Inputs);

        var a = new ReplayPlayer(r).PlayToEnd();
        var b = new ReplayPlayer(back).PlayToEnd();
        Assert.Equal(a.Board.Snapshot(), b.Board.Snapshot());
        Assert.Equal(a.Scoring.Score, b.Scoring.Score);
    }

    [Fact]
    public void ShareCode_Malformed_ReturnsFalse_NoThrow()
    {
        Assert.False(ReplayData.TryFromShareCode("not a valid code!!!", out var d));
        Assert.Null(d);
        Assert.False(ReplayData.TryFromShareCode("", out _));
    }
}
