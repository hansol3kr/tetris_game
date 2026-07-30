namespace Blockfall.Core;

/// <summary>
/// Where advertising is allowed to appear. A FAIRNESS RULE, so it lives in core with tests rather
/// than as an <c>if</c> at each call site — CLAUDE.md §8-4 makes the ad cap inviolable, and a rule
/// that is only enforced where someone remembered to enforce it is not a rule.
///
/// <para>The rule this file exists for: <c>docs/MONETIZATION.md</c> requires ZEN to skip
/// interstitials entirely ("endless, relaxing … to protect the mode's intent"). That was written
/// but never implemented — <c>ResultsScreen</c> and <c>DescentResultsScreen</c> both called the ad
/// hook with no idea what mode had just been played, so a Zen run was as likely to be interrupted
/// as any other. It stayed invisible because the caller is the only place the mode is known and the
/// ad hook is the only place the policy is known, and nothing connected the two.</para>
///
/// <para>Pure and engine-free: no Godot types, no platform lookups, no state. The platform layer
/// still owns "does this device even serve ads", the frequency cap and the premium entitlement —
/// this answers only "is this mode eligible at all", which is the half that is a game-design
/// promise rather than a business one.</para>
/// </summary>
public static class AdPolicy
{
    /// <summary>
    /// True when a finished run in <paramref name="mode"/> may be followed by an interstitial.
    ///
    /// <para>Zen is the single exclusion, and it is deliberate rather than incidental: every other
    /// mode ends on a scoreboard the player is meant to react to, while Zen has no fail state and
    /// no target — the run ends when the player decides they are done, and an ad at that moment
    /// charges them for choosing to stop. A future no-pressure mode belongs here too; a mode being
    /// merely LONG (Marathon, Descent) does not.</para>
    ///
    /// <para>Callers must consult this BEFORE touching any frequency counter. A Zen run that
    /// silently advanced the 1-in-3 cadence would still shift the ad onto the player's next run,
    /// which is the same interruption moved one screen later.</para>
    /// </summary>
    public static bool AllowsInterstitial(GameModeId mode) => mode != GameModeId.Zen;
}
