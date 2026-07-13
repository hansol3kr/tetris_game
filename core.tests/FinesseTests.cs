using System.Collections.Generic;
using Blockfall.Core;
using Xunit;

namespace Blockfall.Core.Tests;

/// <summary>Verifies the finesse SOLVER: minimum keypresses to reach a placement on an empty well.</summary>
public class FinesseSolverTests
{
    // Reference minimums (assert against the BFS, which is the source of truth).
    [Theory]
    [InlineData(PieceType.T, RotationState.Spawn, 3, 0)] // spawn column, just drop
    [InlineData(PieceType.T, RotationState.Spawn, 0, 1)] // DAS left
    [InlineData(PieceType.T, RotationState.Spawn, 1, 2)] // DAS + tapback (or two taps)
    [InlineData(PieceType.T, RotationState.Two, 3, 1)]   // one 180, NOT two CWs
    [InlineData(PieceType.T, RotationState.Right, 8, 2)] // rotate CW, DAS right
    [InlineData(PieceType.I, RotationState.Spawn, 0, 1)] // flat I, DAS left
    [InlineData(PieceType.I, RotationState.Spawn, 3, 0)] // flat I at spawn
    [InlineData(PieceType.I, RotationState.Right, 0, 2)] // vertical I, left wall
    [InlineData(PieceType.I, RotationState.Right, 9, 2)] // vertical I, right wall
    [InlineData(PieceType.O, RotationState.Spawn, 4, 0)] // O at spawn
    [InlineData(PieceType.O, RotationState.Spawn, 0, 1)] // O, DAS left
    [InlineData(PieceType.J, RotationState.Right, 1, 2)] // corrected: DAS left then rotate CW
    [InlineData(PieceType.J, RotationState.Left, 1, 3)]  // genuine 3-press worst case
    public void MinPresses_MatchesReference(PieceType type, RotationState state, int leftCol, int expected)
        => Assert.Equal(expected, FinesseSolver.MinPresses(type, state, leftCol));

    [Fact]
    public void Canonical_O_AllOrientationsCollapseToSpawn()
    {
        // Rotating an O never helps, so every state at leftCol 4 costs the same as spawn (0).
        Assert.Equal(0, FinesseSolver.MinPresses(PieceType.O, RotationState.Two, 4));
        Assert.Equal(0, FinesseSolver.MinPresses(PieceType.O, RotationState.Right, 4));
        Assert.Equal(0, FinesseSolver.MinPresses(PieceType.O, RotationState.Left, 4));
    }

    [Fact]
    public void Canonical_I_FlatTwoEqualsSpawn()
        => Assert.Equal(
            FinesseSolver.MinPresses(PieceType.I, RotationState.Spawn, 3),
            FinesseSolver.MinPresses(PieceType.I, RotationState.Two, 3));

    [Theory]
    [InlineData(PieceType.S)]
    [InlineData(PieceType.Z)]
    public void Canonical_SZ_FlatTwoEqualsSpawn(PieceType type)
    {
        for (int leftCol = 0; leftCol <= 7; leftCol++)
            Assert.Equal(
                FinesseSolver.MinPresses(type, RotationState.Spawn, leftCol),
                FinesseSolver.MinPresses(type, RotationState.Two, leftCol));
    }

    [Fact]
    public void EveryInBoundsPlacement_IsFinite_AndAtMost3()
    {
        foreach (PieceType type in new[] { PieceType.I, PieceType.O, PieceType.T, PieceType.S, PieceType.Z, PieceType.J, PieceType.L })
            foreach (RotationState state in new[] { RotationState.Spawn, RotationState.Right, RotationState.Two, RotationState.Left })
            {
                int foot = FinesseSolver.MaxColOffset(type, state) - FinesseSolver.MinColOffset(type, state) + 1;
                for (int leftCol = 0; leftCol <= Board.DefaultWidth - foot; leftCol++)
                {
                    int mp = FinesseSolver.MinPresses(type, state, leftCol);
                    Assert.InRange(mp, 0, 3);
                }
            }
    }
}

/// <summary>Verifies the FinesseTracker: input counting, fault grading, streaks, rank, and milestones.</summary>
public class FinesseTrackerTests
{
    // ---- helpers -----------------------------------------------------------
    private static FinesseResult PlaceCleanTo(FinesseTracker t, int col)
    {
        t.BeginPiece(PieceType.T, 3, RotationState.Spawn);
        return t.Finalize(col, RotationState.Spawn, false);
    }

    private static void Clean(FinesseTracker t) => PlaceCleanTo(t, 3);        // 0 presses, min 0
    private static void Faulty(FinesseTracker t)
    {
        t.BeginPiece(PieceType.T, 3, RotationState.Spawn);
        t.OnTapMove(-1);
        t.OnTapMove(1);
        t.Finalize(3, RotationState.Spawn, false); // 2 presses, min 0 -> 2 faults
    }
    private static void Spin(FinesseTracker t)
    {
        t.BeginPiece(PieceType.T, 3, RotationState.Spawn);
        t.Finalize(3, RotationState.Spawn, true);
    }

