using Godot;
using System;

namespace Blockfall.UI;

/// <summary>
/// A <see cref="ScrollContainer"/> you can drag ANYWHERE — over a card, over a button, over a
/// preview — not only in the gaps between them.
///
/// <para>WHY THE STOCK CONTAINER CANNOT DO THIS. Godot routes GUI input to the deepest Control
/// under the finger and stops at the first one whose <c>mouse_filter</c> is STOP — which is the
/// default for <see cref="PanelContainer"/> and every <see cref="Button"/>. In a list of cards
/// that means the scroll container only ever sees the drags that land in the dead space between
/// rows, so a store full of cards scrolls only from its margins. That is exactly the bug this
/// class exists to kill, and it is structural: no arrangement of mouse_filter fixes it without
/// either breaking taps (PASS lets a drag also fire the button under it) or breaking scrolling.</para>
///
/// <para>HOW THIS WORKS. Scrolling is lifted out of the GUI dispatch entirely and implemented in
/// <see cref="_Input"/>, which runs BEFORE any control's <c>_gui_input</c> and sees every event
/// regardless of what is under the finger. Nothing is ever consumed — buttons still highlight and
/// still fire — so the only thing left to solve is the accidental tap at the end of a drag, and
/// <see cref="Dragged"/> answers that: it goes true the moment the gesture crosses
/// <see cref="DragThreshold"/> and stays true until the next press, so a <c>Pressed</c> handler
/// (which fires on RELEASE, after we have already seen it) can tell a tap from a fling.
/// <see cref="Bind"/> wires that check for you.</para>
///
/// <para>The container's own <c>mouse_filter</c> is set to IGNORE so the base class's scrolling
/// never runs. Otherwise a drag landing in a gap would be scrolled twice — once by the base
/// class and once by us — and move at double speed depending on WHERE the player grabbed, which
/// is worse than not scrolling at all. That makes this class the single implementation of
/// scrolling for its subtree, so it owns the mouse wheel too. (Setting IGNORE on a container does
/// not affect its children: Godot skips the node itself as a hit candidate and keeps testing
/// descendants, so every button inside still receives input normally.)</para>
/// </summary>
public partial class TouchScroll : ScrollContainer
{
    /// <summary>Travel before a press is reinterpreted as a scroll. 10 design px ≈ 5pt — under
    /// the ~9pt of finger roll a deliberate tap produces, so this never steals a tap, and small
    /// enough that the list starts moving while the player still thinks of it as one motion.</summary>
    private const float DragThreshold = 10f;

    /// <summary>A wheel notch. Matches the ~3 lines desktop users expect at this row height.</summary>
    private const float WheelStep = 96f;

    /// <summary>Fling decay per second (exponential). 6.5 gives ≈0.6s of glide from a firm
    /// flick — long enough to cross a screen of cards, short enough that the list never feels
    /// like it is ignoring you.</summary>
    private const float Friction = 6.5f;

    /// <summary>Below this speed (px/s) the fling is over. Prevents a permanently "moving" list
    /// that never lets _Process go back to sleep.</summary>
    private const float MinFling = 14f;

    /// <summary>A gesture must be this much more vertical than horizontal to scroll. Keeps a
    /// deliberate horizontal drag — a settings slider, a future carousel — out of the list.</summary>
    private const float VerticalBias = 1.15f;

    /// <summary>How long the finger may sit still before the release stops counting as a throw.
    /// 90ms is about one and a half samples at a 60Hz touch rate — long enough that ordinary
    /// sampling gaps mid-drag do not kill a real fling, short enough that a deliberate stop does.</summary>
    private const float StaleFlick = 0.09f;

    private bool _down;
    private bool _dragged;
    private int _touchId = int.MaxValue;
    private Vector2 _start;
    private float _startScroll;
    private float _lastY;
    private float _lastT;
    private float _velocity;

    /// <summary>Accumulated dt — the timebase for fling velocity. Never wall-clock: view code in
    /// this repo reads elapsed time from _Process only (same rule the board renderers follow).</summary>
    private float _t;

    /// <summary>True when the gesture in progress (or the one that just ended) was a scroll
    /// rather than a tap. Cleared on the next press. Read it in a <c>Pressed</c> handler to
    /// discard the click that would otherwise end every fling.</summary>
    public bool Dragged => _dragged;

