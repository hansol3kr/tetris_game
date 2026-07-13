namespace Blockfall.Core;

/// <summary>
/// Turns a run seed into a short, shareable code and back. Because the whole
/// engine is deterministic per seed, a code fully reproduces a run — enabling
/// "replay this exact game", "race a friend on this seed", and typed seed entry.
///
/// Uses Crockford base32 (no I/L/O/U ambiguity), so codes are easy to read aloud
/// and paste. Pure and fully round-trip tested.
/// </summary>
public static class SeedCode
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"; // Crockford base32

    /// <summary>Canonical uppercase code for a seed (leading zeros stripped).</summary>
    public static string Encode(ulong value)
    {
        if (value == 0) return "0";
        var buffer = new char[13]; // 64 bits / 5 ≈ 13 symbols max
        int i = buffer.Length;
        while (value > 0)
        {
            buffer[--i] = Alphabet[(int)(value & 31)];
            value >>= 5;
        }
        return new string(buffer, i, buffer.Length - i);
    }

    /// <summary>
    /// Parses a code back into a seed. Case-insensitive; hyphens/spaces are ignored
    /// and the common I→1, L→1, O→0, U→V confusions are corrected. Returns false on
    /// any invalid character or on overflow past 64 bits.
    /// </summary>
    public static bool TryDecode(string code, out ulong value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(code)) return false;

        foreach (char raw in code)
        {
            char c = char.ToUpperInvariant(raw);
            if (c is '-' or ' ') continue;
            c = c switch { 'I' or 'L' => '1', 'O' => '0', 'U' => 'V', _ => c };

            int idx = Alphabet.IndexOf(c);
            if (idx < 0) return false;
            if (value > (ulong.MaxValue >> 5)) return false; // would overflow on shift
            value = (value << 5) | (uint)idx;
        }
        return true;
    }

    /// <summary>Derive a stable seed from arbitrary text (typing a word as a seed). FNV-1a 64-bit.</summary>
    public static ulong FromText(string text)
    {
        ulong hash = 14695981039346656037UL;
        foreach (char c in text)
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }
        return hash;
    }
}
