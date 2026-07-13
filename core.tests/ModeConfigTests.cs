using System.Collections.Generic;
using Blockfall.Core;
using Xunit;

namespace Blockfall.Core.Tests;

public class ModeConfigTests
{
    [Fact]
    public void With_OverridesOnlyHandlingKnobs_AndKeepsTheRest()
    {
        var baseCfg = new GameConfig { BaseGravity = 1.2, MaxGravityLevel = 0, LockDelay = 0.4 };
        var tuned = baseCfg.With(das: 0.05, arr: 0.0, ghost: false);

        // Overridden.
        Assert.Equal(0.05, tuned.Das, 6);
        Assert.Equal(0.0, tuned.Arr, 6);
        Assert.False(tuned.GhostEnabled);

        // Preserved.
        Assert.Equal(1.2, tuned.BaseGravity, 6);
        Assert.Equal(0, tuned.MaxGravityLevel);
        Assert.Equal(0.4, tuned.LockDelay, 6);

        // Original is untouched (immutability).
        Assert.Equal(0.133, baseCfg.Das, 6);
        Assert.True(baseCfg.GhostEnabled);
    }

    [Fact]
    public void With_NullArgs_KeepCurrentValues()
    {
        var cfg = GameConfig.Default.With(das: 0.2);
        Assert.Equal(0.2, cfg.Das, 6);
        Assert.Equal(GameConfig.Default.Arr, cfg.Arr, 6);
        Assert.Equal(GameConfig.Default.GhostEnabled, cfg.GhostEnabled);
    }

    [Fact]
    public void WithConfig_SwapsConfig_KeepsRules()
    {
        var mode = GameMode.Sprint40;
        var newCfg = mode.Config.With(das: 0.01);
        var tuned = mode.WithConfig(newCfg);

        Assert.Equal(mode.Id, tuned.Id);
        Assert.Equal(mode.LineGoal, tuned.LineGoal);
        Assert.Equal(mode.TimeLimit, tuned.TimeLimit);
        Assert.Equal(mode.LevelCap, tuned.LevelCap);
        Assert.Equal(mode.CanTopOut, tuned.CanTopOut);
        Assert.Equal(0.01, tuned.Config.Das, 6);
    }

    [Fact]
    public void Create_WithConfigOverride_DisablesGhost()
    {
        var noGhost = GameConfig.Default.With(ghost: false);
        var game = Game.Create(GameModeId.Marathon, seed: 42, configOverride: noGhost);
        game.Start();
        Assert.Null(game.GhostPiece());
        Assert.False(game.Config.GhostEnabled);

        var withGhost = Game.Create(GameModeId.Marathon, seed: 42);
        withGhost.Start();
        Assert.NotNull(withGhost.GhostPiece());
    }

    [Fact]
    public void DailyMode_IsBounded_ScoreAttack()
    {
        var daily = GameMode.Daily;
        Assert.Equal(GameModeId.Daily, daily.Id);
        Assert.Equal(120.0, daily.TimeLimit, 6);
        Assert.Equal(GameModeId.Daily, GameMode.ById(GameModeId.Daily).Id);
    }

    [Fact]
    public void Daily_SameSeed_ProducesIdenticalPieceSequence()
    {
        var a = Game.Create(GameModeId.Daily, seed: 20260709);
        var b = Game.Create(GameModeId.Daily, seed: 20260709);
        var c = Game.Create(GameModeId.Daily, seed: 20260710);
        a.Start(); b.Start(); c.Start();

        List<PieceType> Seq(Game g)
        {
            var list = new List<PieceType>();
            var preview = g.Preview();
            for (int i = 0; i < preview.Count; i++) list.Add(preview[i]);
            return list;
        }

        Assert.Equal(Seq(a), Seq(b));          // same day = same pieces everywhere
        Assert.NotEqual(Seq(a), Seq(c));       // next day differs
        Assert.Equal(a.Current!.Type, b.Current!.Type);
    }
}
