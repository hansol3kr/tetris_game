using Blockfall.Core;
using Xunit;

namespace Blockfall.Core.Tests;

/// <summary>
/// Regression cover for the player-handling overlay used by every play surface.
/// Original bug: the versus controllers built their input from the raw mode config,
/// so the player's DAS/ARR settings were silently ignored in CPU duels AND in online
/// (including ranked) matches — the hand tuned on the settings screen only worked solo.
/// The fix routes both duel paths through <see cref="GameConfig.WithHandlingFrom"/>,
/// which must take handling knobs and NOTHING else, so preferences can never change
/// the rules of a match.
/// </summary>
public class VersusHandlingTests
{
    private const double Dt = 1.0 / 60.0;

    private static GameConfig Handling(double das = 0.09, double arr = 0.0, bool ghost = true)
        => GameConfig.Default.With(das: das, arr: arr, ghost: ghost);

    // ---- GameConfig.WithHandlingFrom ---------------------------------------

    [Fact]
    public void WithHandlingFrom_CopiesDasArr()
    {
        var result = GameConfig.Default.WithHandlingFrom(Handling(das: 0.075, arr: 0.005));
        Assert.Equal(0.075, result.Das);
        Assert.Equal(0.005, result.Arr);
    }

    [Fact]
    public void WithHandlingFrom_LeavesSimulationRulesUntouched()
    {
        // A mode's rules (gravity, lock delay, scoring, spawn/preview/hold) must survive
        // the overlay even when the handling config carries wildly different values —
        // otherwise a player's settings could rewrite a duel's rules.
        var mode = new GameConfig
        {
            BaseGravity = 0.35, LockDelay = 0.2, MaxGravityLevel = 0,
            ScoreMultiplier = 1.5, HoldEnabled = false, PreviewCount = 2, AllSpin = true,
        };
        var handling = GameConfig.Default.With(
            das: 0.05, arr: 0.0, baseGravity: 9.9, lockDelay: 9.9, scoreMultiplier: 9.9,
            previewCount: 9, hold: true, allSpin: false);

        var result = mode.WithHandlingFrom(handling);

        Assert.Equal(0.05, result.Das);
        Assert.Equal(0.0, result.Arr);
        Assert.Equal(0.35, result.BaseGravity);
        Assert.Equal(0.2, result.LockDelay);
        Assert.Equal(0, result.MaxGravityLevel);
        Assert.Equal(1.5, result.ScoreMultiplier);
        Assert.False(result.HoldEnabled);
        Assert.Equal(2, result.PreviewCount);
        Assert.True(result.AllSpin);
    }

    [Fact]
    public void WithHandlingFrom_GhostSealedByMode_StaysSealed()
    {
        // A preference may only RESTRICT the ghost, never grant it back — a Descent charm
        // (or a future mode) that seals it off must win over the settings toggle.
        var sealedMode = GameConfig.Default.With(ghost: false);
        Assert.False(sealedMode.WithHandlingFrom(Handling(ghost: true)).GhostEnabled);
        Assert.True(GameConfig.Default.WithHandlingFrom(Handling(ghost: true)).GhostEnabled);
        Assert.False(GameConfig.Default.WithHandlingFrom(Handling(ghost: false)).GhostEnabled);
    }

    // ---- VersusMatch: player-side only -------------------------------------

    [Fact]
    public void VersusMatch_PlayerHandling_AppliesToPlayerSideOnly()
    {
        var m = new VersusMatch(BotDifficulty.Normal, seed: 7, playerHandling: Handling(das: 0.08, arr: 0.004));

        Assert.Equal(0.08, m.PlayerGame.Config.Das);
        Assert.Equal(0.004, m.PlayerGame.Config.Arr);
        // The bot never inherits the human's hand.
        Assert.Equal(GameMode.Versus.Config.Das, m.BotGame.Config.Das);
        Assert.Equal(GameMode.Versus.Config.Arr, m.BotGame.Config.Arr);
    }

    [Fact]
    public void VersusMatch_WithoutHandling_KeepsModeDefaults()
    {
        var m = new VersusMatch(BotDifficulty.Normal, seed: 7);
        Assert.Equal(GameMode.Versus.Config.Das, m.PlayerGame.Config.Das);
        Assert.Equal(GameMode.Versus.Config.Arr, m.PlayerGame.Config.Arr);
    }

    [Fact]
    public void VersusMatch_PlayerHandling_DoesNotPerturbSimulation()
    {
        // Determinism guard: DAS/ARR/ghost are consumed by the input layer only, so the
        // same seed must still produce a bit-identical simulation on both sides. If a
        // handling knob ever leaks into the engine, this test catches it.
        var plain = new VersusMatch(BotDifficulty.Normal, seed: 4242);
        var tuned = new VersusMatch(BotDifficulty.Normal, seed: 4242,
            playerHandling: Handling(das: 0.5, arr: 0.25, ghost: false));
        plain.Start();
        tuned.Start();

        for (int i = 0; i < 3_000; i++) { plain.Update(Dt); tuned.Update(Dt); }

        AssertSameSimulation(plain.PlayerGame, tuned.PlayerGame);
        AssertSameSimulation(plain.BotGame, tuned.BotGame);
    }

    private static void AssertSameSimulation(Game a, Game b)
    {
        Assert.Equal(a.Status, b.Status);
        Assert.Equal(a.Scoring.Score, b.Scoring.Score);
        Assert.Equal(a.Scoring.LinesCleared, b.Scoring.LinesCleared);
        Assert.Equal(a.SpawnCount, b.SpawnCount);
        Assert.Equal(a.PendingGarbage, b.PendingGarbage);
        for (int r = 0; r < a.Board.TotalRows; r++)
            for (int c = 0; c < a.Board.Width; c++)
                Assert.Equal(a.Board[r, c], b.Board[r, c]);
    }

    // ---- Replay reconstruction still matches the live config ----------------

    [Fact]
    public void ReplayPlayer_RebuildsTheHandlingItRecorded()
    {
        // ReplayData records Das/Arr and ReplayPlayer rebuilds the config from them.
        // Solo builds its live config the same way, so a recorded run re-simulates
        // under exactly the handling it was played with.
        var handling = Handling(das: 0.111, arr: 0.011);
        var live = GameMode.ById(GameModeId.Marathon).Config.WithHandlingFrom(handling);
        var replayed = GameMode.ById(GameModeId.Marathon).Config.With(das: live.Das, arr: live.Arr);
        Assert.Equal(live.Das, replayed.Das);
        Assert.Equal(live.Arr, replayed.Arr);
    }
}