    [Fact]
    public void TapThenSameDas_CountsAsOneInput()
    {
        var t = new FinesseTracker();
        t.BeginPiece(PieceType.T, 3, RotationState.Spawn);
        t.OnTapMove(-1);
        t.OnDasMove(-1);                     // absorbed
        var r = t.Finalize(0, RotationState.Spawn, false); // leftCol 0, min 1
        Assert.Equal(1, r.ActualPresses);
        Assert.True(r.Clean);
    }

    [Fact]
    public void LoneTap_CountsAsOne()
    {
        var t = new FinesseTracker();
        t.BeginPiece(PieceType.T, 3, RotationState.Spawn);
        t.OnTapMove(-1);
        var r = t.Finalize(2, RotationState.Spawn, false); // leftCol 2, min 1
        Assert.Equal(1, r.ActualPresses);
        Assert.True(r.Clean);
    }

    [Fact]
    public void ThreeTaps_ToOnePressTarget_IsTwoFaults()
    {
        var t = new FinesseTracker();
        t.BeginPiece(PieceType.T, 3, RotationState.Spawn);
        t.OnTapMove(-1);
        t.OnTapMove(-1);
        t.OnTapMove(1);
        var r = t.Finalize(2, RotationState.Spawn, false); // leftCol 2, min 1, actual 3
        Assert.Equal(2, r.Faults);
    }

    [Fact]
    public void TapThenOppositeDas_CountsAsTwo()
    {
        var t = new FinesseTracker();
        t.BeginPiece(PieceType.T, 3, RotationState.Spawn);
        t.OnTapMove(-1);
        t.OnDasMove(1);                      // opposite direction, not absorbed
        var r = t.Finalize(7, RotationState.Spawn, false);
        Assert.Equal(2, r.ActualPresses);
    }

    [Fact]
    public void RotateBetweenTapAndDas_StillAbsorbsTheHold()
    {
        // Regression: a rotation mid-hold must NOT break the tap->DAS absorb.
        var t = new FinesseTracker();
        t.BeginPiece(PieceType.J, 3, RotationState.Spawn);
        t.OnTapMove(-1);  // initiate a left hold
        t.OnRotate();     // rotate while still holding left
        t.OnDasMove(-1);  // the same hold charges to the wall -> absorbed
        var r = t.Finalize(0, RotationState.Right, false); // J-Right at the left wall, min 2
        Assert.Equal(2, r.ActualPresses); // one move (tap+DAS) + one rotate
        Assert.True(r.Clean);
    }

    [Fact]
    public void CarriedDas_NoPrecedingTap_CountsAsOne()
    {
        // Models Sync crediting a DAS held across a lock->spawn boundary.
        var t = new FinesseTracker();
        t.BeginPiece(PieceType.T, 3, RotationState.Spawn);
        t.OnDasMove(-1);  // carried, already-charged DAS: no preceding tap
        var r = t.Finalize(0, RotationState.Spawn, false); // left wall, min 1
        Assert.Equal(1, r.ActualPresses);
        Assert.True(r.Clean);
    }

    [Fact]
    public void CarriedDasThenCorrection_ChargesTheRealFault()
    {
        var t = new FinesseTracker();
        t.BeginPiece(PieceType.T, 3, RotationState.Spawn);
        t.OnDasMove(1);   // carried DAS to the right wall (1)
        t.OnTapMove(-1);  // then correct back (2)
        t.OnTapMove(-1);  // (3)
        var r = t.Finalize(5, RotationState.Spawn, false); // leftCol 5, min 2
        Assert.Equal(3, r.ActualPresses);
        Assert.Equal(1, r.Faults);
    }

    [Fact]
    public void CarriedSoftDropThenMove_IsUnscored()
    {
        // Models Sync arming the tuck rule when soft drop is held across the boundary.
        var t = new FinesseTracker();
        t.BeginPiece(PieceType.T, 3, RotationState.Spawn);
        t.OnSoftDrop();   // carried soft drop, armed at piece start
        t.OnTapMove(-1);
        var r = t.Finalize(2, RotationState.Spawn, false);
        Assert.False(r.Scored);
    }

    [Fact]
    public void WastedO_Rotation_IsAFault()
    {
        var t = new FinesseTracker();
        t.BeginPiece(PieceType.O, 4, RotationState.Spawn);
        t.OnRotate();
        var r = t.Finalize(4, RotationState.Right, false); // rotating O never helps
        Assert.True(r.Scored);
        Assert.Equal(1, r.Faults);
    }

    [Fact]
    public void HoldDiscard_LeavesCountersUntouched_AndResetsPiece()
    {
        var t = new FinesseTracker();
        t.BeginPiece(PieceType.T, 3, RotationState.Spawn);
        t.OnTapMove(-1);                       // in-flight input...
        t.BeginPiece(PieceType.J, 3, RotationState.Spawn); // ...then swapped out by Hold
        var r = t.Finalize(3, RotationState.Spawn, false); // J placed clean at spawn
        Assert.True(r.Clean);                  // the abandoned tap did NOT carry over
        Assert.Equal(1, t.ScoredPieces);
        Assert.Equal(0, t.UnscoredPieces);
    }

