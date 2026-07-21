using System.Collections.Generic;

namespace Blockfall.Core;

/// <summary>
/// Applies a set of drafted <see cref="Charm"/>s to stage rules by composing
/// immutable copies — the Descent mirror of <see cref="ModifierSet"/>. Pure,
/// engine-free, and ORDER-INDEPENDENT by construction: effects are aggregated
/// first (products, sums, one-way flags) and applied exactly once, so any draft
/// order re-simulates identically.
///
/// Stacking rules (the tuning contract):
///  - Score multipliers MULTIPLY, then clamp to [0.5, 3.0] — compounding greed
///    can't trivialize the deep strata, compounding taxes can't zero a build.
///  - Gravity factors multiply (factors &lt; 1 mean faster fall — a tooth).
///  - Lock-delay charms: the HARSHEST (smallest) value wins, so Anchor's safety
///    net can never erase Gambler's razor.
///  - Hold/ghost seals and all-spin are one-way switches (never re-enabled).
/// </summary>
public static class CharmSet
{
    public const double MultiplierFloor = 0.5;
    public const double MultiplierCeiling = 3.0;
    public const double MinStageTime = 30.0;
    public const int MinAttackGoal = 3;
    public const int MinInitialGarbage = 1;

    /// <summary>Compose the config-level effects of the owned charms onto a stage config.</summary>
    public static GameConfig Apply(GameConfig config, IReadOnlyCollection<Charm> charms)
    {
        if (charms.Count == 0) return config;

        double scoreMult = 1.0;
        double gravityFactor = 1.0;
        double? lockDelay = null; // min over all setters
        bool holdOff = false, ghostOff = false, allSpin = false, uncapGravity = false;
        int previewDelta = 0;

        foreach (var c in charms)
        {
            switch (c)
            {
                case Charm.Greed: scoreMult *= 1.5; gravityFactor *= 0.6; break;
                case Charm.Prophet: previewDelta += 2; holdOff = true; break;
                case Charm.SpinSage: allSpin = true; lockDelay = MinSet(lockDelay, 0.35); break;
                case Charm.Anchor: lockDelay = MinSet(lockDelay, 0.8); scoreMult *= 0.9; break;
                case Charm.Gambler: scoreMult *= 2.0; lockDelay = MinSet(lockDelay, 0.15); break;
                case Charm.Feather: gravityFactor *= 1.6; break;
                case Charm.Blindfold: ghostOff = true; scoreMult *= 1.25; break;
                case Charm.Monk: holdOff = true; scoreMult *= 1.3; break;
                case Charm.Sapper: scoreMult *= 0.8; break;
                case Charm.Hourglass: scoreMult *= 0.85; break;
                // Juggernaut's uncap is defensive: current strata never cap anyway
                // (the tuning table keeps MaxGravityLevel at 0), so the pitch is
                // the 20% slowdown paid for with heavier siege goals.
                case Charm.Juggernaut: uncapGravity = true; gravityFactor *= 1.2; break;
                // Warlord shapes only the mode (goals/clocks) — no config effect.
            }
        }

        double clamped = Math.Clamp(config.ScoreMultiplier * scoreMult, MultiplierFloor, MultiplierCeiling);
        return config.With(
            baseGravity: gravityFactor != 1.0 ? config.BaseGravity * gravityFactor : null,
            lockDelay: lockDelay,
            maxGravityLevel: uncapGravity ? 0 : null,
            previewCount: previewDelta != 0 ? config.PreviewCount + previewDelta : null,
            hold: holdOff ? false : null,
            ghost: ghostOff ? false : null,
            allSpin: allSpin ? true : null,
            scoreMultiplier: clamped != config.ScoreMultiplier ? clamped : null);
    }

