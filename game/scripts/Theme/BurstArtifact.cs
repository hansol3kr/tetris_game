using Godot;

namespace Blockfall.Theme;

/// <summary>
/// The line-clear celebration an equipped "burst FX" artifact plays in Block Fit.
/// Sparks is the always-owned free default; the rest are cosmetic-only (they never
/// change scoring — see the fairness rules in docs/MONETIZATION.md). Append-only:
/// values are persisted as the equipped artifact id, so new entries go at the END.
/// </summary>
public enum BurstArtifact
{
    Sparks, Fireworks, Confetti, Supernova, Shards, Rainbow,
    Aurora, Lightning, BubblePop, PrismBloom, Starfall,
    // The animal-set signatures. Each is a free catalog row in its own right; a set skin just
    // equips its own by default (see BlockTheme.Signature).
    Fluff, Splash, Swarm,
    // The texture set: four recipes that each own a MATERIAL rather than a colour, so none of
    // them can be mistaken for one of the eleven light-and-sparkle bursts above.
    // Glitch = digital tearing, Petals = organic drift, Frost = crystal growth, Ink = pigment.
    Glitch, Petals, Frost, Ink,
}

/// <summary>Maps store item ids to the burst style Block Fit renders.</summary>
public static class BurstArtifacts
{
    public const string DefaultId = "artifact_sparks";

    public static BurstArtifact FromId(string? id) => id switch
    {
        "artifact_fireworks" => BurstArtifact.Fireworks,
        "artifact_confetti" => BurstArtifact.Confetti,
        "artifact_supernova" => BurstArtifact.Supernova,
        "artifact_shards" => BurstArtifact.Shards,
        "artifact_rainbow" => BurstArtifact.Rainbow,
        "artifact_aurora" => BurstArtifact.Aurora,
        "artifact_lightning" => BurstArtifact.Lightning,
        "artifact_bubblepop" => BurstArtifact.BubblePop,
        "artifact_prismbloom" => BurstArtifact.PrismBloom,
        "artifact_starfall" => BurstArtifact.Starfall,
        "artifact_fluff" => BurstArtifact.Fluff,
        "artifact_splash" => BurstArtifact.Splash,
        "artifact_swarm" => BurstArtifact.Swarm,
        "artifact_glitch" => BurstArtifact.Glitch,
        "artifact_petals" => BurstArtifact.Petals,
        "artifact_frost" => BurstArtifact.Frost,
        "artifact_ink" => BurstArtifact.Ink,
        _ => BurstArtifact.Sparks,
    };

    /// <summary>The inverse of <see cref="FromId"/> — the catalog id an artifact is sold under.
    /// Needed so a themed set can equip its signature burst by VALUE (the theme declares a
    /// <see cref="BurstArtifact"/>, the save file stores an id).</summary>
    public static string ToId(BurstArtifact art) => art switch
    {
        BurstArtifact.Fireworks => "artifact_fireworks",
        BurstArtifact.Confetti => "artifact_confetti",
        BurstArtifact.Supernova => "artifact_supernova",
        BurstArtifact.Shards => "artifact_shards",
        BurstArtifact.Rainbow => "artifact_rainbow",
        BurstArtifact.Aurora => "artifact_aurora",
        BurstArtifact.Lightning => "artifact_lightning",
        BurstArtifact.BubblePop => "artifact_bubblepop",
        BurstArtifact.PrismBloom => "artifact_prismbloom",
        BurstArtifact.Starfall => "artifact_starfall",
        BurstArtifact.Fluff => "artifact_fluff",
        BurstArtifact.Splash => "artifact_splash",
        BurstArtifact.Swarm => "artifact_swarm",
        BurstArtifact.Glitch => "artifact_glitch",
        BurstArtifact.Petals => "artifact_petals",
        BurstArtifact.Frost => "artifact_frost",
        BurstArtifact.Ink => "artifact_ink",
        _ => DefaultId,
    };

