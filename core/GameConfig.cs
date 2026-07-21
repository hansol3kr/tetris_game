namespace Blockfall.Core;

/// <summary>
/// All timing/tuning knobs in one place so designers can rebalance without
/// touching game logic. Times are in seconds. These defaults follow modern
/// guideline feel; every field is overridable per mode or per player setting.
/// </summary>
public sealed class GameConfig
{
    // --- Gravity -----------------------------------------------------------
    /// <summary>Seconds a piece takes to fall one cell at level 1 (before soft drop).</summary>
    public double BaseGravity { get; init; } = 1.0;

    /// <summary>Soft-drop multiplier: piece falls this many times faster while soft-dropping.</summary>
    public double SoftDropFactor { get; init; } = 20.0;

    /// <summary>Level at which gravity effectively becomes instant (20G) in some modes. 0 = never.</summary>
    public int MaxGravityLevel { get; init; } = 15;

    // --- Lock delay --------------------------------------------------------
    /// <summary>Seconds a landed piece waits before locking.</summary>
    public double LockDelay { get; init; } = 0.5;

    /// <summary>Max number of move/rotate resets before the piece force-locks (anti-stall).</summary>
    public int MaxLockResets { get; init; } = 15;

    // --- Horizontal auto-shift (input feel; consumed by the presentation layer) ---
    /// <summary>Delayed Auto Shift: hold time before a held direction starts repeating.</summary>
    public double Das { get; init; } = 0.133;

    /// <summary>Auto Repeat Rate: time between repeats once DAS engages (0 = instant to wall).</summary>
    public double Arr { get; init; } = 0.02;

    // --- Spawn / handling --------------------------------------------------
    /// <summary>Entry delay (ARE) between locking a piece and the next one spawning.</summary>
    public double SpawnDelay { get; init; } = 0.0;

    /// <summary>How many next pieces to expose to the UI preview.</summary>
    public int PreviewCount { get; init; } = 5;

    /// <summary>Allow the Hold slot.</summary>
    public bool HoldEnabled { get; init; } = true;

    /// <summary>Enable the ghost/shadow projection.</summary>
    public bool GhostEnabled { get; init; } = true;

    /// <summary>Reward immobile spins of ANY piece, not just T ("all-spin").</summary>
    public bool AllSpin { get; init; }

    // --- Scoring -------------------------------------------------------------
    /// <summary>
    /// Multiplier applied once to each lock's scoring gain (line clears, spins,
    /// combos, perfect clears). Used by Descent charms to trade risk for reward.
    /// Deliberately NOT applied to soft/hard-drop points so drop feedback stays
    /// immediate and readable. 1.0 keeps legacy modes bit-identical.
    /// </summary>
    public double ScoreMultiplier { get; init; } = 1.0;

    /// <summary>
    /// Gravity (seconds per cell) for a given level using the classic guideline curve:
    ///     time = (0.8 - (level-1)*0.007) ^ (level-1)
    /// Clamped so it never goes below a single-frame floor.
    /// </summary>
    public double GravityForLevel(int level)
    {
        if (level < 1) level = 1;
        if (MaxGravityLevel > 0 && level >= MaxGravityLevel) return 1.0 / 60.0; // ~20G feel
        double baseVal = 0.8 - (level - 1) * 0.007;
        if (baseVal <= 0) return 1.0 / 60.0;
        // Scale the guideline curve by BaseGravity so the configurable knob actually
        // applies (level 1 == BaseGravity, since 0.8^0 == 1). Default 1.0 is unchanged.
        double seconds = BaseGravity * Math.Pow(baseVal, level - 1);
        return Math.Max(seconds, 1.0 / 60.0);
    }

    /// <summary>
    /// Returns a copy of this config with the given player-tunable handling knobs
    /// overridden (null keeps the current value). Used to apply per-player settings
    /// (DAS/ARR/ghost) on top of a mode's base config without mutating the preset.
    /// </summary>
    public GameConfig With(double? das = null, double? arr = null, bool? ghost = null,
        bool? hold = null, double? baseGravity = null, int? previewCount = null,
        double? lockDelay = null, int? maxGravityLevel = null, bool? allSpin = null,
        double? scoreMultiplier = null)
        => new()
        {
            BaseGravity = baseGravity ?? BaseGravity,
            SoftDropFactor = SoftDropFactor,
            MaxGravityLevel = maxGravityLevel ?? MaxGravityLevel,
            LockDelay = lockDelay ?? LockDelay,
            MaxLockResets = MaxLockResets,
            Das = das ?? Das,
            Arr = arr ?? Arr,
            SpawnDelay = SpawnDelay,
            PreviewCount = previewCount ?? PreviewCount,
            HoldEnabled = hold ?? HoldEnabled,
            GhostEnabled = ghost ?? GhostEnabled,
            AllSpin = allSpin ?? AllSpin,
            ScoreMultiplier = scoreMultiplier ?? ScoreMultiplier,
        };

    public static GameConfig Default => new();
}
