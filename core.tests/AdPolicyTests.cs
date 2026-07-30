using System;
using Blockfall.Core;
using Xunit;

namespace Blockfall.Core.Tests;

public class AdPolicyTests
{
    [Fact]
    public void AllowsInterstitial_IsFalseForZen_TheModeSoldOnHavingNoPressure()
    {
        // Regression: docs/MONETIZATION.md required Zen to skip interstitials from the day the ad
        // cap was written, but nothing implemented it — ResultsScreen called the ad hook without
        // passing the mode, so Zen was interrupted like every other mode. The rule lives here now
        // precisely so it cannot go missing again without a red test.
        Assert.False(AdPolicy.AllowsInterstitial(GameModeId.Zen));
    }

    [Fact]
    public void AllowsInterstitial_IsTrueForEveryOtherMode()
    {
        // Asserted over the WHOLE enum rather than a hand-listed few: a mode added later is
        // ad-eligible by default, and if someone means it to be exempt they have to say so here —
        // which is a conversation, not an accident.
        foreach (GameModeId mode in Enum.GetValues<GameModeId>())
        {
            if (mode == GameModeId.Zen) continue;
            Assert.True(AdPolicy.AllowsInterstitial(mode), $"{mode} should be ad-eligible");
        }
    }

    [Fact]
    public void AllowsInterstitial_LongModesAreStillEligible_LengthIsNotTheCriterion()
    {
        // The exclusion is about a mode having no fail state and no target, not about it being a
        // long sitting. Marathon and Descent are both long and both still end on a scoreboard.
        Assert.True(AdPolicy.AllowsInterstitial(GameModeId.Marathon));
        Assert.True(AdPolicy.AllowsInterstitial(GameModeId.Descent));
    }
}
