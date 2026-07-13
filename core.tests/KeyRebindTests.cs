using System.Collections.Generic;
using Blockfall.Core;
using Xunit;

namespace Blockfall.Core.Tests;

/// <summary>
/// The pure keyboard-rebind bookkeeping: a MINIMAL override map (only bindings
/// that differ from default) with collision SWAP so every action stays bound
/// exactly once. The Godot layer only mirrors these results into the InputMap.
/// </summary>
public class KeyRebindTests
{
    // A small stand-in for the real action set. Values are arbitrary "keycodes".
    private static Dictionary<string, long> Defaults() => new()
    {
        ["left"]  = 1,
        ["right"] = 2,
        ["drop"]  = 3,
        ["hold"]  = 4,
    };

    [Fact]
    public void Current_ReturnsOverride_ThenDefault()
    {
        var defaults = Defaults();
        var overrides = new Dictionary<string, long> { ["left"] = 99 };

        Assert.Equal(99, KeyRebind.Current(overrides, defaults, "left")); // override wins
        Assert.Equal(2, KeyRebind.Current(overrides, defaults, "right")); // falls back to default
    }

    [Fact]
    public void Rebind_ToFreeKey_SetsOverride_NoSwap()
    {
        var defaults = Defaults();
        var overrides = new Dictionary<string, long>();

        string? swapped = KeyRebind.Rebind(overrides, defaults, "left", 42);

        Assert.Null(swapped);
        Assert.Equal(42, KeyRebind.Current(overrides, defaults, "left"));
    }

    [Fact]
    public void Rebind_ToDefaultKey_StoresNoOverride()
    {
        var defaults = Defaults();
        var overrides = new Dictionary<string, long> { ["left"] = 99 };

        // Re-binding "left" back to its own default (1) should drop the override.
        string? swapped = KeyRebind.Rebind(overrides, defaults, "left", 1);

        Assert.Null(swapped);
        Assert.False(overrides.ContainsKey("left")); // minimal map: no redundant entry
        Assert.Equal(1, KeyRebind.Current(overrides, defaults, "left"));
    }

    [Fact]
    public void Rebind_SameKey_IsNoOp()
    {
        var defaults = Defaults();
        var overrides = new Dictionary<string, long>();

        string? swapped = KeyRebind.Rebind(overrides, defaults, "left", 1); // 1 is already left's key

        Assert.Null(swapped);
        Assert.Empty(overrides);
    }

    [Fact]
    public void Rebind_Collision_SwapsKeys()
    {
        var defaults = Defaults();
        var overrides = new Dictionary<string, long>();

        // Bind "left" to right's key (2). "right" must inherit left's old key (1).
        string? swapped = KeyRebind.Rebind(overrides, defaults, "left", 2);

        Assert.Equal("right", swapped);
        Assert.Equal(2, KeyRebind.Current(overrides, defaults, "left"));
        Assert.Equal(1, KeyRebind.Current(overrides, defaults, "right"));
    }

    [Fact]
    public void Rebind_Collision_KeepsEveryKeyBoundExactlyOnce()
    {
        var defaults = Defaults();
        var overrides = new Dictionary<string, long>();

        KeyRebind.Rebind(overrides, defaults, "drop", 1); // drop takes left's key; left takes drop's (3)

        var live = new HashSet<long>();
        foreach (var action in defaults.Keys)
            Assert.True(live.Add(KeyRebind.Current(overrides, defaults, action)),
                $"duplicate binding surfaced for {action}");
        Assert.Equal(defaults.Count, live.Count); // still one distinct key per action
    }

    [Fact]
    public void Rebind_SwapBackToDefault_MinimizesOverrides()
    {
        var defaults = Defaults();
        var overrides = new Dictionary<string, long>();

        // left<->right swap, then swap them back: the map should collapse to empty.
        KeyRebind.Rebind(overrides, defaults, "left", 2);
        KeyRebind.Rebind(overrides, defaults, "left", 1);

        Assert.Empty(overrides); // both bindings equal their defaults again
        Assert.Equal(1, KeyRebind.Current(overrides, defaults, "left"));
        Assert.Equal(2, KeyRebind.Current(overrides, defaults, "right"));
    }

    [Fact]
    public void Rebind_ChainedReassignments_StayConsistent()
    {
        var defaults = Defaults();
        var overrides = new Dictionary<string, long>();

        KeyRebind.Rebind(overrides, defaults, "left", 5);  // free key
        KeyRebind.Rebind(overrides, defaults, "right", 5); // collides with left -> left inherits right's (2)

        Assert.Equal(5, KeyRebind.Current(overrides, defaults, "right"));
        Assert.Equal(2, KeyRebind.Current(overrides, defaults, "left"));
        // drop/hold untouched
        Assert.Equal(3, KeyRebind.Current(overrides, defaults, "drop"));
        Assert.Equal(4, KeyRebind.Current(overrides, defaults, "hold"));
    }
}
