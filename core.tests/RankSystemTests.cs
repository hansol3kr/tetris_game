using Blockfall.Core;
using Xunit;

namespace Blockfall.Core.Tests;

public class RankSystemTests
{
    [Fact]
    public void Expected_IsHalf_ForEqualRatings()
    {
        Assert.Equal(0.5, RankSystem.Expected(1000, 1000), 6);
    }

    [Fact]
    public void Expected_FavorsHigherRating()
    {
        Assert.True(RankSystem.Expected(1400, 1000) > 0.5);
        Assert.True(RankSystem.Expected(1000, 1400) < 0.5);
    }

    [Fact]
    public void Apply_Win_RaisesRating_Loss_LowersRating()
    {
        var w = new RankRating();
        int dw = RankSystem.Apply(w, 1000, won: true);
        Assert.True(dw > 0);
        Assert.Equal(1000 + dw, w.Rating);
        Assert.Equal(1, w.Wins);

        var l = new RankRating();
        int dl = RankSystem.Apply(l, 1000, won: false);
        Assert.True(dl < 0);
        Assert.Equal(1000 + dl, l.Rating);
        Assert.Equal(1, l.Losses);
    }

    [Fact]
    public void Apply_BeatingStrongerOpponent_GainsMoreThanBeatingWeaker()
    {
        var strong = new RankRating();
        int upset = RankSystem.Apply(strong, 1600, won: true); // big underdog win

        var easy = new RankRating();
        int expected = RankSystem.Apply(easy, 600, won: true); // beat a much weaker player

        Assert.True(upset > expected);
    }

    [Fact]
    public void Apply_AlwaysMovesAtLeastOne()
    {
        // A heavy favorite still loses at least 1 on a win (rounding could give 0).
        var favorite = new RankRating { Rating = 2000 };
        int d = RankSystem.Apply(favorite, 100, won: true);
        Assert.True(d >= 1);
    }

    [Fact]
    public void Apply_NeverFallsBelowFloor()
    {
        var r = new RankRating { Rating = RankSystem.MinRating };
        RankSystem.Apply(r, 2500, won: false);
        Assert.Equal(RankSystem.MinRating, r.Rating);
    }

    [Fact]
    public void Apply_TracksPeak()
    {
        var r = new RankRating();
        RankSystem.Apply(r, 1200, won: true);
        int peakAfterWin = r.Peak;
        Assert.Equal(r.Rating, peakAfterWin);
        RankSystem.Apply(r, 1200, won: false);
        Assert.True(r.Rating < peakAfterWin);
        Assert.Equal(peakAfterWin, r.Peak); // peak stays at the high-water mark
    }

    [Fact]
    public void KFactor_ShrinksAsGamesGrow()
    {
        Assert.True(RankSystem.KFactor(0) > RankSystem.KFactor(15));
        Assert.True(RankSystem.KFactor(15) > RankSystem.KFactor(50));
    }

    [Theory]
    [InlineData(50, RankTier.Bronze)]
    [InlineData(1000, RankTier.Silver)]
    [InlineData(1250, RankTier.Gold)]
    [InlineData(1500, RankTier.Platinum)]
    [InlineData(1700, RankTier.Diamond)]
    [InlineData(2000, RankTier.Master)]
    [InlineData(2500, RankTier.Grandmaster)]
    public void TierOf_MapsRatingToBand(int rating, RankTier expected)
    {
        Assert.Equal(expected, RankSystem.TierOf(rating));
    }

    [Fact]
    public void TierName_CoversEveryTier()
    {
        foreach (RankTier tier in System.Enum.GetValues(typeof(RankTier)))
            Assert.False(string.IsNullOrWhiteSpace(RankSystem.TierName(tier)));
    }
}
