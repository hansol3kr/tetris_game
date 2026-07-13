using Godot;
using System.Collections.Generic;
using Blockfall.Core;
using Blockfall.Platform;

namespace Blockfall.Gameplay;

/// <summary>
/// Keyboard remapping. The game reads input by action name (see
/// <see cref="ButtonSampler"/>), so rebinding is just repointing each action's
/// keyboard event in Godot's <see cref="InputMap"/> — no gameplay code changes,
/// and gamepad bindings are left untouched. Bindings persist as physical
/// keycodes (layout-independent), and a rebind that collides with another action
/// SWAPS keys so every action always stays bound exactly once.
/// </summary>
public static class KeyBinds
{
    /// <summary>The remappable actions, their UI labels (English keys for Loc.T), and defaults.</summary>
    public static readonly (string Action, string Label, Key Default)[] Bindable =
    {
        ("move_left",  "MOVE LEFT",  Key.Left),
        ("move_right", "MOVE RIGHT", Key.Right),
        ("soft_drop",  "SOFT DROP",  Key.Down),
        ("hard_drop",  "HARD DROP",  Key.Space),
        ("rotate_cw",  "ROTATE CW",  Key.Up),
        ("rotate_ccw", "ROTATE CCW", Key.Z),
        ("rotate_180", "ROTATE 180", Key.A),
        ("hold_piece", "HOLD",       Key.C),
        ("pause_game", "PAUSE",      Key.Escape),
    };

    private static Key DefaultOf(string action)
    {
        foreach (var b in Bindable) if (b.Action == action) return b.Default;
        return Key.None;
    }

    // Defaults as the plain map the core rebind logic works with.
    private static Dictionary<string, long> DefaultMap()
    {
        var d = new Dictionary<string, long>();
        foreach (var b in Bindable) d[b.Action] = (long)b.Default;
        return d;
    }

    /// <summary>The key currently bound to an action (override, else default).</summary>
    public static Key CurrentKey(GameSettings s, string action)
        => s.KeyBindings.TryGetValue(action, out var code) ? (Key)code : DefaultOf(action);

    /// <summary>Repoint every bindable action's keyboard event at its bound key.
    /// Safe to call repeatedly (idempotent); leaves gamepad events intact.</summary>
    public static void ApplyAll(GameSettings s)
    {
        foreach (var b in Bindable)
            SetActionKey(b.Action, CurrentKey(s, b.Action));
    }

    /// <summary>Replace the keyboard event(s) of one action with a single physical key.</summary>
    public static void SetActionKey(string action, Key physical)
    {
        if (!InputMap.HasAction(action)) return;
        foreach (var ev in InputMap.ActionGetEvents(action))
            if (ev is InputEventKey) InputMap.ActionEraseEvent(action, ev);
        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = physical });
    }

    /// <summary>Bind <paramref name="physical"/> to <paramref name="action"/>, swapping
    /// with any action that already holds that key so nothing is left duplicated or unbound.
    /// Mutates <paramref name="s"/> (caller persists) and applies to the live InputMap.</summary>
    public static void Rebind(GameSettings s, string action, Key physical)
    {
        // Core does the (tested) map bookkeeping + collision swap; we mirror the
        // result into the live InputMap for this action and any swapped one.
        string? swapped = KeyRebind.Rebind(s.KeyBindings, DefaultMap(), action, (long)physical);
        if (swapped != null) SetActionKey(swapped, CurrentKey(s, swapped));
        SetActionKey(action, physical);
    }

    /// <summary>Clear all custom bindings and restore the built-in defaults.</summary>
    public static void ResetDefaults(GameSettings s)
    {
        s.KeyBindings.Clear();
        ApplyAll(s);
    }

    /// <summary>A friendly, physical-layout key name for the UI (e.g. "Left", "Space").</summary>
    public static string KeyName(Key physical)
    {
        string n = OS.GetKeycodeString(physical);
        return string.IsNullOrEmpty(n) ? physical.ToString() : n;
    }
}
