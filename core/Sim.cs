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
}
