using Godot;
using System;
using Blockfall.Theme;

namespace Blockfall.Gameplay;

/// <summary>
/// Direct-manipulation touch controls: instead of a static d-pad, you grab the
/// board and slide the falling piece into place with your finger. Every gesture is
/// funnelled through the same <see cref="ButtonSampler"/> the keyboard uses, so a
/// touch-played run records and replays bit-for-bit identically.
///
/// Gestures (anywhere over the play field):
///  • horizontal drag → the piece steps column-by-column under your finger
///  • quick tap → rotate clockwise
///  • slow drag downward → soft drop; fast flick downward → hard drop
///  • fast flick upward → hold
/// A tiny translucent cluster keeps the less-frequent actions one tap away
/// (rotate-CCW, hold) plus a pause button, since gestures alone hide them.
///
/// Handles real touch (screen touch/drag) AND mouse (so the desktop F9 mobile
/// preview is fully playable). It never blocks input — it reads via
/// <see cref="_UnhandledInput"/>, and the aux buttons consume their own taps.
/// </summary>
public partial class GestureBoardControls : Control
{
    // Gesture tuning, all expressed in CELLS / milliseconds so they scale with board size.
    private const float TapMaxTravelCells = 0.5f;  // a "tap" barely moves
    private const ulong TapMaxMs = 250;            // …and is quick
    private const float SoftEngageCells = 0.55f;   // drag this far down (vertically) → soft drop
    private const float FlickMinCells = 1.2f;      // a flick must cover at least this much
    private const float FlickSpeedPerCell = 10f;   // …at ≥ cell*10 px/s to count (else it's a slow drag)
    private const int MouseId = -2;                // synthetic finger id for desktop mouse

    private readonly BoardView _view;
    private readonly ButtonSampler _sampler;
    // True only while the run is actually playable (not paused / over / finished). Gestures
    // and aux taps are ignored otherwise, so nothing latches to fire on a later resume/revive.
    private readonly Func<bool> _canPlay;

    public event Action? PauseRequested;

    private bool CanPlay => _canPlay();

    // Active primary gesture (one finger; extra fingers are ignored by the board surface).
    private int _touchId = -1;
    private Vector2 _downPos, _lastPos;
    private ulong _downMs;
    private float _accumX;   // horizontal px not yet converted into a whole-cell step
    private float _travel;   // total path length — distinguishes a tap from a drag
    private bool _soft;      // is this drag currently holding soft drop?

    private Label? _hint;

    public GestureBoardControls(BoardView view, ButtonSampler sampler, Func<bool> canPlay)
    {
        _view = view;
        _sampler = sampler;
        _canPlay = canPlay;
    }

    public override void _Ready()
    {
        UiTheme.ApplyTo(this); // hangs off a Node2D controller — no theme inheritance
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore; // transparent to picking; we read via _UnhandledInput
        BuildAuxCluster();
        BuildHint();
    }

