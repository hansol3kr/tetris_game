namespace Blockfall.Core;

/// <summary>Minimal deterministic RNG contract so gameplay can be seeded and replayed.</summary>
public interface IRandomSource
{
    /// <summary>Uniform integer in [0, maxExclusive).</summary>
    int Next(int maxExclusive);
}

/// <summary>
/// Fast, deterministic xorshift128 generator. We avoid System.Random because its
/// algorithm is not guaranteed stable across .NET versions/platforms, which would
/// break shared daily-challenge seeds between mobile and Steam.
/// </summary>
public sealed class XorShiftRandom : IRandomSource
{
    private uint _x, _y, _z, _w;

    public XorShiftRandom(ulong seed)
    {
        // SplitMix64-style seeding to avoid a zero state.
        ulong s = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
        _x = (uint)(s ^ 0xDEADBEEF);
        _y = (uint)((s >> 32) ^ 0x95419E13);
        _z = _x ^ 0x1B56C4E9;
        _w = _y ^ 0x0FC1A7B3;
        if ((_x | _y | _z | _w) == 0) _w = 1;
    }

    public uint NextUInt()
    {
        uint t = _x ^ (_x << 11);
        _x = _y; _y = _z; _z = _w;
        _w = _w ^ (_w >> 19) ^ t ^ (t >> 8);
        return _w;
    }

    public int Next(int maxExclusive)
    {
        if (maxExclusive <= 0) return 0;
        // Rejection sampling to keep a uniform distribution.
        uint range = (uint)maxExclusive;
        uint limit = uint.MaxValue - (uint.MaxValue % range);
        uint r;
        do { r = NextUInt(); } while (r >= limit);
        return (int)(r % range);
    }
}

/// <summary>Contract for a piece-sequence generator (7-bag today, could be 14-bag/other later).</summary>
public interface IPieceGenerator
{
    PieceType Next();
    /// <summary>Peek up to <paramref name="count"/> upcoming pieces without consuming them.</summary>
    IReadOnlyList<PieceType> Preview(int count);
}

/// <summary>
/// Standard 7-bag randomizer: every permutation of the seven pieces is dealt
/// before any repeats, guaranteeing fair, floodable distribution.
/// </summary>
public sealed class SevenBagGenerator : IPieceGenerator
{
    private static readonly PieceType[] AllPieces =
    {
        PieceType.I, PieceType.O, PieceType.T, PieceType.S, PieceType.Z, PieceType.J, PieceType.L,
    };

    private readonly IRandomSource _rng;
    private readonly Queue<PieceType> _queue = new();

    public SevenBagGenerator(IRandomSource rng)
    {
        _rng = rng;
        Refill();
    }

    private void Refill()
    {
        var bag = (PieceType[])AllPieces.Clone();
        // Fisher-Yates shuffle.
        for (int i = bag.Length - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (bag[i], bag[j]) = (bag[j], bag[i]);
        }
        foreach (var p in bag) _queue.Enqueue(p);
    }

    public PieceType Next()
    {
        if (_queue.Count == 0) Refill();
        return _queue.Dequeue();
    }

    public IReadOnlyList<PieceType> Preview(int count)
    {
        while (_queue.Count < count) Refill();
        var result = new List<PieceType>(count);
        int taken = 0;
        foreach (var p in _queue)
        {
            if (taken++ >= count) break;
            result.Add(p);
        }
        return result;
    }
}
