using System.Collections.Generic;

namespace Blockfall.Core;

/// <summary>Optional gameplay mutators that stack on any mode for fresh variety.</summary>
public enum GameModifier
{
    NoHold,      // hold slot disabled — commit to every piece
    NoGhost,     // no landing shadow — read the board yourself
    LowGravity,  // floaty, forgiving fall speed
    Blitz,       // fast, frantic fall speed
    HardLock,    // near-instant lock — no dithering on the floor
    AllSpin,     // reward immobile spins of every piece
    Invisible,   // the locked stack is hidden — play from memory (presentation-only)
}

/// <summary>
/// Applies a set of <see cref="GameModifier"/>s to a <see cref="GameConfig"/> by
/// composing immutable copies (via <see cref="GameConfig.With"/>). Pure and
/// order-independent for the current modifiers, so it's trivially unit-testable and
/// carries no engine dependency.
/// </summary>
public static class ModifierSet
{
    public static GameConfig Apply(GameConfig config, IReadOnlyCollection<GameModifier> modifiers)
    {
        var cfg = config;
        foreach (var m in modifiers)
        {
            cfg = m switch
            {
                GameModifier.NoHold => cfg.With(hold: false),
                GameModifier.NoGhost => cfg.With(ghost: false),
                GameModifier.LowGravity => cfg.With(baseGravity: 3.0, maxGravityLevel: 0),
                GameModifier.Blitz => cfg.With(baseGravity: 0.35, maxGravityLevel: 0),
                GameModifier.HardLock => cfg.With(lockDelay: 0.12),
                GameModifier.AllSpin => cfg.With(allSpin: true),
                GameModifier.Invisible => cfg, // presentation-only: BoardView hides the stack, config is untouched
                _ => cfg,
            };
        }
        return cfg;
    }

    /// <summary>Short display label for a modifier (UI toggles).</summary>
    public static string Label(GameModifier m) => m switch
    {
        GameModifier.NoHold => "NO HOLD",
        GameModifier.NoGhost => "NO GHOST",
        GameModifier.LowGravity => "LOW-G",
        GameModifier.Blitz => "BLITZ",
        GameModifier.HardLock => "HARD LOCK",
        GameModifier.AllSpin => "ALL-SPIN",
        GameModifier.Invisible => "INVISIBLE",
        _ => m.ToString(),
    };
}
