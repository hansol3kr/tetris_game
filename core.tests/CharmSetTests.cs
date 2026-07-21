using System.Collections.Generic;
using System.Linq;
using Blockfall.Core;
using Xunit;

namespace Blockfall.Core.Tests;

public class CharmSetTests
{
    [Fact]
    public void Apply_EmptyCharms_ReturnsConfigUnchanged()
    {
        var cfg = GameConfig.Default;
        Assert.Same(cfg, CharmSet.Apply(cfg, System.Array.Empty<Charm>()));
    }

    [Fact]
    public void Apply_Prophet_AddsTwoPreviewsAndDisablesHold()
    {
        var cfg = CharmSet.Apply(GameConfig.Default, new[] { Charm.Prophet });
        Assert.Equal(GameConfig.Default.PreviewCount + 2, cfg.PreviewCount);
        Assert.False(cfg.HoldEnabled);
        Assert.Equal(1.0, cfg.ScoreMultiplier); // Prophet trades information, not score
    }

    [Fact]
    public void Apply_IsOrderIndependent_ForEntireLaunchPool()
    {
        var forward = CharmSet.All.ToArray();
        var reversed = CharmSet.All.Reverse().ToArray();

        var a = CharmSet.Apply(GameConfig.Default, forward);
        var b = CharmSet.Apply(GameConfig.Default, reversed);

        Assert.Equal(a.BaseGravity, b.BaseGravity, 12);
        Assert.Equal(a.LockDelay, b.LockDelay, 12);
        Assert.Equal(a.ScoreMultiplier, b.ScoreMultiplier, 12);
        Assert.Equal(a.MaxGravityLevel, b.MaxGravityLevel);
        Assert.Equal(a.PreviewCount, b.PreviewCount);
        Assert.Equal(a.HoldEnabled, b.HoldEnabled);
        Assert.Equal(a.GhostEnabled, b.GhostEnabled);
        Assert.Equal(a.AllSpin, b.AllSpin);

        // Mode shaping must be order-independent too (siege stage exercises every field).
        var siege = RunDirector.BaseStageMode(new StageSpec { Stratum = 5 });
        var ma = CharmSet.Apply(siege, forward);
        var mb = CharmSet.Apply(siege, reversed);
        Assert.Equal(ma.TimeLimit, mb.TimeLimit, 12);
        Assert.Equal(ma.AttackGoal, mb.AttackGoal);
        Assert.Equal(ma.InitialGarbage, mb.InitialGarbage);
        Assert.Equal(ma.GarbageRiseInterval, mb.GarbageRiseInterval, 12);
    }

    [Fact]
    public void Apply_MultiplierStack_ClampsAtUpperAndLowerBound()
    {
        // Greed 1.5 * Gambler 2.0 * Blindfold 1.25 * Monk 1.3 = 4.875 -> ceiling 3.0.
        var greedy = CharmSet.Apply(GameConfig.Default,
            new[] { Charm.Greed, Charm.Gambler, Charm.Blindfold, Charm.Monk });
        Assert.Equal(CharmSet.MultiplierCeiling, greedy.ScoreMultiplier);

        // The floor guards against compounding taxes (duplicates only possible in
        // a raw call — the draft never deals dupes, but Apply must still be total).
        var taxed = CharmSet.Apply(GameConfig.Default,
            new List<Charm> { Charm.Sapper, Charm.Sapper, Charm.Sapper, Charm.Sapper });
        Assert.Equal(CharmSet.MultiplierFloor, taxed.ScoreMultiplier);
    }

    [Fact]
    public void Apply_LockDelayCharms_HarshestWins()
    {
        // Anchor's 0.8s safety net must never erase Gambler's 0.15s razor.
        var cfg = CharmSet.Apply(GameConfig.Default, new[] { Charm.Anchor, Charm.Gambler });
        Assert.Equal(0.15, cfg.LockDelay, 12);
        // Gambler's x2.0 and Anchor's x0.9 still both apply to score.
        Assert.Equal(1.8, cfg.ScoreMultiplier, 12);
    }

    [Fact]
    public void Apply_Juggernaut_UncapsGravityAndSlowsFall()
    {
        var cfg = CharmSet.Apply(GameConfig.Default, new[] { Charm.Juggernaut });
        Assert.Equal(0, cfg.MaxGravityLevel); // never reaches 20G
        Assert.Equal(GameConfig.Default.BaseGravity * 1.2, cfg.BaseGravity, 12);
    }

    [Fact]
    public void Apply_StageShapingCharms_OnlyAffectMatchingStageKinds()
    {
        // Warlord lightens siege goals and shortens burst clocks — on a Dig stage
        // (no attack goal, no clock) it must change nothing.
        var dig = RunDirector.BaseStageMode(new StageSpec { Stratum = 1 });
        var shapedDig = CharmSet.Apply(dig, new[] { Charm.Warlord });
        Assert.Equal(0, shapedDig.AttackGoal);
        Assert.Equal(0.0, shapedDig.TimeLimit);
        Assert.Equal(dig.InitialGarbage, shapedDig.InitialGarbage);

        var siege = RunDirector.BaseStageMode(new StageSpec { Stratum = 3 });
        var shapedSiege = CharmSet.Apply(siege, new[] { Charm.Warlord });
        Assert.Equal(siege.AttackGoal - 4, shapedSiege.AttackGoal);

        // Sapper thins garbage on Dig and slows the rise on Siege; Burst is untouched.
        var sappedDig = CharmSet.Apply(dig, new[] { Charm.Sapper });
        Assert.Equal(dig.InitialGarbage - 2, sappedDig.InitialGarbage);
        var sappedSiege = CharmSet.Apply(siege, new[] { Charm.Sapper });
        Assert.Equal(siege.GarbageRiseInterval + 1.0, sappedSiege.GarbageRiseInterval, 12);
        var burst = RunDirector.BaseStageMode(new StageSpec { Stratum = 2 });
        var sappedBurst = CharmSet.Apply(burst, new[] { Charm.Sapper });
        Assert.Equal(burst.TimeLimit, sappedBurst.TimeLimit, 12);
        Assert.Equal(0, sappedBurst.InitialGarbage);
    }

