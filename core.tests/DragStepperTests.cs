using System.Collections.Generic;
using Blockfall.Core;
using Xunit;

namespace Blockfall.Core.Tests;

/// <summary>
/// The touch-drag control depends on one guarantee: a finger that crosses N cells
/// must produce EXACTLY N single-cell moves — never fewer (DAS swallowing steps) nor
/// more (an accidental slide). These tests pin that down through the real
/// InputProcessor + Game, the same path replays and netcode run.
/// </summary>
public class DragStepperTests
{
    [Fact]
    public void Next_EmitsOneStepPerCell_WithGapsBetween()
    {
        var s = new DragStepper();
        s.Queue(3); // finger crossed 3 cells to the right

        var seq = new List<Buttons>();
        for (int i = 0; i < 8; i++) seq.Add(s.Next());

        // move, gap, move, gap, move, then idle — exactly 3 Right pulses, each isolated.
        Assert.Equal(Buttons.Right, seq[0]);
        Assert.Equal(Buttons.None, seq[1]);
        Assert.Equal(Buttons.Right, seq[2]);
        Assert.Equal(Buttons.None, seq[3]);
        Assert.Equal(Buttons.Right, seq[4]);
        Assert.Equal(Buttons.None, seq[5]);
        Assert.Equal(Buttons.None, seq[6]);
        Assert.False(s.HasPending);
    }

    [Fact]
    public void Next_LeftIsNegative_AndClearDropsPending()
    {
        var s = new DragStepper();
        s.Queue(-2);
        Assert.Equal(Buttons.Left, s.Next());
        Assert.True(s.HasPending);
        s.Clear();
        Assert.False(s.HasPending);
        Assert.Equal(Buttons.None, s.Next());
    }

    [Fact]
    public void QueuedDrag_MovesPieceExactlyThatManyColumns()
    {
        foreach (int cells in new[] { 2, -2 })
        {
            var g = Game.Create(GameModeId.Zen, 5);
            var p = new InputProcessor(g.Config);
            g.Start();
            int startCol = g.Current!.Origin.Col;

            var stepper = new DragStepper();
            stepper.Queue(cells);
            // Run more ticks than the drain needs: it must not over-shoot once drained.
            for (int i = 0; i < 12 && g.Status == GameStatus.Running; i++)
            {
                p.Step(stepper.Next(), g);
                g.Update(Sim.TickDt);
            }
            Assert.Equal(startCol + cells, g.Current!.Origin.Col);
        }
    }

    [Fact]
    public void ForceGap_DelaysNextStepByOneTick_NeverLosesIt()
    {
        // The sampler calls ForceGap on any tick another source holds a direction, so the
        // first drag step after that hold ends is still a fresh tap (one gap, then the steps).
        var s = new DragStepper();
        s.Queue(2);
        s.ForceGap();
        Assert.Equal(Buttons.None, s.Next());   // forced release gap
        Assert.Equal(Buttons.Right, s.Next());  // step 1 (not lost)
        Assert.Equal(Buttons.None, s.Next());   // internal gap
        Assert.Equal(Buttons.Right, s.Next());  // step 2
        Assert.False(s.HasPending);
    }

    [Fact]
    public void GapTick_IsLoadBearing_TwoAdjacentBitsMoveOnlyOnce()
    {
        // Contrast case proving WHY DragStepper injects a gap: two Right bits on
        // adjacent ticks (no gap) read as one held press — the 2nd charges DAS, not a step.
        var g = Game.Create(GameModeId.Zen, 5);
        var p = new InputProcessor(g.Config);
        g.Start();
        int startCol = g.Current!.Origin.Col;

        p.Step(Buttons.Right, g); g.Update(Sim.TickDt);
        p.Step(Buttons.Right, g); g.Update(Sim.TickDt); // held, not a fresh tap
        Assert.Equal(startCol + 1, g.Current!.Origin.Col); // only ONE cell — hence the gap
    }
}
