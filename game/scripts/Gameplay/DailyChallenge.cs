using Godot;

namespace Blockfall.Gameplay;

/// <summary>
/// Derives the daily-challenge date key and its deterministic seed. Everyone who
/// plays on the same UTC calendar day gets the identical piece sequence, so daily
/// scores are directly comparable (and leaderboard-ready). Because the seed is a
/// pure function of the date and the core RNG (XorShift) is version-stable, mobile
/// and Steam players see the same board — no server required for the challenge itself.
/// </summary>
public static class DailyChallenge
{
    /// <summary>Today's ("yyyy-MM-dd", seed) in UTC.</summary>
    public static (string Key, ulong Seed) Today()
    {
        var d = Time.GetDatetimeDictFromSystem(true); // utc
        int year = d["year"].AsInt32();
        int month = d["month"].AsInt32();
        int day = d["day"].AsInt32();
        return ($"{year:0000}-{month:00}-{day:00}", Seed(year, month, day));
    }

    private static ulong Seed(int year, int month, int day)
    {
        // Mix the date into a well-spread 64-bit value (SplitMix64 finalizer).
        ulong z = ((ulong)year << 16) ^ ((ulong)month << 8) ^ (ulong)day;
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