    [Fact]
    public void Apply_TimedStageCharms_ComposeFactorThenDelta()
    {
        var burst = RunDirector.BaseStageMode(new StageSpec { Stratum = 2 }); // 60s
        // Feather x0.85, Hourglass +20, Warlord -15 => 60*0.85 + 20 - 15 = 56.
        var shaped = CharmSet.Apply(burst, new[] { Charm.Feather, Charm.Hourglass, Charm.Warlord });
        Assert.Equal(56.0, shaped.TimeLimit, 9);
        // And the clamp: enough shortening never drops a stage below the floor.
        var floor = CharmSet.Apply(
            burst.WithConfig(burst.Config), new List<Charm> { Charm.Warlord, Charm.Warlord, Charm.Warlord });
        Assert.True(floor.TimeLimit >= CharmSet.MinStageTime);
    }

    // ----- Per-charm numeric contracts ---------------------------------------
    // The exact numbers ARE the tuning contract. The aggregate tests above
    // (order independence, clamps) stay green even if a single value drifts,
    // so every live effect gets one absolute assertion here.

    [Fact]
    public void Apply_SpinSage_EnablesAllSpinAndTightensLockDelay()
    {
        var cfg = CharmSet.Apply(GameConfig.Default, new[] { Charm.SpinSage });
        Assert.True(cfg.AllSpin);
        Assert.Equal(0.35, cfg.LockDelay, 12);
        Assert.Equal(1.0, cfg.ScoreMultiplier); // SpinSage trades tempo, not score
    }

    [Fact]
    public void Apply_Greed_ExactTrade()
    {
        var cfg = CharmSet.Apply(GameConfig.Default, new[] { Charm.Greed });
        Assert.Equal(1.5, cfg.ScoreMultiplier, 12);
        Assert.Equal(GameConfig.Default.BaseGravity * 0.6, cfg.BaseGravity, 12);
    }

    [Fact]
    public void Apply_Blindfold_ExactTrade()
    {
        var cfg = CharmSet.Apply(GameConfig.Default, new[] { Charm.Blindfold });
        Assert.False(cfg.GhostEnabled);
        Assert.Equal(1.25, cfg.ScoreMultiplier, 12);
    }

    [Fact]
    public void Apply_Monk_ExactTrade()
    {
        var cfg = CharmSet.Apply(GameConfig.Default, new[] { Charm.Monk });
        Assert.False(cfg.HoldEnabled);
        Assert.Equal(1.3, cfg.ScoreMultiplier, 12);
    }

    [Fact]
    public void Apply_Hourglass_ExactTrade()
    {
        var cfg = CharmSet.Apply(GameConfig.Default, new[] { Charm.Hourglass });
        Assert.Equal(0.85, cfg.ScoreMultiplier, 12);
        var burst = RunDirector.BaseStageMode(new StageSpec { Stratum = 2 }); // 60s clock
        var shaped = CharmSet.Apply(burst, new[] { Charm.Hourglass });
        Assert.Equal(burst.TimeLimit + 20.0, shaped.TimeLimit, 12);
    }

    [Fact]
    public void Apply_Juggernaut_RaisesSiegeGoalByThree()
    {
        var siege = RunDirector.BaseStageMode(new StageSpec { Stratum = 3 });
        var shaped = CharmSet.Apply(siege, new[] { Charm.Juggernaut });
        Assert.Equal(siege.AttackGoal + 3, shaped.AttackGoal);
    }

    [Fact]
    public void Apply_GoalAndGarbageFloors_HoldForRawDuplicateCalls()
    {
        // Same totality standard the multiplier floor documents: raw duplicate
        // calls must clamp, never zero a stage's goal or prefill.
        var siege = RunDirector.BaseStageMode(new StageSpec { Stratum = 3 }); // goal 10
        var lightened = CharmSet.Apply(siege,
            new List<Charm> { Charm.Warlord, Charm.Warlord, Charm.Warlord });
        Assert.Equal(CharmSet.MinAttackGoal, lightened.AttackGoal);

        var dig = RunDirector.BaseStageMode(new StageSpec { Stratum = 1 }); // 6 rows
        var thinned = CharmSet.Apply(dig,
            new List<Charm> { Charm.Sapper, Charm.Sapper, Charm.Sapper });
        Assert.Equal(CharmSet.MinInitialGarbage, thinned.InitialGarbage);
    }

    [Fact]
    public void LabelAndDescribe_CoverEveryCharm()
    {
        foreach (var c in CharmSet.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(CharmSet.Label(c)));
            Assert.False(string.IsNullOrWhiteSpace(CharmSet.Describe(c)));
        }
        Assert.Equal(12, CharmSet.All.Length);
    }
}