    [Fact]
    public void Spin_IsUnscored_StreakNeutral()
    {
        var t = new FinesseTracker();
        Clean(t);                              // streak 1
        Spin(t);                               // unscored
        Assert.Equal(1, t.ScoredPieces);
        Assert.Equal(1, t.UnscoredPieces);
        Assert.Equal(1, t.CleanStreak);        // streak untouched by the spin
    }

    [Fact]
    public void Tuck_SoftDropThenMove_IsUnscored()
    {
        var t = new FinesseTracker();
        t.BeginPiece(PieceType.T, 3, RotationState.Spawn);
        t.OnSoftDrop();
        t.OnTapMove(-1);
        var r = t.Finalize(2, RotationState.Spawn, false);
        Assert.False(r.Scored);
        Assert.Equal(1, t.UnscoredPieces);
    }

    [Fact]
    public void FewerPressesThanMin_ClampsToZeroFaults()
    {
        // A DAS that terrain-stopped short: 1 actual press for a min-2 column -> never a false fault.
        var t = new FinesseTracker();
        t.BeginPiece(PieceType.T, 3, RotationState.Spawn);
        t.OnTapMove(-1);
        t.OnDasMove(-1);                       // absorbed -> actual 1
        var r = t.Finalize(1, RotationState.Spawn, false); // leftCol 1, min 2
        Assert.Equal(0, r.Faults);
        Assert.True(r.Clean);
    }

    [Fact]
    public void NaturalLock_SoftDropToRest_NoMove_IsCleanScored()
    {
        var t = new FinesseTracker();
        t.BeginPiece(PieceType.T, 3, RotationState.Spawn);
        t.OnSoftDrop();                        // soft drop for speed, no move after
        var r = t.Finalize(3, RotationState.Spawn, false);
        Assert.True(r.Scored);
        Assert.True(r.Clean);
    }

    [Fact]
    public void Accumulation_TracksPercentStreakAndBest()
    {
        var t = new FinesseTracker();
        Clean(t);   // streak 1
        Faulty(t);  // streak 0
        Clean(t);   // streak 1
        Clean(t);   // streak 2
        Spin(t);    // unscored, streak stays 2
        Clean(t);   // streak 3

        Assert.Equal(5, t.ScoredPieces);
        Assert.Equal(4, t.CleanPieces);
        Assert.Equal(1, t.UnscoredPieces);
        Assert.Equal(80.0, t.FinessePercent, 6);
        Assert.Equal(3, t.CleanStreak);
        Assert.Equal(3, t.BestCleanStreak);
    }

    [Fact]
    public void Milestone_FiresOnceAtTenCleanInARow()
    {
        var t = new FinesseTracker();
        var fired = new List<int>();
        t.StreakMilestone += n => fired.Add(n);
        for (int i = 0; i < 11; i++) Clean(t); // 10th triggers, 11th does not
        Assert.Single(fired);
        Assert.Equal(10, fired[0]);
    }

    [Fact]
    public void Rank_IsNoneUntilTenPieces_ThenGradesByPercent()
    {
        var t = new FinesseTracker();
        for (int i = 0; i < 5; i++) Clean(t);
        Assert.Equal(FinesseRank.None, t.Rank); // too few to rate

        for (int i = 0; i < 7; i++) Clean(t);   // now 12 scored, all clean -> 100%
        Assert.Equal(FinesseRank.S, t.Rank);
    }

    [Fact]
    public void Reset_ZeroesEverything()
    {
        var t = new FinesseTracker();
        Faulty(t);
        Clean(t);
        t.Reset();
        Assert.Equal(0, t.ScoredPieces);
        Assert.Equal(0, t.TotalFaults);
        Assert.Equal(0, t.BestCleanStreak);
        Assert.Equal(100.0, t.FinessePercent, 6);
    }
}

/// <summary>The INVISIBLE modifier is presentation-only and must not perturb the engine config.</summary>
public class InvisibleModifierTests
{
    [Fact]
    public void Invisible_LeavesConfigIdentical()
    {
        var cfg = GameConfig.Default;
        var result = ModifierSet.Apply(cfg, new[] { GameModifier.Invisible });
        Assert.Equal(cfg.HoldEnabled, result.HoldEnabled);
        Assert.Equal(cfg.GhostEnabled, result.GhostEnabled);
        Assert.Equal(cfg.BaseGravity, result.BaseGravity, 6);
        Assert.Equal(cfg.LockDelay, result.LockDelay, 6);
        Assert.Equal(cfg.AllSpin, result.AllSpin);
    }

    [Fact]
    public void Invisible_HasALabel()
        => Assert.Equal("INVISIBLE", ModifierSet.Label(GameModifier.Invisible));
}
