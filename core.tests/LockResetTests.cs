using System.Collections.Generic;
using Blockfall.Core;
using Xunit;

namespace Blockfall.Core.Tests;

/// <summary>
/// Lock-delay reset accounting.
///
/// Original bug (shipped v1.0–v1.4): <c>Game.OnPieceChanged</c> charged one reset per
/// CELL the piece moved, and <c>InputProcessor</c>'s auto-shift loop can move a piece
/// up to 32 cells in a single tick. So the anti-stall budget was spent at a rate set by
/// the player's ARR, not by their decisions — measured on a landed piece, the number of
/// direction changes allowed before a force-lock was 16 when tapping, 8 at ARR=50ms,
/// 5 at ARR=20ms and 3 at ARR=0. Picking competitive handling made pieces freeze three
/// times sooner, which is the opposite of what those settings are for.
///
/// V2 charges one reset per ACTION (a tap, or one held auto-shift slide however long),
/// so the budget means the same number of decisions at every handling setting.
/// </summary>
public class LockResetTests
{
    /// <summary>
    /// Drops the piece onto the floor, then alternates LEFT/RIGHT holds and counts how
    /// many direction changes land before the piece force-locks. Each direction is held
    /// long enough for DAS to charge and ARR to slide the piece, which is exactly the
    /// input shape that used to drain the budget.
    /// </summary>
    private static int DirectionChangesBeforeLock(double das, double arr, RulesVersion rules,
        GameModeId mode = GameModeId.Zen, int holdTicks = 14)
    {
        var cfg = GameMode.ById(mode).Config.With(das: das, arr: arr, rules: rules);
        var g = Game.Create(mode, 12345, cfg);
        var proc = new InputProcessor(g.Config);
        g.Start();

        // Park the piece on the floor without locking it (hard drop would lock).
        while (!g.Board.IsLanded(g.Current!))
        {
            proc.Step(Buttons.Soft, g);
            g.Update(Sim.TickDt);
        }

        int startPieces = g.Stats.PiecesPlaced;
        int changes = 0;
        var dir = Buttons.Left;
        for (int burst = 0; burst < 200; burst++)
        {
            dir = dir == Buttons.Left ? Buttons.Right : Buttons.Left;
            changes++;
            for (int t = 0; t < holdTicks; t++)
            {
                proc.Step(dir, g);
                g.Update(Sim.TickDt);
                if (g.Stats.PiecesPlaced != startPieces) return changes;
            }
        }
        return changes;
    }

    [Theory]
    [InlineData(0.133, 0.05)]  // DAS 133 / ARR 50
    [InlineData(0.133, 0.02)]  // DAS 133 / ARR 20 (shipping default)
    [InlineData(0.133, 0.0)]   // DAS 133 / ARR 0  (competitive, slide-to-wall)
    [InlineData(0.1, 0.0167)]  // DAS 100 / ARR 1 frame
    [InlineData(0.2, 0.1)]     // slow handling
    public void LockResetBudget_IsIndependentOfArr_UnderV2(double das, double arr)
    {
        // Zen's MaxLockResets is the default 15, so every handling setting must survive
        // exactly 15 direction changes before the force-lock.
        int changes = DirectionChangesBeforeLock(das, arr, RulesVersion.V2);
        Assert.Equal(GameConfig.Default.MaxLockResets, changes);
    }

    [Fact]
    public void LockResetBudget_VariedWithArr_UnderV1()
    {
        // The bug, pinned so the legacy branch keeps reproducing it for old replays:
        // faster auto-repeat used to buy FEWER decisions, not more.
        int slow = DirectionChangesBeforeLock(0.133, 0.05, RulesVersion.V1);
        int fast = DirectionChangesBeforeLock(0.133, 0.0, RulesVersion.V1);
        Assert.True(fast < slow,
            $"v1 must stay ARR-sensitive for replay fidelity (ARR=0 gave {fast}, ARR=50ms gave {slow})");
    }

    [Fact]
    public void MaxLockResets_GrantsExactlyTheConfiguredCount_UnderV2()
    {
        // Off-by-one: Game.Update compared '_lockResets > MaxLockResets', so a budget of
        // 15 actually handed out 16 resets. V2 compares '>=' and grants exactly 15.
        Assert.Equal(15, GameConfig.Default.MaxLockResets);
        Assert.Equal(15, DirectionChangesBeforeLock(0.133, 0.02, RulesVersion.V2));
    }