    /// <summary>
    /// The identity colour of a burst — the hue its screen wash, its store-card poster and its
    /// equip flourish are all tinted with.
    ///
    /// <para>This is ONE table because it used to be three: an enum switch in
    /// <c>BlockFitController</c> for the in-game wash, another in <c>StorePreviews</c> for the
    /// reduced-motion poster, and a string-keyed one in <c>StoreScreen</c> for the equip pulse.
    /// None of the three errored on a missing entry, so they drifted exactly as you would expect —
    /// FLUFF, SPLASH and SWARM shipped absent from the in-game table and washed the screen gold,
    /// while the store card next to them showed the right colour. Four more artifacts would have
    /// meant twelve more chances to get that wrong.</para>
    ///
    /// <para>Values that reach for <see cref="Palette"/> follow the equipped skin's accent on
    /// purpose (a burst is decoration, and decoration should move with the skin); the literals are
    /// bursts whose identity IS a specific colour — white-hot, ember, ice, ink — and would stop
    /// being themselves if a skin repainted them.</para>
    /// </summary>
    public static Color AccentOf(BurstArtifact art) => art switch
    {
        BurstArtifact.Supernova => new Color(1f, 0.98f, 0.90f),
        BurstArtifact.Rainbow => Palette.AccentViolet,
        BurstArtifact.Fireworks => Palette.AccentGold,
        BurstArtifact.Confetti => Palette.AccentGreen,
        BurstArtifact.Shards => Palette.Accent,
        BurstArtifact.Aurora => new Color(0.30f, 0.90f, 0.80f),
        BurstArtifact.Lightning => Palette.Accent,
        BurstArtifact.BubblePop => Palette.Accent,
        BurstArtifact.PrismBloom => Palette.AccentViolet,
        BurstArtifact.Starfall => new Color(0.90f, 0.85f, 1.00f),
        BurstArtifact.Fluff => new Color(1.00f, 0.86f, 0.70f),
        BurstArtifact.Splash => new Color(0.55f, 0.95f, 1.00f),
        BurstArtifact.Swarm => new Color(0.75f, 1.00f, 0.90f),
        BurstArtifact.Glitch => new Color(0.35f, 1.00f, 0.85f),
        BurstArtifact.Petals => new Color(1.00f, 0.72f, 0.85f),
        BurstArtifact.Frost => new Color(0.75f, 0.92f, 1.00f),
        BurstArtifact.Ink => new Color(0.55f, 0.62f, 0.85f),
        _ => Palette.AccentGold,
    };

    /// <summary>
    /// How hard a burst washes the backdrop, before the per-line scaling the caller adds. Split
    /// from <see cref="AccentOf"/> rather than returned as a tuple because only the in-game wash
    /// needs it — a store card has no screen to bleach — and a tuple would have made every call
    /// site discard half of it.
    /// </summary>
    public static float PulseOf(BurstArtifact art) => art switch
    {
        BurstArtifact.Supernova => 0.34f,
        BurstArtifact.Lightning => 0.30f,
        BurstArtifact.Fireworks => 0.28f,
        BurstArtifact.Rainbow or BurstArtifact.PrismBloom => 0.26f,
        BurstArtifact.Shards or BurstArtifact.Starfall or BurstArtifact.Glitch => 0.24f,
        BurstArtifact.Confetti or BurstArtifact.Frost => 0.22f,
        BurstArtifact.Aurora or BurstArtifact.Splash or BurstArtifact.Swarm => 0.20f,
        // The quiet ones. FLUFF, PETALS and INK are sold on NOT flashing ("no flash, no glare"),
        // so their wash sits below every sparkle burst rather than at the default.
        BurstArtifact.BubblePop or BurstArtifact.Petals => 0.18f,
        BurstArtifact.Fluff or BurstArtifact.Ink => 0.15f,
        _ => 0.22f,
    };
}
