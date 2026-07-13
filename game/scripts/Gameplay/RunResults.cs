using Blockfall.Core;

namespace Blockfall.Gameplay;

/// <summary>Immutable summary of a finished run, passed to the results screen + leaderboards.</summary>
public readonly struct RunResults
{
    public GameModeId Mode { get; init; }
    public bool Completed { get; init; } // true = goal reached, false = topped out
    public long Score { get; init; }
    public int Lines { get; init; }
    public int Level { get; init; }
    public double Time { get; init; }
    public RunStats Stats { get; init; }
    public long GarbageSent { get; init; }
    public bool IsNewBest { get; init; }

    /// <summary>The seed this run played, so it can be replayed or shared.</summary>
    public ulong Seed { get; init; }

    /// <summary>Modifiers that were active (for an exact replay).</summary>
    public GameModifier[] Modifiers { get; init; }

    /// <summary>Finesse (input-economy) performance for this run.</summary>
    public FinesseSummary Finesse { get; init; }

    /// <summary>The recorded input stream for exact playback, or null if this run isn't replayable (e.g. revived).</summary>
    public ReplayData? Replay { get; init; }

    /// <summary>Ids of achievements unlocked by this run (for the results toast). Never null.</summary>
    public string[] UnlockedAchievements { get; init; }

    /// <summary>The headline number for this mode (time for Sprint, score otherwise).</summary>
    public double PrimaryMetric => Mode == GameModeId.Sprint ? Time : Score;
}
