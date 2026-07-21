namespace Blockfall.Core;

/// <summary>
/// Descent's rule-bending boons, drafted one-of-three between strata. Every charm
/// is a trade — a reward paid for with a tooth — expressed purely as GameConfig /
/// GameMode data (see <see cref="CharmSet"/>), so charms never touch engine logic,
/// RNG consumption, or the replay input contract.
///
/// APPEND-ONLY: enum values are a wire/save contract for run records and share
/// codes (the same discipline as <see cref="GameModifier"/>). Never reorder or remove.
/// </summary>
public enum Charm
{
    Greed,      // score x1.5 — but gravity 40% faster
    Prophet,    // +2 previews — but hold is sealed
    SpinSage,   // all-spin rewarded — but lock delay tightens to 0.35s
    Anchor,     // lock delay stretches to 0.8s — but score x0.9
    Gambler,    // score x2.0 — but a razor 0.15s lock delay
    Feather,    // gravity 60% slower — but timed stages 15% shorter
    Blindfold,  // ghost projection off — score x1.25
    Monk,       // hold is sealed — score x1.3
    Sapper,     // 2 fewer garbage rows, slower rise — but score x0.8
    Warlord,    // siege goals 4 lines lighter — but burst clocks 15s shorter
    Hourglass,  // timed stages +20s — but score x0.85
    Juggernaut, // gravity 20% slower — but siege goals +3
}
