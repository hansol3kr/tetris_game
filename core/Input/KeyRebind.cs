using System.Collections.Generic;

namespace Blockfall.Core;

/// <summary>
/// Pure keyboard-rebind bookkeeping (engine-agnostic, unit-tested). Maintains a
/// MINIMAL override map — only bindings that differ from their default — and, on
/// a collision, SWAPS keys so every action stays bound exactly once (never
/// duplicated, never left blank). The Godot layer (KeyBinds) applies the result
/// to the live InputMap; all the fiddly logic lives here where it can be tested.
/// Keys are physical keycodes stored as long.
/// </summary>
public static class KeyRebind
{
    /// <summary>The key currently bound to an action: its override, else its default.</summary>
    public static long Current(IReadOnlyDictionary<string, long> overrides,
                               IReadOnlyDictionary<string, long> defaults, string action)
    {
        if (overrides.TryGetValue(action, out var c)) return c;
        return defaults.TryGetValue(action, out var d) ? d : 0;
    }

    /// <summary>
    /// Bind <paramref name="key"/> to <paramref name="action"/>. If another action
    /// already holds that key, it inherits this action's previous key (a swap).
    /// Mutates <paramref name="overrides"/> in place and returns the swapped action's
    /// name, or null if there was no collision (or the key was unchanged).
    /// </summary>
    public static string? Rebind(IDictionary<string, long> overrides,
                                 IReadOnlyDictionary<string, long> defaults,
                                 string action, long key)
    {
        long prev = CurrentOf(overrides, defaults, action);
        if (prev == key) return null;

        string? swapped = null;
        foreach (var kv in defaults)
        {
            if (kv.Key == action) continue;
            if (CurrentOf(overrides, defaults, kv.Key) == key)
            {
                SetOverride(overrides, defaults, kv.Key, prev);
                swapped = kv.Key;
                break; // keys are unique across actions, so at most one collision
            }
        }

        SetOverride(overrides, defaults, action, key);
        return swapped;
    }

    // Current-key reader over the MUTABLE override map (IDictionary doesn't derive
    // from IReadOnlyDictionary, so the public Current overload can't take it).
    private static long CurrentOf(IDictionary<string, long> overrides,
                                  IReadOnlyDictionary<string, long> defaults, string action)
    {
        if (overrides.TryGetValue(action, out var c)) return c;
        return defaults.TryGetValue(action, out var d) ? d : 0;
    }

    // Keep the map minimal: a binding equal to its default is stored as "no override".
    private static void SetOverride(IDictionary<string, long> overrides,
                                    IReadOnlyDictionary<string, long> defaults, string action, long key)
    {
        if (defaults.TryGetValue(action, out var d) && d == key) overrides.Remove(action);
        else overrides[action] = key;
    }
}
