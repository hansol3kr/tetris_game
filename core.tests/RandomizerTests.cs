using Blockfall.Core;
using Xunit;

namespace Blockfall.Core.Tests;

public class RandomizerTests
{
    [Fact]
    public void SevenBag_FirstSevenPieces_AreAllDistinct()
    {
        var gen = new SevenBagGenerator(new XorShiftRandom(123));
        var seen = new HashSet<PieceType>();
        for (int i = 0; i < 7; i++) seen.Add(gen.Next());
        Assert.Equal(7, seen.Count);
    }

    [Fact]
    public void SevenBag_EveryWindowOfSeven_ContainsAllPieces()
    {
        var gen = new SevenBagGenerator(new XorShiftRandom(999));
        var draws = new List<PieceType>();
        for (int i = 0; i < 70; i++) draws.Add(gen.Next());

        for (int start = 0; start < 70; start += 7)
        {
            var window = new HashSet<PieceType>(draws.GetRange(start, 7));
            Assert.Equal(7, window.Count); // no piece repeats within a bag
        }
    }

    [Fact]
    public void SameSeed_ProducesIdenticalSequence()
    {
        var a = new SevenBagGenerator(new XorShiftRandom(2026));
        var b = new SevenBagGenerator(new XorShiftRandom(2026));
        for (int i = 0; i < 50; i++)
            Assert.Equal(a.Next(), b.Next());
    }

    [Fact]
    public void DifferentSeeds_DivergeSomewhere()
    {
        var a = new SevenBagGenerator(new XorShiftRandom(1));
        var b = new SevenBagGenerator(new XorShiftRandom(2));
        bool differs = false;
        for (int i = 0; i < 50; i++)
            if (a.Next() != b.Next()) { differs = true; break; }
        Assert.True(differs);
    }

    [Fact]
    public void Preview_DoesNotConsume_UpcomingPieces()
    {
        var gen = new SevenBagGenerator(new XorShiftRandom(7));
        var preview = gen.Preview(5);
        Assert.Equal(5, preview.Count);
        // The next actual draws must match the preview exactly.
        for (int i = 0; i < 5; i++)
            Assert.Equal(preview[i], gen.Next());
    }

    [Fact]
    public void XorShift_Next_StaysInRange()
    {
        var rng = new XorShiftRandom(555);
        for (int i = 0; i < 10000; i++)
        {
            int v = rng.Next(7);
            Assert.InRange(v, 0, 6);
        }
    }
}