    public override void _Ready()
    {
        // See the class doc: switching the base class's gui_input off makes this the single
        // implementation of scrolling for the subtree.
        MouseFilter = MouseFilterEnum.Ignore;
        SetProcess(false);
    }

    /// <summary>
    /// Wire a button so it ignores the "click" at the end of a scroll gesture. Finds the
    /// enclosing <see cref="TouchScroll"/> at press time by walking ancestors, so rows do not
    /// have to be handed a reference to the container they will eventually be added to (store
    /// rows are built before they are parented).
    /// </summary>
    public static void Bind(BaseButton button, Action onPressed)
        => button.Pressed += () => { if (!Enclosing(button)?.Dragged ?? true) onPressed(); };

    /// <summary>
    /// The <c>Toggled</c> twin of <see cref="Bind"/>, for switches and toggle pills. A toggle
    /// cannot simply be ignored the way a press can — Godot has already flipped
    /// <see cref="BaseButton.ButtonPressed"/> by the time the signal fires — so a scroll gesture
    /// puts it back with <c>SetPressedNoSignal</c> (no re-entrant Toggled) and returns before the
    /// caller's handler runs, leaving both the state and the visuals untouched.
    /// </summary>
    public static void BindToggle(BaseButton button, Action<bool> onToggled)
        => button.Toggled += on =>
        {
            if (Enclosing(button)?.Dragged == true) { button.SetPressedNoSignal(!on); return; }
            onToggled(on);
        };

    private static TouchScroll? Enclosing(Node n)
    {
        for (Node? p = n.GetParent(); p is not null; p = p.GetParent())
            if (p is TouchScroll ts) return ts;
        return null;
    }

    /// <summary>Runs while a finger is down (to keep <see cref="_t"/> advancing — velocity is
    /// measured against it, and a frozen clock would report every sample as infinitely fast) and
    /// while a fling decays. Sleeps otherwise.</summary>
    public override void _Process(double delta)
    {
        _t += (float)delta;
        if (_down) return;
        if (Mathf.Abs(_velocity) < MinFling) { _velocity = 0f; SetProcess(false); return; }
        ScrollBy(-_velocity * (float)delta);
        _velocity *= Mathf.Exp(-Friction * (float)delta);
    }

    public override void _Input(InputEvent e)
    {
        // MOBILE DELIVERS EVERY GESTURE TWICE. Godot's emulate_mouse_from_touch (on by default,
        // and required — BaseButton only understands mouse events) synthesizes a mouse press/motion
        // beside every ScreenTouch/ScreenDrag, and BOTH reach _Input. Rather than sniff the device
        // (the emulation id is not exposed as a C# constant, so that guard would be a magic -1),
        // the three handlers below are written to be IDEMPOTENT under a repeated event:
        //   * Press  — refuses to re-enter while a gesture is live (a second Press would recompute
        //              the fling-catch flag from a velocity the first one already zeroed, which
        //              silently disabled the whole tap-vs-scroll guard on every touch device),
        //   * Move   — scroll offset is absolute from the gesture's start, and the velocity update
        //              ignores a zero-delta repeat (otherwise every real sample was followed by a
        //              damping one and mobile flings ran at ~⅓ speed),
        //   * Release — early-outs once the gesture is already closed.
        switch (e)
        {
            case InputEventScreenTouch t:
                if (t.Pressed) { if (_touchId == int.MaxValue) { _touchId = (int)t.Index; Press(t.Position); } }
                else if ((int)t.Index == _touchId) { _touchId = int.MaxValue; Release(); }
                break;

            case InputEventScreenDrag d when (int)d.Index == _touchId:
                Move(d.Position);
                break;

            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Left:
                if (mb.Pressed) Press(mb.Position); else Release();
                break;

            case InputEventMouseButton wheel when wheel.Pressed
                    && wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown:
                if (!Inside(wheel.Position)) return;
                _velocity = 0f;
                ScrollBy(wheel.ButtonIndex == MouseButton.WheelUp ? -WheelStep : WheelStep);
                break;

            case InputEventMouseMotion mm when _down && (mm.ButtonMask & MouseButtonMask.Left) != 0:
                Move(mm.Position);
                break;
        }
    }

    private bool Inside(Vector2 p)
        => IsVisibleInTree() && GetGlobalRect().HasPoint(p);

