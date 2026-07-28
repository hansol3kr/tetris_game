namespace Blockfall.Core;

/// <summary>
/// Fixed-timestep constants for the deterministic simulation. All replay and
/// netcode runs advance the game in whole ticks of <see cref="TickDt"/> seconds
/// so behaviour is frame-rate independent and identical across machines.
/// </summary>
public static class Sim
{
    public const int TickHz = 60;
    public const double TickDt = 1.0 / TickHz;

    /// <summary>
    /// Converts a duration in seconds to a whole number of ticks, rounding to the
    /// NEAREST tick.
    ///
    /// Why this exists: accumulating <see cref="TickDt"/> and comparing against a
    /// seconds threshold is not exact in binary floating point — adding 1/60 six
    /// times yields 0.09999999999999999, which fails a <c>&gt;= 0.1</c> test and
    /// silently costs one extra frame. Timings that only ever exist as whole ticks
    /// must be resolved to ticks ONCE, up front, not rediscovered by summation.
    ///
    /// Nearest (not ceiling) because the tick grid is coarse at 60 Hz: ceiling would
    /// turn a requested 20 ms into 33.3 ms — a change the hand feels immediately —
    /// whereas nearest lands on 16.7 ms, the achievable value closest to intent.
    /// The UI is expected to display the achievable frame count alongside the
    /// millisecond value so the number a player reads is the number they get.
    /// </summary>
    public static int TicksFor(double seconds)
        => seconds <= 0 ? 0 : (int)Math.Round(seconds * TickHz, MidpointRounding.AwayFromZero);
}
