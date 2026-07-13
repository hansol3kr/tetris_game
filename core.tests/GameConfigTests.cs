using Blockfall.Core;
using Xunit;

namespace Blockfall.Core.Tests;

public class GameConfigTests
{
    [Fact]
    public void GravityForLevel1_EqualsBaseGravity()
    {
        // Regression: BaseGravity was previously ignored by the curve.
        Assert.Equal(1.0, GameConfig.Default.GravityForLevel(1), 6);

        var slow = new GameConfig { BaseGravity = 1.2, MaxGravityLevel = 0 };
        Assert.Equal(1.2, slow.GravityForLevel(1), 6);

        var fast = new GameConfig { BaseGravity = 0.5, MaxGravityLevel = 0 };
        Assert.Equal(0.5, fast.GravityForLevel(1), 6);
    }

    [Fact]
    public void GravityGetsFaster_AsLevelRises()
    {
        var cfg = GameConfig.Default;
        double l1 = cfg.GravityForLevel(1);
        double l5 = cfg.GravityForLevel(5);
        double l9 = cfg.GravityForLevel(9);
        Assert.True(l5 < l1);
        Assert.True(l9 < l5);
    }

    [Fact]
    public void Gravity_NeverBelowFrameFloor()
    {
        var cfg = GameConfig.Default;
        for (int level = 1; level <= 30; level++)
            Assert.True(cfg.GravityForLevel(level) >= 1.0 / 60.0);
    }

    [Fact]
    public void BaseGravity_ScalesTheWholeCurve()
    {
        var normal = new GameConfig { MaxGravityLevel = 0 };
        var doubled = new GameConfig { BaseGravity = 2.0, MaxGravityLevel = 0 };
        // At the same level the doubled config should be ~2x slower (until the floor).
        double n = normal.GravityForLevel(3);
        double d = doubled.GravityForLevel(3);
        Assert.Equal(2.0, d / n, 3);
    }
}
