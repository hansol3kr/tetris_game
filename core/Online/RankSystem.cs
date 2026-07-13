using System;

namespace Blockfall.Core;

/// <summary>A player's competitive ranking, persisted and synced. Pure data.</summary>
public sealed class RankRating
{
    public int Rating { get; set; } = RankSystem.StartRating;
    public int Peak { get; set; } = RankSystem.StartRating;
    public int Wins { get; set; }
    public int Losses { get; set; }

    /// <summary>Total ranked matches played — drives the provisional K-factor.</summary>
    public int GamesPlayed => Wins + Losses;
}

/// <summary>The named rank bands a rating falls into, coarse → elite.</summary>
public enum RankTier { Bronze, Silver, Gold, Platinum, Diamond, Master, Grandmaster }

/// <summary>
/// Elo-style competitive ranking for ranked (Quick Match) online duels. Engine-
/// agnostic and unit-tested: given a rating and an opponent's, a win/loss produces
/// a deterministic rating delta. Provisional players (few games) move faster so new
/// accounts converge quickly, then settle. Ratings never fall below a floor, and a
/// result always shifts the rating by at least ±1 so every match visibly counts.
/// </summary>
public static class RankSystem
{
    public const int StartRating = 1000;
    public const int MinRating = 100;

    /// <summary>Elo expected score (win probability) of <paramref name="rating"/> vs an opponent.</summary>
    public static double Expected(int rating, int opponentRating)
        => 1.0 / (1.0 + Math.Pow(10, (opponentRating - rating) / 400.0));

    /// <summary>K-factor: larger while provisional so new ratings converge, then tightens.</summary>
    public static int KFactor(int gamesPlayed)
        => gamesPlayed < 10 ? 40 : gamesPlayed < 30 ? 24 : 16;

    /// <summary>
    /// Apply one ranked result to <paramref name="r"/> (mutates it) and return the
    /// signed rating delta actually applied. Wins always gain ≥1, losses lose ≥1.
    /// </summary>
    public static int Apply(RankRating r, int opponentRating, bool won)
    {
        double expected = Expected(r.Rating, opponentRating);
        double actual = won ? 1.0 : 0.0;
        int k = KFactor(r.GamesPlayed);
        int delta = (int)Math.Round(k * (actual - expected));

        // Guarantee movement in the right direction — a rounded delta of 0 (or a
        // wrong-signed one at extreme rating gaps) would make a match feel like a no-op.
        if (won && delta <= 0) delta = 1;
        if (!won && delta >= 0) delta = -1;

        int before = r.Rating;
        r.Rating = Math.Max(MinRating, r.Rating + delta);
        if (won) r.Wins++; else r.Losses++;
        if (r.Rating > r.Peak) r.Peak = r.Rating;
        return r.Rating - before; // the delta actually applied (0 at the floor on a loss)
    }

    /// <summary>The tier a rating sits in.</summary>
    public static RankTier TierOf(int rating) =>
        rating >= 2200 ? RankTier.Grandmaster :
        rating >= 1900 ? RankTier.Master :
        rating >= 1650 ? RankTier.Diamond :
        rating >= 1400 ? RankTier.Platinum :
        rating >= 1200 ? RankTier.Gold :
        rating >= 1000 ? RankTier.Silver :
                         RankTier.Bronze;

    /// <summary>Display name for a tier (English key; localized in the UI layer).</summary>
    public static string TierName(RankTier tier) => tier switch
    {
        RankTier.Grandmaster => "GRANDMASTER",
        RankTier.Master => "MASTER",
        RankTier.Diamond => "DIAMOND",
        RankTier.Platinum => "PLATINUM",
        RankTier.Gold => "GOLD",
        RankTier.Silver => "SILVER",
        _ => "BRONZE",
    };
}
