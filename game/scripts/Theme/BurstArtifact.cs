namespace Blockfall.Theme;

/// <summary>
/// The line-clear celebration an equipped "burst FX" artifact plays in Block Fit.
/// Sparks is the always-owned free default; the rest are cosmetic-only (they never
/// change scoring — see the fairness rules in docs/MONETIZATION.md).
/// </summary>
public enum BurstArtifact { Sparks, Fireworks, Confetti, Supernova, Shards, Rainbow }

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
        _ => BurstArtifact.Sparks,
    };
}
