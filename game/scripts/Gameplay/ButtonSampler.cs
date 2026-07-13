using Godot;
using Blockfall.Core;

namespace Blockfall.Gameplay;

/// <summary>
/// Collapses all input sources — keyboard, gamepad (both via the project input
/// map) and on-screen touch — into a single per-tick <see cref="Buttons"/> mask
/// for the deterministic <see cref="InputProcessor"/>.
///
/// Held controls (move/soft) are polled each tick. Edge controls (rotate / hard
/// drop / hold) are LATCHED the instant they are pressed and delivered on the
/// next tick, so a press is never lost or double-counted when the fixed-step loop
/// runs zero or several ticks in a frame. This is what makes recorded replays
/// reproduce the live run exactly.
/// </summary>
public sealed class ButtonSampler
{
    private Buttons _latched;   // pending edge presses, consumed on the next Sample()
    private bool _touchLeft, _touchRight, _touchSoft;
    // Drag-to-move (gesture controls): the finger's column crossings are drained into
    // one-cell-at-a-time taps here, so a drag reproduces exactly on replay (see DragStepper).
    private readonly DragStepper _drag = new();

    // --- Edge presses (called from _UnhandledInput and touch buttons) -------
    public void LatchRotateCw() => _latched |= Buttons.RotateCw;
    public void LatchRotateCcw() => _latched |= Buttons.RotateCcw;
    public void LatchRotate180() => _latched |= Buttons.Rotate180;
    public void LatchHardDrop() => _latched |= Buttons.Hard;
    public void LatchHold() => _latched |= Buttons.Hold;

    // --- Touch held state (from TouchControls press/release) ----------------
    public void SetTouchLeft(bool on) => _touchLeft = on;
    public void SetTouchRight(bool on) => _touchRight = on;
    public void SetTouchSoft(bool on) => _touchSoft = on;

    // --- Drag stepping (from GestureBoardControls) --------------------------
    /// <summary>Queue horizontal drag steps (±1 per cell the finger crossed).</summary>
    public void QueueDragMove(int cells) => _drag.Queue(cells);
    /// <summary>Cancel any queued drag steps (e.g. when the piece locks under the finger).</summary>
    public void ClearDragMove() => _drag.Clear();

    /// <summary>Discard all pending input (edges, held touch, drag). Used on revive so a
    /// gesture made while topped-out can't fire on the fresh board.</summary>
    public void Reset()
    {
        _latched = Buttons.None;
        _touchLeft = _touchRight = _touchSoft = false;
        _drag.Clear();
    }

    /// <summary>Build this tick's button mask and consume any latched edges / drag steps.</summary>
    public Buttons Sample()
    {
        Buttons b = _latched;
        _latched = Buttons.None;

        if (Input.IsActionPressed("move_left") || _touchLeft) b |= Buttons.Left;
        if (Input.IsActionPressed("move_right") || _touchRight) b |= Buttons.Right;
        if (Input.IsActionPressed("soft_drop") || _touchSoft) b |= Buttons.Soft;

        // Drag steps yield to any tick that already claims input: a held direction
        // (keyboard/d-pad) would fight the drain, and a Hard/Hold locks+respawns —
        // a leftover step must not carry over and shove the fresh piece (it's dropped
        // by the ClearDragMove that fires on spawn). Otherwise drain one crisp step.
        if ((b & (Buttons.Left | Buttons.Right | Buttons.Hard | Buttons.Hold)) == 0)
            b |= _drag.Next();
        else
            _drag.ForceGap(); // when the claim clears, the next step is still a fresh tap

        return b;
    }
}
