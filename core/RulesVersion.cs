namespace Blockfall.Core;

/// <summary>
/// The generation of SIMULATION RULES a run is played — or replayed — under.
///
/// The determinism contract (same seed + same per-tick <see cref="Buttons"/> stream
/// == bit-identical result) is only meaningful relative to a fixed rule set. When a
/// rule that alters the outcome of an input stream has to change, we do NOT silently
/// re-interpret history: we mint a new <see cref="RulesVersion"/>, keep the old
/// branch alive forever, and stamp every recording with the version it was played
/// under (<see cref="ReplayData.Version"/>). A replay recorded in 2026 must still
/// re-simulate bit-identically in 2030, on the rules that were live when a human
/// actually pressed those buttons — that is what makes a saved score, a ghost, and
/// an anti-cheat re-sim trustworthy.
///
/// Adding a member here is a deliberate, reviewed act. It must be accompanied by:
///   * a bump of <see cref="ReplayData.CurrentVersion"/>,
///   * a mapping in <see cref="SimRules.ForReplayVersion"/>,
///   * a golden-fixture regression test that pins the OLD version's output.
/// </summary>
public enum RulesVersion
{
    /// <summary>
    /// Shipped v1.0 – v1.4.x. Known quirks, preserved verbatim for old recordings:
    ///   * A lock-delay reset was consumed per CELL moved, so auto-shift (ARR) burned
    ///     the reset budget several times faster than tapping — hardcore handling
    ///     settings force-locked pieces ~3x sooner.
    ///   * <c>MaxLockResets</c> was compared with <c>&gt;</c>, granting N+1 resets.
    ///   * DAS/ARR thresholds were compared against a float-accumulated timer, so
    ///     round millisecond values (100 ms, 200 ms) charged one tick late.
    ///   * The 180° kick table nudged up to two columns sideways with no diagonal
    ///     tests, so ~0.7% of 180 spins landed two columns from where the player aimed.
    /// </summary>
    V1 = 1,

    /// <summary>
    /// v1.5+. Lock resets are counted per ACTION (a tap or one auto-shift slide) so
    /// lock delay is independent of the DAS/ARR a player chose; <c>MaxLockResets</c>
    /// grants exactly N; DAS/ARR are quantised to whole ticks so the configured
    /// milliseconds are the milliseconds you get; the 180° kick table is the community
    /// standard set (max one column of lateral displacement, diagonal tests included).
    /// </summary>
    V2 = 2,
}

/// <summary>
/// Rule-set selection helpers. Kept separate from <see cref="RulesVersion"/> so the
/// enum stays pure data (it is serialized indirectly via the replay version number).
/// </summary>
public static class SimRules
{
    /// <summary>The rule set every NEW run is played under.</summary>
    public const RulesVersion Current = RulesVersion.V2;

    /// <summary>
    /// Maps a <see cref="ReplayData.Version"/> onto the rules that recording was made
    /// with. Unknown FUTURE versions clamp to <see cref="Current"/> rather than throwing:
    /// a replay from a newer build will simply mis-simulate and fail validation, which is
    /// the correct, non-crashing outcome for a share code pasted from a future release.
    /// </summary>
    public static RulesVersion ForReplayVersion(int replayVersion)
        => replayVersion <= 1 ? RulesVersion.V1 : Current;
}
