using Blockfall.Core;
using Xunit;

namespace Blockfall.Core.Tests;

/// <summary>
/// DAS / ARR timing resolution.
///
/// Original bug (shipped v1.0–v1.4): <c>InputProcessor</c> accumulated
/// <c>Sim.TickDt</c> into a double and compared it against the configured seconds.
/// Adding 1/60 six times yields 0.09999999999999999, which fails a <c>&gt;= 0.1</c>
/// test, so a 100 ms DAS actually charged at 116.7 ms and a 200 ms DAS at 216.7 ms —
/// one frame late, on exactly the two round values players pick most often. 133 ms and
/// 150 ms happened to land clean, which is why it went unnoticed.
///
/// V2 resolves the threshold to whole ticks ONCE, up front (<see cref="Sim.TicksFor"/>).
/// </summary>
public class HandlingTimingTests
{
    /// <summary>
    /// Number of ticks a held direction takes to produce its SECOND cell of movement.
    /// Tick 1 is the initial tap; the next movement is the first auto-shift step, so this
    /// measures when DAS actually engaged. Run with instant ARR so the moment DAS charges
    /// is the moment the piece moves again — no ARR interval mixed into the reading.
    /// </summary>
    private static int TicksUntilAutoShift(double das, RulesVersion rules)
    {
        var cfg = GameMode.ById(GameModeId.Zen).Config.With(das: das, arr: 0.0, rules: rules);
        var g = Game.Create(GameModeId.Zen, 99, cfg);
        var proc = new InputProcessor(g.Config);
        g.Start();

        proc.Step(Buttons.Left, g);  // tick 1: the tap
        g.Update(Sim.TickDt);
        int col = g.Current!.Origin.Col;

        for (int t = 2; t < 200; t++)
        {
            proc.Step(Buttons.Left, g);
            g.Update(Sim.TickDt);
            if (g.Current!.Origin.Col != col) return t;
        }
        return -1;
    }

    [Theory]
    [InlineData(0.1, 7)]    // 100 ms: 6 ticks of charge, so movement resumes on tick 7
    [InlineData(0.133, 9)]  // 133 ms: 8 ticks (was already exact)
    [InlineData(0.15, 10)]  // 150 ms: 9 ticks (was already exact)
    [InlineData(0.2, 13)]   // 200 ms: 12 ticks
    public void DasCharges_OnTheConfiguredTick_UnderV2(double das, int expectedTick)
    {
        Assert.Equal(expectedTick, TicksUntilAutoShift(das, RulesVersion.V2));
    }

    [Theory]
    [InlineData(0.1, 6)]    // asked for 6 ticks, v1 charged on the 7th
    [InlineData(0.2, 12)]   // asked for 12 ticks, v1 charged on the 13th
    public void DasCharged_OneTickLate_UnderV1(double das, int requestedTicks)
    {
        // Pinned so the legacy branch keeps reproducing the late charge for old replays.
        Assert.Equal(requestedTicks, Sim.TicksFor(das));
        Assert.Equal(requestedTicks + 2, TicksUntilAutoShift(das, RulesVersion.V1));
    }

    [Fact]
    public void TicksFor_ResolvesRoundMillisecondValues_Exactly()
    {
        // The float-accumulation trap, isolated: naive summation of TickDt undershoots.
        double accum = 0;
        for (int i = 0; i < 6; i++) accum += Sim.TickDt;
        Assert.True(accum < 0.1, "the original bug: six ticks do not sum to 0.1 in binary FP");

        Assert.Equal(6, Sim.TicksFor(0.1));
        Assert.Equal(8, Sim.TicksFor(0.133));
        Assert.Equal(9, Sim.TicksFor(0.15));
        Assert.Equal(12, Sim.TicksFor(0.2));
        Assert.Equal(0, Sim.TicksFor(0));
        Assert.Equal(0, Sim.TicksFor(-1));
    }

    [Fact]
    public void TicksFor_RoundsToNearestFrame_NotUp()
    {
        // Nearest, because ceiling would turn the shipping 20 ms ARR default into 33.3 ms —
        // a change the hand feels instantly. 20 ms resolves to one frame (16.7 ms) instead.
        Assert.Equal(1, Sim.TicksFor(0.02));
        Assert.Equal(2, Sim.TicksFor(0.033));
        Assert.Equal(3, Sim.TicksFor(0.05));
    }

    [Fact]
    public void AutoShift_RepeatsOnAUniformCadence_UnderV2()
    {
        // Related bug: at ARR=20 ms the float remainder produced a 1-1-1-1-1-pause stutter
        // ("110111110111110") because 20 ms is not a multiple of 16.667 ms. Quantising to
        // whole ticks makes every gap between auto-shift steps identical.
        var cfg = GameMode.ById(GameModeId.Zen).Config.With(das: 0.05, arr: 0.05, rules: RulesVersion.V2);
        var g = Game.Create(GameModeId.Zen, 5, cfg);
        var proc = new InputProcessor(g.Config);
        g.Start();

        int lastCol = g.Current!.Origin.Col;
        int lastMoveTick = 0;
        var gaps = new System.Collections.Generic.List<int>();
        for (int t = 1; t <= 60; t++)
        {
            proc.Step(Buttons.Right, g);
            g.Update(Sim.TickDt);
            int col = g.Current!.Origin.Col;
            if (col != lastCol)
            {
                if (lastMoveTick > 0) gaps.Add(t - lastMoveTick);
                lastMoveTick = t;
                lastCol = col;
            }
            if (col == lastCol && gaps.Count >= 3) break;
        }

        Assert.True(gaps.Count >= 2, "expected several auto-shift steps to measure");
        // Skip gap[0]: it spans the DAS charge, not an ARR interval.
        for (int i = 1; i < gaps.Count; i++)
            Assert.Equal(gaps[1], gaps[i]);
    }

    [Fact]
    public void SubTickArr_StillAdvancesOneCellPerTick()
    {
        // A positive ARR below one frame must not divide by zero or stall; it clamps to the
        // fastest a fixed 60 Hz tick can express.
        var cfg = GameMode.ById(GameModeId.Zen).Config.With(das: 0.0, arr: 0.001, rules: RulesVersion.V2);
        var proc = new InputProcessor(cfg);
        var g = Game.Create(GameModeId.Zen, 6, cfg);
        g.Start();
        int startCol = g.Current!.Origin.Col;
        for (int t = 0; t < 5; t++) { proc.Step(Buttons.Left, g); g.Update(Sim.TickDt); }
        Assert.True(g.Current!.Origin.Col < startCol);
    }
}
