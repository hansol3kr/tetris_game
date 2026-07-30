using System.Collections.Generic;
using System.Text.Json;
using Blockfall.Core;
using Xunit;

namespace Blockfall.Core.Tests;

/// <summary>
/// The rename of two PERSISTED identifiers that carried a third-party trademark (CLAUDE.md §8-1
/// bans the mark from code and metadata, and both of these were written into every player's save
/// file). Nothing the player ever saw changes — the stat has always been labelled "QUADS" and the
/// achievement has always been called "Quad Damage" — so the only way this can go wrong is by
/// quietly resetting progress, which is exactly what these tests exist to prevent.
/// </summary>
public class TrademarkMigrationTests
{
    [Fact]
    public void LifetimeStats_LoadsTheLegacyKey_SoAnExistingPlayerKeepsTheirQuadCount()
    {
        // A save written by any build up to v1.7.0.
        const string legacy = """{"GamesPlayed":12,"Tetrises":37,"TSpins":4}""";

        var s = JsonSerializer.Deserialize<LifetimeStats>(legacy)!;

        Assert.Equal(37, s.Quads);   // folded forward, not reset to zero
        Assert.Equal(12, s.GamesPlayed);
        Assert.Equal(4, s.TSpins);
    }

    [Fact]
    public void LifetimeStats_NeverWritesTheLegacyKeyBack()
    {
        // The whole point of the rename is that the string leaves the save file. A migration shim
        // that round-trips the old key would keep it there forever.
        var s = JsonSerializer.Deserialize<LifetimeStats>("""{"Tetrises":9}""")!;

        string json = JsonSerializer.Serialize(s);

        Assert.DoesNotContain("Tetrises", json);
        Assert.Contains("\"Quads\":9", json);
    }

    [Fact]
    public void LifetimeStats_NewKeyWins_WhenBothArePresent()
    {
        // Only possible from a hand-edited or half-merged file, but the shim takes the max rather
        // than letting key order decide — losing a career total to JSON ordering would be absurd.
        var a = JsonSerializer.Deserialize<LifetimeStats>("""{"Tetrises":5,"Quads":11}""")!;
        var b = JsonSerializer.Deserialize<LifetimeStats>("""{"Quads":11,"Tetrises":5}""")!;

        Assert.Equal(11, a.Quads);
        Assert.Equal(11, b.Quads);
    }

    [Fact]
    public void MigrateId_RenamesTheOldAchievement_AndLeavesEverythingElseAlone()
    {
        Assert.Equal("first_quad", AchievementCatalog.MigrateId("first_tetris"));
        Assert.Equal("first_line", AchievementCatalog.MigrateId("first_line"));
        // An id from a NEWER build must pass through, not be dropped — discarding it would lose
        // that unlock permanently on the next write.
        Assert.Equal("some_future_id", AchievementCatalog.MigrateId("some_future_id"));
    }

    [Fact]
    public void MigrateUnlocked_RewritesTheListInPlace_PreservingOrder()
    {
        var unlocked = new List<string> { "first_line", "first_tetris", "perfect" };

        Assert.True(AchievementCatalog.MigrateUnlocked(unlocked));

        Assert.Equal(new[] { "first_line", "first_quad", "perfect" }, unlocked);
    }

    [Fact]
    public void MigrateUnlocked_CollapsesTheDuplicateAMergeCanCreate()
    {
        // A cloud save from an old device unioned with a local one from a new device can hold both
        // spellings of the same unlock. The player earned it once; it must appear once.
        var unlocked = new List<string> { "first_quad", "first_tetris" };

        Assert.True(AchievementCatalog.MigrateUnlocked(unlocked));

        Assert.Equal(new[] { "first_quad" }, unlocked);
    }

    [Fact]
    public void MigrateUnlocked_ReportsNoChange_WhenNothingNeedsMigrating()
    {
        // The caller marks the save dirty on a true return, so a false negative would rewrite the
        // file on every single launch.
        var unlocked = new List<string> { "first_line", "first_quad" };

        Assert.False(AchievementCatalog.MigrateUnlocked(unlocked));

        Assert.Equal(new[] { "first_line", "first_quad" }, unlocked);
    }

    [Fact]
    public void AchievementCatalog_ContainsNoTrademarkedIdentifier()
    {
        // The audit itself, as a test: a future achievement id cannot reintroduce the mark without
        // turning this red.
        foreach (var def in AchievementCatalog.All)
        {
            Assert.DoesNotContain("tetris", def.Id, System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("tetris", def.Name, System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("tetris", def.Description, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