    private float Cell => Mathf.Max(1f, _view.CellSize);

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventScreenTouch t:
                if (t.Pressed) BeginGesture((int)t.Index, t.Position);
                else EndGesture((int)t.Index, t.Position);
                break;
            case InputEventScreenDrag d:
                MoveGesture((int)d.Index, d.Position);
                break;
            // Desktop mouse = one synthetic finger, so the F9 preview is playable.
            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Left:
                if (mb.Pressed) BeginGesture(MouseId, mb.Position);
                else EndGesture(MouseId, mb.Position);
                break;
            case InputEventMouseMotion mm when (mm.ButtonMask & MouseButtonMask.Left) != 0:
                MoveGesture(MouseId, mm.Position);
                break;
        }
    }

    private void BeginGesture(int id, Vector2 pos)
    {
        if (_touchId != -1) return; // already tracking a primary finger
        if (!CanPlay) return;       // ignore input while paused / over / finished
        _touchId = id;
        _downPos = _lastPos = pos;
        _downMs = Time.GetTicksMsec();
        _accumX = 0f;
        _travel = 0f;
        _soft = false;
        FadeHint();
    }

    private void MoveGesture(int id, Vector2 pos)
    {
        if (id != _touchId) return;
        // Game left Running mid-drag (game over / pause): abandon this gesture entirely.
        // A held finger then goes inert until lifted and re-pressed, so a continuous hold
        // across a revive can't burst a stale delta into the fresh piece — the next
        // BeginGesture re-initializes _downPos/_lastPos/_accumX from scratch.
        if (!CanPlay) { SetSoft(false); _touchId = -1; return; }
        float cell = Cell;
        Vector2 rel = pos - _lastPos;
        _lastPos = pos;
        _travel += rel.Length();

        // Horizontal: turn accumulated px into whole-cell steps (the sampler drains them
        // one crisp tap at a time — see DragStepper).
        _accumX += rel.X;
        while (_accumX >= cell) { _sampler.QueueDragMove(1); _accumX -= cell; }
        while (_accumX <= -cell) { _sampler.QueueDragMove(-1); _accumX += cell; }

        // Vertical soft drop: only when the drag is genuinely downward-dominant, so a
        // sideways slide (which may dip a little) never soft-drops by accident.
        Vector2 fromStart = pos - _downPos;
        bool downDominant = fromStart.Y > Mathf.Abs(fromStart.X);
        SetSoft(downDominant && fromStart.Y > cell * SoftEngageCells);
    }

    private void EndGesture(int id, Vector2 pos)
    {
        if (id != _touchId) return;
        _touchId = -1;
        SetSoft(false);       // always release soft, even if the game just left Running
        if (!CanPlay) return; // …but don't latch a tap/flick action into a paused/over game

        float cell = Cell;
        float dur = Mathf.Max(1f, Time.GetTicksMsec() - _downMs);
        Vector2 disp = pos - _downPos;

        // A quick, near-stationary touch is a tap → rotate CW (never places).
        if (_travel < cell * TapMaxTravelCells && dur < TapMaxMs)
        {
            _sampler.LatchRotateCw();
            return;
        }

        // A fast upward flick → hold (swap the piece away).
        bool verticalDominant = Mathf.Abs(disp.Y) > Mathf.Abs(disp.X) * 1.3f;
        float vy = disp.Y / dur * 1000f; // px/s, signed (+ down)
        if (verticalDominant && disp.Y < -cell * FlickMinCells && -vy > cell * FlickSpeedPerCell)
        {
            _sampler.LatchHold();
            return;
        }

        // Otherwise you dragged the piece to line it up — lifting your finger PLACES
        // it: hard-drop into the chosen column, puzzle-style. This is the "drag to
        // fit, lift to drop" control the player asked for (#6, Tier A).
        _sampler.LatchHardDrop();
    }

    private void SetSoft(bool on)
    {
        if (_soft == on) return;
        _soft = on;
        _sampler.SetTouchSoft(on);
    }

    /// <summary>
    /// Forget any in-flight gesture (called on revive). Whether or not the finger was
    /// moving when the board reset, the tracked touch is dropped and soft released, so a
    /// still-held finger can't drive the fresh board via stale _touchId/_lastPos — a new
    /// touch must re-arm. Complements <see cref="ButtonSampler.Reset"/> (which clears the
    /// sampler but not this node's finger-tracking state).
    /// </summary>
    public void CancelGesture()
    {
        _touchId = -1;
        SetSoft(false);
    }

    // ---- Aux cluster (rotate-CCW / hold / pause) ---------------------------

    private void BuildAuxCluster()
    {
        // Gated on CanPlay so a tap over the pause/revive overlay can't queue an action.
        var ccw = MakeGlassButton("↺", 76, () => { if (CanPlay) _sampler.LatchRotateCcw(); });
        var hold = MakeGlassButton("HOLD", 88, () => { if (CanPlay) _sampler.LatchHold(); });
        var pause = MakeGlassButton("II", 60, () => { if (CanPlay) PauseRequested?.Invoke(); });

        Place(ccw, LayoutPreset.BottomRight, new Vector2(-210, -116));
        Place(hold, LayoutPreset.BottomRight, new Vector2(-112, -116));
        // Pause lives bottom-LEFT: the top-right corner is taken by the NEXT card
        // (they overlapped and the button clipped the screen edge).
        Place(pause, LayoutPreset.BottomLeft, new Vector2(24, -116));
    }

    private Button MakeGlassButton(string glyph, int size, Action onDown)
    {
        var b = new Button
        {
            Text = glyph,
            CustomMinimumSize = new Vector2(size, size),
            Modulate = new Color(1, 1, 1, 0.7f), // quieter than the piece — gestures are the main act
        };
        b.AddThemeStyleboxOverride("normal", CircleStyle(
            TextureFactory.Circle(96, new Color(0.72f, 0.76f, 1f, 0.06f), new Color(1, 1, 1, 0.14f), 1.5f)));
        b.AddThemeStyleboxOverride("hover", CircleStyle(
            TextureFactory.Circle(96, new Color(0.72f, 0.76f, 1f, 0.09f), new Color(1, 1, 1, 0.20f), 1.5f)));
        b.AddThemeStyleboxOverride("pressed", CircleStyle(
            TextureFactory.Circle(96, new Color(Palette.Accent.R, Palette.Accent.G, Palette.Accent.B, 0.28f),
                                       new Color(Palette.Accent.R, Palette.Accent.G, Palette.Accent.B, 0.9f), 2f)));
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        b.AddThemeFontSizeOverride("font_size", (int)(size * 0.34f));
        b.AddThemeColorOverride("font_color", Palette.TextPrimary);
        b.AddThemeColorOverride("font_pressed_color", Colors.White);
        Motion.BindButtonFeel(b);
        b.ButtonDown += () => onDown();
        return b;
    }

    private static StyleBoxTexture CircleStyle(Texture2D tex) => new() { Texture = tex };

    private void Place(Control c, LayoutPreset preset, Vector2 offset)
    {
        c.SetAnchorsPreset(preset);
        c.Position = offset;
        AddChild(c);
    }

    // ---- First-run hint ----------------------------------------------------

    private void BuildHint()
    {
        _hint = new Label
        {
            Text = "DRAG TO LINE UP   ·   LIFT TO DROP   ·   TAP TO ROTATE",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _hint.AddThemeFontSizeOverride("font_size", 16);
        _hint.AddThemeColorOverride("font_color", new Color(Palette.TextPrimary.R, Palette.TextPrimary.G, Palette.TextPrimary.B, 0.85f));
        _hint.SetAnchorsPreset(LayoutPreset.CenterBottom);
        _hint.Position = new Vector2(-320, -96);
        _hint.CustomMinimumSize = new Vector2(640, 0);
        AddChild(_hint);

        // Auto-dismiss even if the player never touches (fades on first gesture too). The
        // SceneTreeTimer outlives this node, so guard against firing after we're freed.
        GetTree().CreateTimer(3.5).Timeout += () => { if (IsInstanceValid(this)) FadeHint(); };
    }

    private void FadeHint()
    {
        if (_hint is null) return;
        var h = _hint;
        _hint = null;
        if (Motion.Reduced) { h.QueueFree(); return; }
        var tw = CreateTween();
        tw.TweenProperty(h, "modulate:a", 0f, 0.4f);
        tw.TweenCallback(Callable.From(h.QueueFree));
    }
}