    private void Press(Vector2 p)
    {
        if (_down) return;   // the twin event of the same finger-down — see the note in _Input
        // A control that owns drags starting on it wins outright; the list does not also scroll.
        if (!Inside(p) || ClaimedByChild(this, p)) { _down = false; _dragged = false; return; }
        _down = true;
        SetProcess(true);   // the clock must run for the whole gesture, not just the fling
        // A press that lands on a moving list STOPS it and counts as a drag, so the tap that
        // catches a fling never also activates whatever card it landed on. This is the one
        // gesture where "did the player mean to press this?" has an unambiguous answer.
        _dragged = Mathf.Abs(_velocity) >= MinFling;
        _velocity = 0f;
        _start = p;
        _lastY = p.Y;
        _lastT = _t;
        _startScroll = ScrollVertical;
    }

    private void Move(Vector2 p)
    {
        if (!_down) return;
        var d = p - _start;
        if (!_dragged)
        {
            if (Mathf.Abs(d.Y) < DragThreshold || Mathf.Abs(d.Y) < Mathf.Abs(d.X) * VerticalBias) return;
            _dragged = true;
        }
        SetScroll(_startScroll - d.Y);

        // The repeat of the same sample carries no motion. Folding it in would lerp the velocity
        // toward zero once per real sample — a 0.45× damp every frame, i.e. mobile flings at
        // roughly a third of the speed the finger actually threw.
        if (p.Y == _lastY) return;

        float dt = Mathf.Max(_t - _lastT, 1f / 240f);
        // Smoothed so one jittery sample cannot launch a fling the player did not throw.
        _velocity = Mathf.Lerp(_velocity, (p.Y - _lastY) / dt, 0.55f);
        _lastY = p.Y;
        _lastT = _t;
    }

    private void Release()
    {
        if (!_down) return;
        _down = false;
        if (!_dragged) { _velocity = 0f; return; }
        // A finger that came to rest before lifting is not throwing anything. No drag event is
        // emitted while it sits still, so the last measured velocity would otherwise survive the
        // pause and the list would take off from a deliberate stop.
        if (_t - _lastT > StaleFlick) _velocity = 0f;
        if (Mathf.Abs(_velocity) >= MinFling) SetProcess(true);
    }

    /// <summary>
    /// Jump to the top and cancel any glide. For a CONTENT SWAP (a store tab change): setting
    /// <see cref="ScrollContainer.ScrollVertical"/> alone leaves the fling running in
    /// <see cref="_Process"/>, which then drags the freshly-built list off its first row — the
    /// exact thing "start the new shelf at its top" is meant to guarantee.
    /// </summary>
    public void ResetToTop()
    {
        _velocity = 0f;
        _dragged = false;
        ScrollVertical = 0;
    }

    /// <summary>
    /// True when the press landed on a widget that OWNS the drag starting on it. Today that means
    /// any <see cref="Slider"/>: Godot snaps a slider to the press position and then tracks the
    /// motion, and this class consumes nothing, so without this a drag across a settings slider
    /// would scroll the list AND rewrite the setting on the way past.
    ///
    /// <para>Walked at press time — once per gesture, never per frame — rather than kept as a
    /// registry, so a slider added to any screen later is respected with no extra wiring. Invisible
    /// subtrees are skipped, which also covers the hidden tabs of a TabContainer.</para>
    /// </summary>
    private static bool ClaimedByChild(Node n, Vector2 p)
    {
        foreach (var child in n.GetChildren())
        {
            if (child is Control c)
            {
                if (!c.IsVisibleInTree()) continue;
                if (c is Slider && c.GetGlobalRect().HasPoint(p)) return true;
            }
            if (ClaimedByChild(child, p)) return true;
        }
        return false;
    }

    private void ScrollBy(float dy) => SetScroll(ScrollVertical + dy);

    private void SetScroll(float v)
    {
        var bar = GetVScrollBar();
        // Clamp here rather than trusting the setter: a fling that runs past the end would keep
        // its velocity against a silently clamped value and "stick" to the edge for the rest of
        // its decay instead of stopping.
        float max = bar is null ? 0f : Mathf.Max(0f, (float)(bar.MaxValue - bar.Page));
        float clamped = Mathf.Clamp(v, 0f, max);
        if (clamped != v) _velocity = 0f;
        ScrollVertical = Mathf.RoundToInt(clamped);
    }
}