    [Fact]
    public void MaxLockResets_StillGrantsTheExtraReset_UnderV1()
    {
        Assert.Equal(16, DirectionChangesBeforeLock(0.133, 0.02, RulesVersion.V1, holdTicks: 3));
    }

    [Fact]
    public void MasterMode_TighterBudget_IsAlsoArrIndependent()
    {
        // Master sets MaxLockResets = 8. The playtest measured it collapsing to TWO usable
        // direction changes at 20G with default handling; it must now be the full 8 at any ARR.
        int budget = GameMode.ById(GameModeId.Master).Config.MaxLockResets;
        Assert.Equal(8, budget);
        Assert.Equal(budget, DirectionChangesBeforeLock(0.133, 0.02, RulesVersion.V2, GameModeId.Master));
        Assert.Equal(budget, DirectionChangesBeforeLock(0.133, 0.0, RulesVersion.V2, GameModeId.Master));
    }

    /// <summary>
    /// Slides a landed piece wall-to-wall <paramref name="traversals"/> times at instant
    /// ARR and reports whether it survived. Each traversal is ONE press but crosses many
    /// cells — precisely the input that the per-cell accounting over-charged.
    /// </summary>
    private static bool SurvivesWallToWallSlides(RulesVersion rules, int traversals)
    {
        var cfg = GameMode.ById(GameModeId.Zen).Config.With(das: 0.05, arr: 0.0, rules: rules);
        var g = Game.Create(GameModeId.Zen, 777, cfg);
        var proc = new InputProcessor(g.Config);
        g.Start();
        while (!g.Board.IsLanded(g.Current!)) { proc.Step(Buttons.Soft, g); g.Update(Sim.TickDt); }

        int startPieces = g.Stats.PiecesPlaced;
        var dir = Buttons.Left;
        for (int i = 0; i < traversals; i++)
        {
            dir = dir == Buttons.Left ? Buttons.Right : Buttons.Left;
            for (int t = 0; t < 8; t++) // long enough to charge DAS and reach the wall
            {
                proc.Step(dir, g);
                g.Update(Sim.TickDt);
                if (g.Stats.PiecesPlaced != startPieces) return false;
            }
        }
        return g.Stats.PiecesPlaced == startPieces;
    }

    [Fact]
    public void HeldSlide_CostsOneReset_NotOnePerCell()
    {
        // The heart of the fix. Seven wall-to-wall slides are seven decisions — a third of
        // the 15-reset budget — so the piece must still be alive. Under v1 the same seven
        // presses crossed ~50 cells and force-locked the piece less than halfway through.
        Assert.True(SurvivesWallToWallSlides(RulesVersion.V2, traversals: 7),
            "v2: seven held slides must cost seven resets, not fifty");
        Assert.False(SurvivesWallToWallSlides(RulesVersion.V1, traversals: 7),
            "v1 must stay per-cell for replay fidelity");
    }

    [Fact]
    public void RotationsStillCountAsActions()
    {
        // Rotations are edge-triggered presses with no auto-repeat form, so each one is a
        // fresh action and must keep spending the budget — otherwise infinite spin-stalling.
        var cfg = GameMode.ById(GameModeId.Zen).Config.With(rules: RulesVersion.V2);
        var g = Game.Create(GameModeId.Zen, 4, cfg);
        var proc = new InputProcessor(g.Config);
        g.Start();
        while (!g.Board.IsLanded(g.Current!)) { proc.Step(Buttons.Soft, g); g.Update(Sim.TickDt); }

        int startPieces = g.Stats.PiecesPlaced;
        int rotations = 0;
        // Spin in place well past the budget; the piece must eventually force-lock.
        for (int t = 0; t < 400 && g.Stats.PiecesPlaced == startPieces; t++)
        {
            proc.Step(Buttons.RotateCw, g);
            g.Update(Sim.TickDt);
            rotations++;
        }
        Assert.NotEqual(startPieces, g.Stats.PiecesPlaced);
        // Bounded by the budget plus a little slack: a kick can lift the piece clear of the
        // floor for a tick, and a rotation while airborne legitimately spends nothing.
        Assert.True(rotations <= GameConfig.Default.MaxLockResets * 2,
            $"spin-stall must be bounded by the reset budget, took {rotations} rotations");
    }
}