    /// <summary>
    /// Compose the full effect of the owned charms onto a stage: goal/clock shaping
    /// (time limits, siege goals, garbage prefills) plus the config-level effects.
    /// Only fields the stage actually uses are shaped (a charm that lightens siege
    /// goals does nothing on a Dig stratum) — picking for the road ahead is the skill.
    /// </summary>
    public static GameMode Apply(GameMode mode, IReadOnlyCollection<Charm> charms)
    {
        if (charms.Count == 0) return mode;

        double timeFactor = 1.0;
        double timeDelta = 0, riseDelta = 0;
        int attackDelta = 0, garbageDelta = 0;

        foreach (var c in charms)
        {
            switch (c)
            {
                case Charm.Feather: timeFactor *= 0.85; break;
                case Charm.Hourglass: timeDelta += 20.0; break;
                case Charm.Warlord: attackDelta -= 4; timeDelta -= 15.0; break;
                case Charm.Sapper: garbageDelta -= 2; riseDelta += 1.0; break;
                case Charm.Juggernaut: attackDelta += 3; break;
            }
        }

        double time = mode.TimeLimit > 0
            ? Math.Max(MinStageTime, mode.TimeLimit * timeFactor + timeDelta)
            : mode.TimeLimit;
        int attackGoal = mode.AttackGoal > 0
            ? Math.Max(MinAttackGoal, mode.AttackGoal + attackDelta)
            : mode.AttackGoal;
        int initialGarbage = mode.InitialGarbage > 0
            ? Math.Max(MinInitialGarbage, mode.InitialGarbage + garbageDelta)
            : mode.InitialGarbage;
        double riseInterval = mode.GarbageRiseInterval > 0
            ? mode.GarbageRiseInterval + riseDelta
            : mode.GarbageRiseInterval;

        return new GameMode
        {
            Id = mode.Id,
            Name = mode.Name,
            LineGoal = mode.LineGoal,
            TimeLimit = time,
            LevelCap = mode.LevelCap,
            StartLevel = mode.StartLevel,
            CanTopOut = mode.CanTopOut,
            InitialGarbage = initialGarbage,
            DigGoal = mode.DigGoal,
            GarbageRiseInterval = riseInterval,
            GarbageRiseAccel = mode.GarbageRiseAccel,
            GarbageRiseAmount = mode.GarbageRiseAmount,
            AttackGoal = attackGoal,
            Config = Apply(mode.Config, charms),
        };
    }

    private static double MinSet(double? current, double candidate)
        => current is null ? candidate : Math.Min(current.Value, candidate);

    /// <summary>Short display name (also the Loc key — English original).</summary>
    public static string Label(Charm c) => c switch
    {
        Charm.Greed => "GREED",
        Charm.Prophet => "PROPHET",
        Charm.SpinSage => "SPIN SAGE",
        Charm.Anchor => "ANCHOR",
        Charm.Gambler => "GAMBLER",
        Charm.Feather => "FEATHER",
        Charm.Blindfold => "BLINDFOLD",
        Charm.Monk => "MONK",
        Charm.Sapper => "SAPPER",
        Charm.Warlord => "WARLORD",
        Charm.Hourglass => "HOURGLASS",
        Charm.Juggernaut => "JUGGERNAUT",
        _ => c.ToString(),
    };

    /// <summary>One-line trade description (also the Loc key — English original).</summary>
    public static string Describe(Charm c) => c switch
    {
        Charm.Greed => "Score x1.5 - pieces fall 40% faster",
        Charm.Prophet => "+2 next previews - Hold is sealed",
        Charm.SpinSage => "Every spin scores - lock delay 0.35s",
        Charm.Anchor => "Lock delay 0.8s - score x0.9",
        Charm.Gambler => "Score x2.0 - lock delay 0.15s",
        Charm.Feather => "Gravity 60% slower - timed stages 15% shorter",
        Charm.Blindfold => "No ghost piece - score x1.25",
        Charm.Monk => "Hold is sealed - score x1.3",
        Charm.Sapper => "2 fewer garbage rows, slower rise - score x0.8",
        Charm.Warlord => "Siege goals 4 lines lighter - Burst 15s shorter",
        Charm.Hourglass => "Timed stages +20s - score x0.85",
        Charm.Juggernaut => "Gravity 20% slower - siege goals +3",
        _ => string.Empty,
    };

    /// <summary>Every draftable charm, in enum order (the draft pool source).</summary>
    public static readonly Charm[] All =
    {
        Charm.Greed, Charm.Prophet, Charm.SpinSage, Charm.Anchor, Charm.Gambler,
        Charm.Feather, Charm.Blindfold, Charm.Monk, Charm.Sapper, Charm.Warlord,
        Charm.Hourglass, Charm.Juggernaut,
    };
}
