using Godot;
using Blockfall.Core;

namespace Blockfall.Gameplay;

/// <summary>
/// Translates raw input into Blockfall.Core commands with proper handling feel:
/// DAS (Delayed Auto Shift) and ARR (Auto Repeat Rate) for horizontal movement,
/// held soft drop, and edge-triggered rotations / hard drop / hold.
///
/// Keyboard + gamepad go through the project input map. Touch buttons call the
/// public Press*/Release* methods directly, so both paths share identical timing.
/// </summary>
public sealed class InputController
{
    private readonly GameConfig _config;
    // Optional finesse tracker: this is the ONE place tap-vs-hold is distinguishable
    // (the engine only sees repeated Move commands), so we feed it semantic actions here.
    private readonly FinesseTracker? _finesse;
    private bool _softHeld;

    // Horizontal auto-shift state (-1 left, +1 right, 0 none).
    private int _dir;
    private double _dasTimer;
    private bool _dasCharged;
    private double _arrTimer;

    // Whether the current direction / soft drop is driven by touch buttons (which
    // fire press/release events) rather than the polled input map. Without this the
    // per-frame keyboard poll in Update() would immediately cancel touch input.
    private bool _touchDir;
    private bool _touchSoft;

    // Discrete-action edge latches for touch (keyboard uses IsActionJustPressed).
    public InputController(GameConfig config, FinesseTracker? finesse = null)
    {
        _config = config;
        _finesse = finesse;
    }

    // ---- Touch / virtual button hooks ------------------------------------
    public void PressLeft(Game g) { _dir = -1; _touchDir = true; ResetDas(); _finesse?.OnTapMove(-1); g.MoveLeft(); }
    public void PressRight(Game g) { _dir = 1; _touchDir = true; ResetDas(); _finesse?.OnTapMove(1); g.MoveRight(); }
    public void ReleaseHorizontal(int released)
    {
        // Only clear if the released direction is the one currently active.
        if (_dir == released) { _dir = 0; _dasCharged = false; _touchDir = false; }
    }
    public void PressSoftDrop(Game g) => _touchSoft = true;
    public void ReleaseSoftDrop(Game g) => _touchSoft = false;

    /// <summary>
    /// Reconcile finesse accounting at a piece boundary (call right after the tracker's
    /// BeginPiece). A direction or soft drop HELD across the previous piece's lock leaves
    /// no fresh press / DAS-charge / rising-edge event on the new piece, so the carried
    /// inputs must be credited here — otherwise a carried DAS records zero presses
    /// (under-counting faults) and a carried soft drop never arms the tuck rule (charging
    /// false faults on a legitimate tuck).
    /// </summary>
    public void SyncFinesseOnPieceStart()
    {
        if (_finesse is null) return;
        // A fully-charged DAS keeps sliding the new piece with no new event — one input.
        // (Credit the move BEFORE arming soft drop, so a clean carried slide isn't a "tuck".)
        if (_dir != 0 && _dasCharged) _finesse.OnDasMove(_dir);
        if (_softHeld) _finesse.OnSoftDrop();
    }

    private void ResetDas() { _dasTimer = 0; _arrTimer = 0; _dasCharged = false; }

    /// <summary>Poll continuous inputs (movement auto-shift, soft drop) each frame.</summary>
    public void Update(double delta, Game g)
    {
        // Resolve held horizontal direction from the input map (keyboard/gamepad).
        bool left = Input.IsActionPressed("move_left");
        bool right = Input.IsActionPressed("move_right");
        int held = (right ? 1 : 0) - (left ? 1 : 0);

        if (held != 0 && held != _dir)
        {
            // New keyboard/gamepad direction pressed this frame: immediate step, charge DAS.
            _dir = held;
            _touchDir = false;
            ResetDas();
            _finesse?.OnTapMove(held);
            if (held < 0) g.MoveLeft(); else g.MoveRight();
        }
        else if (held == 0 && !_touchDir)
        {
            // No key held and touch isn't driving: clear. (Touch is cleared by ReleaseHorizontal.)
            _dir = 0;
            _dasCharged = false;
        }

        // Auto-shift progression while a direction is held.
        if (_dir != 0)
        {
            if (!_dasCharged)
            {
                _dasTimer += delta;
                if (_dasTimer >= _config.Das)
                {
                    _dasCharged = true;
                    _arrTimer = 0;
                    // Exactly one DAS engagement per hold (ResetDas clears _dasCharged on the next press).
                    _finesse?.OnDasMove(_dir);
                }
            }
            else
            {
                _arrTimer += delta;
                // ARR of 0 means "teleport to wall": repeat until it can't move.
                if (_config.Arr <= 0)
                {
                    for (int i = 0; i < 32; i++)
                        if (!(_dir < 0 ? g.MoveLeft() : g.MoveRight())) break;
                }
                else
                {
                    while (_arrTimer >= _config.Arr)
                    {
                        _arrTimer -= _config.Arr;
                        if (!(_dir < 0 ? g.MoveLeft() : g.MoveRight())) break;
                    }
                }
            }
        }

        // Soft drop is a held state — engaged by either the key OR the touch button.
        bool soft = _touchSoft || Input.IsActionPressed("soft_drop");
        if (soft && !_softHeld) _finesse?.OnSoftDrop(); // rising edge arms the tuck rule
        _softHeld = soft;
        g.SetSoftDrop(soft);
    }

    /// <summary>Handle edge-triggered actions (rotate / hard drop / hold / pause).</summary>
    public void HandleEvent(InputEvent e, Game g, System.Action onPause)
    {
        if (e.IsActionPressed("rotate_cw")) g.RotateCw();
        else if (e.IsActionPressed("rotate_ccw")) g.RotateCcw();
        else if (e.IsActionPressed("rotate_180")) g.Rotate180();
        else if (e.IsActionPressed("hard_drop")) g.HardDrop();
        else if (e.IsActionPressed("hold_piece")) g.HoldPiece();
        else if (e.IsActionPressed("pause_game")) onPause();
    }
}
