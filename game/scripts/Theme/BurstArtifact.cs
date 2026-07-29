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
        _ => DefaultId,
    };
}
