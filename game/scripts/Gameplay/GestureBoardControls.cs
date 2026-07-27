using Godot;
using System;
using Blockfall.Core.Localization;
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
/// (rotate-CCW, hold) plus a pause button, since gestures alone hide them. The
/// cluster is laid out from the LIVE board rect into the thumb band reserved under
/// the playfield — never on top of it.
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
    // Where recognised actions go: the solo game's deterministic ButtonSampler, or CPU
    // versus's live Game. The sink also answers "is the run playable right now?" — gestures
    // and aux taps are ignored otherwise, so nothing fires on a later resume/revive.
    private readonly IGestureSink _sink;

    public event Action? PauseRequested;

    private bool CanPlay => _sink.CanPlay;

    // Active primary gesture (one finger; extra fingers are ignored by the board surface).
    private int _touchId = -1;
    private Vector2 _downPos, _lastPos;
    private ulong _downMs;
    private float _accumX;   // horizontal px not yet converted into a whole-cell step
    private float _travel;   // total path length — distinguishes a tap from a drag
    private bool _soft;      // is this drag currently holding soft drop?

    private Label? _hint;

    // Aux cluster. 84px = 44pt on the 720×1280 design canvas (1 design px ≈ 0.52pt on an
    // iPhone SE3) — the platform minimum touch target, which 76/60px buttons missed.
    private const float TouchTarget = 84f;
    private const float AuxGap = 10f;     // between two buttons
    private const float AuxMargin = 8f;   // from the canvas edge
    private Button _ccwBtn = null!, _holdBtn = null!, _pauseBtn = null!;
    // Placement is derived from the live board rect, so remember what it was computed for
    // and redo it only when the board or the canvas actually moves (Hud.WantStrip pattern).
    private Rect2 _placedForBoard;
    private Vector2 _placedForCanvas;

    public GestureBoardControls(BoardView view, IGestureSink sink)
    {
        _view = view;
        _sink = sink;
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
        while (_accumX >= cell) { _sink.StepHorizontal(1); _accumX -= cell; }
        while (_accumX <= -cell) { _sink.StepHorizontal(-1); _accumX += cell; }

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
            _sink.RotateCw();
            return;
        }

        // A fast upward flick → hold (swap the piece away).
        bool verticalDominant = Mathf.Abs(disp.Y) > Mathf.Abs(disp.X) * 1.3f;
        float vy = disp.Y / dur * 1000f; // px/s, signed (+ down)
        if (verticalDominant && disp.Y < -cell * FlickMinCells && -vy > cell * FlickSpeedPerCell)
        {
            _sink.Hold();
            return;
        }

        // Otherwise you dragged the piece to line it up — lifting your finger PLACES
        // it: hard-drop into the chosen column, puzzle-style. This is the "drag to
        // fit, lift to drop" control the player asked for (#6, Tier A).
        _sink.HardDrop();
    }

    private void SetSoft(bool on)
    {
        if (_soft == on) return;
        _soft = on;
        _sink.SetSoftDrop(on);
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
        _ccwBtn = MakeGlassButton("↺", (int)TouchTarget, () => { if (CanPlay) _sink.RotateCcw(); });
        _holdBtn = MakeGlassButton("HOLD", (int)TouchTarget, () => { if (CanPlay) _sink.Hold(); });
        _pauseBtn = MakeGlassButton("II", (int)TouchTarget, () => { if (CanPlay) PauseRequested?.Invoke(); });
        AddChild(_ccwBtn);
        AddChild(_holdBtn);
        AddChild(_pauseBtn);
        LayoutAux();
    }

    // Re-place only when the board or the canvas actually moved (a resize, or the very
    // first frame where the board has been laid out but this node had not been yet).
    public override void _Process(double delta)
    {
        if (_view.CellsRect() != _placedForBoard || Size != _placedForCanvas) LayoutAux();
    }

    /// <summary>
    /// Position the aux cluster so it never covers a playfield cell. The mobile board
    /// layout reserves <see cref="BoardView.MobileThumbBandPx"/> under the playfield, so the
    /// buttons form a row down there: pause far-left (rare, weak-hand side), rotate-CCW and
    /// HOLD under the right thumb. Anchors alone can't express this — the band moves with
    /// the board — and the previous fixed BottomRight offsets put the cluster's top edge
    /// ABOVE the board's bottom edge on every phone aspect, hiding the bottom row.
    /// </summary>
    private void LayoutAux()
    {
        var canvas = Size;
        var board = _view.CellsRect();
        _placedForCanvas = canvas;
        _placedForBoard = board;
        if (canvas.X <= 0f || canvas.Y <= 0f || board.Size.X <= 0f) return;

        float top = canvas.Y - AuxMargin - TouchTarget;     // the row's top edge
        float rightX = canvas.X - AuxMargin - TouchTarget;

        if (top >= board.End.Y)
        {
            PlaceAt(_pauseBtn, new Vector2(AuxMargin, top));
            PlaceAt(_holdBtn, new Vector2(rightX, top));
            PlaceAt(_ccwBtn, new Vector2(rightX - TouchTarget - AuxGap, top));
        }
        else
        {
            // No band under the board (a canvas the phone layout never produces — e.g. a
            // squat resized desktop window): stack beside the board instead, which hides
            // nothing either as long as the side gutters are wide enough.
            PlaceAt(_holdBtn, new Vector2(rightX, top));
            PlaceAt(_ccwBtn, new Vector2(rightX, top - TouchTarget - AuxGap));
            PlaceAt(_pauseBtn, new Vector2(AuxMargin, top));
        }
        LayoutHint(canvas, top);
    }

    private static void PlaceAt(Control c, Vector2 pos)
    {
        c.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft); // offsets too — anchors alone collapse to 0×0
        // A Control never shrinks below its own minimum size, so bottom-align against the
        // real size rather than the nominal one (a taller button would run off-canvas).
        var min = c.GetCombinedMinimumSize();
        var size = new Vector2(Mathf.Max(TouchTarget, min.X), Mathf.Max(TouchTarget, min.Y));
        c.Size = size;
        c.Position = new Vector2(pos.X, pos.Y + TouchTarget - size.Y);
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

    // ---- First-run hint ----------------------------------------------------

    private void BuildHint()
    {
        _hint = new Label
        {
            Text = Loc.T("DRAG TO LINE UP   ·   LIFT TO DROP   ·   TAP TO ROTATE"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _hint.AddThemeFontSizeOverride("font_size", 16);
        _hint.AddThemeColorOverride("font_color", new Color(Palette.TextPrimary.R, Palette.TextPrimary.G, Palette.TextPrimary.B, 0.85f));
        AddChild(_hint);
        LayoutHint(Size, Size.Y - AuxMargin - TouchTarget);

        // Auto-dismiss even if the player never touches (fades on first gesture too). The
        // SceneTreeTimer outlives this node, so guard against firing after we're freed.
        GetTree().CreateTimer(3.5).Timeout += () => { if (IsInstanceValid(this)) FadeHint(); };
    }

    /// <summary>Park the control hint in the gap BETWEEN the aux buttons in the thumb band,
    /// so the one thing that explains the controls never sits on the playfield (nor under
    /// the hand that is about to reach for HOLD).</summary>
    private void LayoutHint(Vector2 canvas, float rowTop)
    {
        if (_hint is null || !IsInstanceValid(_hint) || canvas.X <= 0f) return;
        float left = AuxMargin + TouchTarget + AuxGap;                       // right of pause
        float right = canvas.X - AuxMargin - TouchTarget * 2f - AuxGap * 2f; // left of rotate
        float w = Mathf.Max(180f, right - left);
        _hint.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        // Wrap inside that gap rather than spilling over the buttons — Korean runs much
        // wider than the English source here. The width is explicit, so the wrapped height
        // is measurable (a zero-width autowrap label reports a nonsense minimum).
        _hint.CustomMinimumSize = new Vector2(w, 0);
        float h = Mathf.Max(TouchTarget, _hint.GetCombinedMinimumSize().Y);
        _hint.Size = new Vector2(w, h);
        _hint.Position = new Vector2(left, Mathf.Min(rowTop, canvas.Y - h - AuxMargin));
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
