using Godot;
using System;
using Blockfall.Audio;
using Blockfall.Core;
using Blockfall.Core.BlockFit;
using Blockfall.Core.Localization;
using Blockfall.Theme;

namespace Blockfall.Gameplay;

/// <summary>
/// Free-placement block puzzle mode (Block Blast style): a 10×10 grid and a tray
/// of 3 fixed-orientation neon pieces you drag anywhere they fit (no gravity, no
/// rotation). Fill a full row/column to clear it. The tray is a continuous stream —
/// placing a piece refills its slot instantly. A reserve HOLD slot (left of the tray)
/// parks an awkward piece for later (drag a tray piece onto it; drag it back out to place).
/// The dragged piece floats ABOVE the finger, and while dragging a top-of-screen MAGNIFIER
/// (loupe) shows the landing area zoomed so the fingertip never hides where it lands — the
/// key mobile control details. Engine is the tested <see cref="BlockFitGame"/>; render + touch here.
/// </summary>
public partial class BlockFitController : Node2D
{
    public event Action? QuitRequested;

    // Mode flavour (screen is identical — only the rules differ): a fixed seed makes a
    // deterministic daily; a dailyKey records the daily best; descent rains garbage over time.
    private readonly int _seed;
    private readonly string? _dailyKey;
    private readonly bool _descent;
    private float _rainTimer;

    private BlockFitGame _game = new();
    private Control _uiHost = null!;
    private Button _back = null!;
    private Label _score = null!, _best = null!, _combo = null!, _streak = null!, _fuse = null!;
    private FuseMeter _fuseMeter = null!;   // text/colour/announce policy for _fuse (shared with the duel)
    private Label _holdLabel = null!;
    private Control _overlay = null!;
    private Label _overScore = null!, _overBest = null!, _overStreak = null!, _overBadge = null!;
    private Control? _coach;               // first-run "how this game works" card

    // Run bookkeeping for the game-over card. The engine owns Streak (it resets on a placement
    // that clears nothing); the view only remembers the run's high-water mark and whether this
    // run beat the stored best, so a finished run finally says something about itself.
    private int _runBestStreak;
    private bool _newBest;

    // 44pt on the 720×1280 design canvas (1 design px ≈ 0.52pt on an iPhone SE3). Layout
    // carves a strip of this height off the BOTTOM of the tray band for the exit button:
    // the tray only ever uses ~3.4 mini-cells of that band, so the strip costs the tray
    // nothing (verified — _trayCell stays width-bound at every portrait aspect) and buys a
    // thumb-reachable exit.
    private const float TouchTarget = 84f;

    // Height of the "HOLD" caption band at the top of the reserve slot. 26 = the 18px caption at
    // this font's ≈1.40× line height (25.2px ink) plus a hair — the piece origin and the label
    // box both derive from it so the caption can never sit on the parked piece.
    private const float HoldCaptionH = 26f;

    // Geometry (recomputed in Layout).
    private float _cell, _trayCell, _holdCell;
    private Vector2 _boardOrigin;
    private readonly Rect2[] _traySlot = new Rect2[3];
    private Rect2 _holdSlot;               // the reserve (hold) slot, left of the tray

    // Drag state. _dragIndex is a tray slot 0..2, HoldSlot (dragging the parked piece), or -1.
    private const int HoldSlot = 3;
    private int _dragIndex = -1;
    private Vector2 _finger;
    private int _touchId = int.MinValue;
    private float _comboFlash;
    // Reused per-frame buffers for the drag-time line-clear preview (no per-frame alloc).
    private readonly System.Collections.Generic.List<int> _pvRows = new(), _pvCols = new();

    // Line-clear celebration: a bright band flash over each cleared row/column plus a
    // spark burst. Bands are always on (they read as "these lines popped"); sparks and
    // the background pulse are pure juice, gated off under reduced motion.
    private const float ClearBandLife = 0.4f;
    private struct ClearBand { public bool Row; public int Index; public float Age; }
    private readonly System.Collections.Generic.List<ClearBand> _bands = new();

    // Shared particle engine (also driven by the store's ArtifactPreview) + its additive
    // (BlendMode.Add) surface for real SDR bloom, and a monotonic clock driving the board's
    // material shimmer/breathe. The bands above are informational (always on); everything
    // the engine spawns is pure juice, gated off under reduced motion.
    private readonly BurstEngine _burst = new();
    private AdditiveFxLayer _fxAdd = null!;      // glow ON TOP of the board (sparks, rings, bleach)
    private AdditiveFxLayer _fxAddUnder = null!; // glowing DEBRIS, under the cells (see BoardUnderLayer)
    private BoardUnderLayer _under = null!;      // board panel + normal-alpha debris, under the cells
    private float _shimmer;
    // Pre-clear cell colours (key = r*Size+c) captured before TryPlace so Shards fly in
    // the exact hues that shattered.
    private readonly System.Collections.Generic.Dictionary<int, PieceType> _shardColors = new();
    // The exact cell set a clear popped (deduped where a row and a column cross), plus the
    // centre of the piece that caused it. Both feed the FX pass: the reduced-motion fade needs
    // the cells, the debris blast core needs the strike point.
    private readonly System.Collections.Generic.List<(int R, int C)> _clearCells = new();
    private readonly bool[] _clearMark = new bool[BlockFitGame.Size * BlockFitGame.Size];
    private Vector2 _strike;

    // The equipped burst-FX artifact — the cosmetic line-clear style, read from the save on entry.
    private BurstArtifact _artifact;

    // Idle hint: after HintDelay seconds without a placement, surface a valid move
    // (FindHint prefers one that clears a line) as a pulsing green "put it here" cue.
    private const float HintDelay = 5f;
    private float _idle;
    private bool _hintOn;
    private int _hintIdx = -1, _hintR, _hintC;
    private float _hintPulse;

    /// <param name="seed">Non-zero → deterministic board (the daily uses today's seed).</param>
    /// <param name="dailyKey">Set → record the daily best under this date key.</param>
    /// <param name="descent">True → the survival variant: garbage rises over time.</param>
    public BlockFitController(int seed = 0, string? dailyKey = null, bool descent = false)
    {
        _seed = seed;
        _dailyKey = dailyKey;
        _descent = descent;
        if (seed != 0) _game = new BlockFitGame(seed);
    }

    /// <summary>The relevant best for this flavour (daily best / descent best / plain best).</summary>
    private long CurrentBest()
    {
        var save = Bootstrap.Instance.Save;
        if (_descent) return (long)save.DescentFitBest;
        if (_dailyKey != null) return (long)(save.GetDailyBest(_dailyKey) ?? 0);
        return (long)save.BlockFitBest;
    }

    private void SubmitBest(long score)
    {
        var save = Bootstrap.Instance.Save;
        if (_descent) { if (score > save.DescentFitBest) save.DescentFitBest = score; }
        else if (_dailyKey != null) save.SubmitDaily(_dailyKey, score);
        else if (score > save.BlockFitBest) save.BlockFitBest = score;
    }

    public override void _Ready()
    {
        _uiHost = new Control { Name = "UiHost", MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_uiHost);
        UiTheme.ApplyTo(_uiHost);

        // Header: score (left), best (right), combo pop (center).
        _score = Header(HorizontalAlignment.Left);
        _best = Header(HorizontalAlignment.Right);
        _combo = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        _combo.AddThemeFontSizeOverride("font_size", 30);
        _combo.AddThemeColorOverride("font_color", Palette.AccentGold);
        _uiHost.AddChild(_combo);

        // Streak read-out: the engine has scored consecutive clearing placements since day one
        // (BlockFitGame.Streak, worth 5×(Streak-1) points) and never showed it. Accent cyan, not
        // gold — gold is reserved for records/daily, and the value is spelled out ("×3") so the
        // information never rides on hue alone.
        _streak = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _streak.AddThemeFontSizeOverride("font_size", 16);
        _streak.AddThemeColorOverride("font_color", Palette.Accent);
        _uiHost.AddChild(_streak);

        // Fuse budget read-out. Merging is a finite resource, and a resource the player cannot see
        // is a resource that just looks broken ("why is the fuse red now?").
        //
        // It used to sit at font 16 (≈8.3pt) in the thin band above the BOARD — 731px (≈59mm) above
        // the tray, which is the only place a fuse ever happens. Two separate defects: 16 is below
        // the size this codebase already rejected once for the HOLD caption ("15 ≈ 8pt is too small
        // on a phone", which is why that one is 18), and the information sat at the top of the
        // screen while the thumb and the act both live at the bottom.
        //
        // It now sits in the EXIT STRIP, immediately under the tray band and beside the exit button
        // — the same strip that was carved out precisely because it covers neither a board cell nor
        // a tray slot (both are drag targets). Measured at 720×1280: strip y 1190→1274, tray band
        // ends 1184, board ends 810.8, exit button x 25→109 and this label starts at 121. No
        // overlap with the board, the tray or the button on any portrait canvas we ship.
        _fuse = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _fuse.AddThemeFontSizeOverride("font_size", 22);   // ≈11.5pt — readable at a glance mid-drag
        _uiHost.AddChild(_fuse);
        _fuseMeter = new FuseMeter(_fuse);

        // "HOLD" caption over the reserve slot (positioned in Layout). Drawn on _uiHost so it
        // sits above the slot panel painted in _Draw.
        // 18, not the old 15: this caption is the ONLY thing naming the reserve slot, and 15 on
        // the design canvas is ~8pt on a phone. Accent (not TextSecondary) marks it as an
        // interactive target rather than decoration.
        _holdLabel = new Label { Text = Loc.T("HOLD"), HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        _holdLabel.AddThemeFontSizeOverride("font_size", 18);
        _holdLabel.AddThemeColorOverride("font_color", new Color(Palette.Accent.R, Palette.Accent.G, Palette.Accent.B, 0.75f));
        _uiHost.AddChild(_holdLabel);

        // Exit button. It used to live in the top-LEFT corner — the single hardest spot to
        // reach in a right-handed portrait grip, and only 52×52. It now sits in the bottom
        // strip under the tray (thumb arc, same side as the pause button in the falling-block
        // gesture layout) at a full 84×84. Sized/positioned in Layout.
        _back = MakeGlassButton("‹", () => QuitRequested?.Invoke());
        _uiHost.AddChild(_back);

        BuildGameOverOverlay();
        _best.Text = Loc.T("BEST {0}", CurrentBest());
        _artifact = BurstArtifacts.FromId(Bootstrap.Instance.Save.EquippedArtifactId);

        // ---- Draw stack ---------------------------------------------------------------
        // A CanvasItem always paints ITSELF before its children, so anything that must sit
        // BELOW this node's own _Draw (the board cells) has to be a separate canvas item at a
        // lower z. Relative z-indices do that; the node itself is lifted to 2 first so the
        // under-layers land on 1 — strictly above the shared Background at 0, never negative.
        // (SceneRouter frees the old screen before adding the new one, so this stack never
        // has another screen interleaved in it.)
        //
        //   z1  _under        board panel + normal-alpha debris
        //   z1  _fxAddUnder   glowing (additive) debris — the VAPOR TUBE half
        //   z2  this._Draw    cells, clear bands, tray, hint, previews, piece in hand
        //   z2  _fxAdd        sparks/rings/screen bleach (debris excluded — drawn below)
        //   z2  _uiHost       score, streak, HOLD caption, exit button, overlays
        ZIndex = 2;
        _under = new BoardUnderLayer(DrawUnder) { Position = Vector2.Zero, ZIndex = -1 };
        AddChild(_under);
        MoveChild(_under, 0);

        // Additive glow surfaces. Node2D (NOT CanvasLayer — that would drop modulate and break
        // crossfades). The debris half rides the under-layer; everything else stays above the
        // board but below _uiHost so the score/back button are never washed out.
        _fxAddUnder = new AdditiveFxLayer(_burst, () => _cell,
            () => new Rect2(Vector2.Zero, Bootstrap.Instance.SafeCanvasSize), AdditiveFxLayer.Pass.DebrisOnly)
        {
            Position = Vector2.Zero,
            ZIndex = -1,
        };
        AddChild(_fxAddUnder);
        MoveChild(_fxAddUnder, 1);

        _fxAdd = new AdditiveFxLayer(_burst, () => _cell,
            () => new Rect2(Vector2.Zero, Bootstrap.Instance.SafeCanvasSize), AdditiveFxLayer.Pass.NoDebris)
        {
            Position = Vector2.Zero,
        };
        AddChild(_fxAdd);
        MoveChild(_fxAdd, 2);   // draw before _uiHost (glow under UI)

        // First run — added last so it sits above everything in _uiHost.
        if (!CoachSeen) BuildCoachCard();

        GetViewport().SizeChanged += Layout;
        Layout();
        Bootstrap.Instance.Audio.PlayMusic("game");
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= Layout;

    private Label Header(HorizontalAlignment align)
    {
        var l = new Label
        {
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Center,   // centre in the header band → lines up with the back button
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        l.AddThemeFontSizeOverride("font_size", 24);
        l.AddThemeColorOverride("font_color", Palette.TextPrimary);
        _uiHost.AddChild(l);
        return l;
    }

    /// <summary>A round glass button at the 44pt touch floor — same language as the
    /// falling-block gesture cluster, so "leave" looks the same in both games.</summary>
    private static Button MakeGlassButton(string glyph, Action onPressed)
    {
        var b = new Button
        {
            Text = glyph,
            CustomMinimumSize = new Vector2(TouchTarget, TouchTarget),
            MouseFilter = Control.MouseFilterEnum.Stop,
            FocusMode = Control.FocusModeEnum.None,
            Modulate = new Color(1, 1, 1, 0.85f),
        };
        b.AddThemeStyleboxOverride("normal", new StyleBoxTexture {
            Texture = TextureFactory.Circle(96, new Color(0.72f, 0.76f, 1f, 0.06f), new Color(1, 1, 1, 0.14f), 1.5f) });
        b.AddThemeStyleboxOverride("hover", new StyleBoxTexture {
            Texture = TextureFactory.Circle(96, new Color(0.72f, 0.76f, 1f, 0.09f), new Color(1, 1, 1, 0.20f), 1.5f) });
        b.AddThemeStyleboxOverride("pressed", new StyleBoxTexture {
            Texture = TextureFactory.Circle(96, new Color(Palette.Accent.R, Palette.Accent.G, Palette.Accent.B, 0.28f),
                                                new Color(Palette.Accent.R, Palette.Accent.G, Palette.Accent.B, 0.9f), 2f) });
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        // CustomMinimumSize already pins 84×84; 28 keeps the glyph's own measured height well
        // under that floor (this UI font reports ≈1.40× line height → ≈39px), so the button
        // never grows past its minimum and the round stylebox stays a circle instead of
        // stretching. (The old note here claimed a 3× line height — it was wrong, and the
        // layout comments downstream inherited the error.)
        b.AddThemeFontSizeOverride("font_size", 28);
        b.AddThemeColorOverride("font_color", Palette.TextPrimary);
        Motion.BindButtonFeel(b);
        b.Pressed += onPressed;
        return b;
    }

    private void Layout()
    {
        var safe = Bootstrap.Instance.SafeCanvasSize;
        _uiHost.Position = Vector2.Zero;
        _uiHost.Size = safe;

        // The exit strip is measured, not assumed: a Button never shrinks below its own
        // minimum size, so trusting the constant would let a taller button run off-canvas.
        var backMin = _back.GetCombinedMinimumSize();
        float exitH = Mathf.Max(TouchTarget, backMin.Y);
        float exitW = Mathf.Max(TouchTarget, backMin.X);
        float exitBand = exitH + 12f;

        float headerH = Mathf.Clamp(safe.Y * 0.09f, 56f, 120f);
        // Board is width-bound on a phone; leave room for the tray below.
        _cell = Mathf.Floor(Mathf.Min(safe.X * 0.94f / BlockFitGame.Size, safe.Y * 0.58f / BlockFitGame.Size));
        float boardPx = _cell * BlockFitGame.Size;
        _boardOrigin = new Vector2((safe.X - boardPx) / 2f, headerH + safe.Y * 0.02f);

        float trayTop = _boardOrigin.Y + boardPx + safe.Y * 0.03f;
        // The tray band now ends above the exit strip so the exit button covers neither the
        // board nor a tray slot (the slots are drag/drop targets — a button on top of one
        // would steal the grab).
        float trayH = Mathf.Max(48f, safe.Y - trayTop - exitBand);
        // Reserve a compact column on the left for the HOLD slot; the 3 tray slots split the rest.
        float holdW = safe.X * 0.22f;
        float slotW = (safe.X - holdW) / 3f;
        _holdSlot = new Rect2(0f, trayTop, holdW, trayH);
        // A tray piece is at most 5 cells wide / 3 tall; size the mini-cell to fit a slot.
        _trayCell = Mathf.Floor(Mathf.Min(_cell * 0.55f, Mathf.Min(slotW * 0.9f / 5f, trayH / 3.4f)));
        for (int i = 0; i < 3; i++)
            _traySlot[i] = new Rect2(holdW + i * slotW, trayTop, slotW, trayH);
        // The held piece can be up to 5×5; leave the caption band (HoldCaptionH) at the slot top.
        _holdCell = Mathf.Floor(Mathf.Max(6f, Mathf.Min(_trayCell, Mathf.Min((holdW - 10f) / 5f, (trayH - HoldCaptionH - 4f) / 5f))));
        _holdLabel.Position = new Vector2(_holdSlot.Position.X, _holdSlot.Position.Y + 2f);
        _holdLabel.Size = new Vector2(_holdSlot.Size.X, HoldCaptionH - 4f);

        // Header row: score (left) and best (right), vertically centred in the header band.
        // The exit button no longer shares this row — it moved to the bottom strip — so the
        // score starts at the same left margin as everything else.
        float pad = Mathf.Max(12f, safe.X * 0.035f);
        _score.Position = new Vector2(pad, 0f); _score.Size = new Vector2(Mathf.Max(40f, safe.X * 0.5f - pad), headerH);
        _best.Position = new Vector2(safe.X * 0.5f, 0f); _best.Size = new Vector2(safe.X * 0.5f - pad, headerH);
        // COMBO used to start AT headerH and run 40px down, which put it 16px over the board's
        // top row (measured: header 115.2 + 40 vs board top 140.8 on a 720×1280 canvas — the old
        // "3× line height" note was wrong). It now hangs from the BOTTOM of the header band, so
        // it lands in the empty strip between the two read-outs instead of on the playfield. It
        // stays full width and centred: score is left-aligned and best right-aligned, so a
        // centred pop can never collide with either.
        const float comboH = 42f;
        _combo.Position = new Vector2(0, Mathf.Max(0f, headerH - comboH)); _combo.Size = new Vector2(safe.X, comboH);
        // Streak sits in the gap between the header and the board — never over a cell.
        float gap = Mathf.Max(0f, _boardOrigin.Y - headerH);
        float streakH = Mathf.Clamp(gap - 6f, 12f, 22f);
        _streak.Position = new Vector2(0f, _boardOrigin.Y - 3f - streakH);
        _streak.Size = new Vector2(safe.X, streakH);

        // Exit: bottom-left of the carved strip, clear of the tray band above it.
        _back.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
        _back.Size = new Vector2(exitW, exitH);
        _back.Position = new Vector2(pad, safe.Y - exitH - 6f);

        // FUSE: the rest of that same strip, starting one gutter right of the exit button and
        // vertically centred on it. Derived from the button's MEASURED rect, so a taller/wider
        // button (a longer glyph, a bigger font) pushes the read-out instead of colliding with it.
        // It inherits the exit button's own guarantee: the strip starts at safe.Y - exitBand,
        // which is exactly where the tray band is told to stop, so the read-out covers no board
        // cell and no tray slot (both are drag targets and a cover would steal the grab).
        float fuseX = _back.Position.X + exitW + 12f;
        _fuse.Position = new Vector2(fuseX, _back.Position.Y);
        _fuse.Size = new Vector2(Mathf.Max(40f, safe.X - fuseX - pad), exitH);

        // Arm the debris play box. Nobody armed it before, so the field fell back to its
        // conservative derived box — walls hugging the board and a floor 6px under the board's
        // bottom lip — which meant every chunk bounced INSIDE the playfield and settled on the
        // last row, exactly on top of the cells the player is reading. Here the walls are the
        // whole canvas (chunks leave the board sideways) and the shelf is the empty gap between
        // the board and the tray, so settled debris covers neither a board cell nor a drag
        // target. Re-armed on every layout because it is pure geometry.
        _burst.SetDebrisBounds(
            new Rect2(0f, _boardOrigin.Y - _cell, safe.X, Mathf.Max(1f, trayTop - (_boardOrigin.Y - _cell))),
            trayTop - 4f);

        if (GodotObject.IsInstanceValid(_overlay)) { _overlay.Position = Vector2.Zero; _overlay.Size = safe; }
        if (_coach is not null && GodotObject.IsInstanceValid(_coach)) { _coach.Position = Vector2.Zero; _coach.Size = safe; }
        RedrawAll();
    }

    public override void _Process(double delta)
    {
        _score.Text = _game.Score.ToString("N0");
        if (_comboFlash > 0f)
        {
            _comboFlash -= (float)delta;
            _combo.Modulate = new Color(1, 1, 1, Mathf.Clamp(_comboFlash / 0.9f, 0, 1));
        }
        else _combo.Text = "";

        // Streak only earns screen space once it is actually paying (the bonus starts at 2).
        bool showStreak = _game.Streak >= 2;
        if (showStreak) _streak.Text = Loc.T("STREAK ×{0}", _game.Streak);
        _streak.Visible = showStreak;

        float dt = (float)delta;
        // Fuse budget. Always on, and it ANNOUNCES every change (see FuseMeter) — a run sits at
        // 5/5 most of its length, so a silent counter is one the player only ever reads too late.
        _fuseMeter.Sync(_game.Merges, dt);

        for (int i = _bands.Count - 1; i >= 0; i--)
        {
            var b = _bands[i]; b.Age += dt;
            if (b.Age >= ClearBandLife) _bands.RemoveAt(i); else _bands[i] = b;
        }
        _burst.Update(dt);
        _shimmer += dt;                 // drives material breathe / holo scroll / starfield twinkle

        // While the first-run card is up the run is on hold: no idle-hint countdown and no
        // Descent garbage, so reading it can't cost the player anything.
        if (_coach is not null && GodotObject.IsInstanceValid(_coach)) { RedrawAll(); return; }

        // Idle hint timer: only advances while the run is live and nothing is being dragged.
        if (!_game.GameOver && _dragIndex == -1)
        {
            _idle += dt;
            if (_idle >= HintDelay)
            {
                if (!_hintOn) _hintOn = _game.FindHint(out _hintIdx, out _hintR, out _hintC);
                _hintPulse += dt;
            }
        }

        // Descent survival: garbage rises — faster and heavier as the score climbs.
        if (_descent && !_game.GameOver)
        {
            _rainTimer += dt;
            float interval = Mathf.Max(3.5f, 9f - _game.Score / 400f);
            if (_rainTimer >= interval)
            {
                _rainTimer = 0f;
                _game.AddGarbage(2 + (int)(_game.Score / 600));
                if (_game.GameOver) ShowGameOver();
            }
        }
        RedrawAll();
    }

    /// <summary>Redraw every canvas item in the stack — the three FX/board layers all read the
    /// same particle lists, so they must be invalidated together or the glow lags the bodies.</summary>
    private void RedrawAll()
    {
        QueueRedraw();
        _under.QueueRedraw();
        _fxAddUnder.QueueRedraw();
        _fxAdd.QueueRedraw();
    }

    // ---- Input: grab a tray piece, drag it (floating above the finger), release to place ----

    /// <summary>Grabs only. Presses go through the UNhandled path so the exit button (and any
    /// overlay) wins the taps that land on it.</summary>
    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventScreenTouch { Pressed: true } t:
                Grab((int)t.Index, t.Position);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb:
                Grab(int.MaxValue, mb.Position);
                break;
            // ESC also leaves the run (desktop parity with every other screen).
            case InputEventKey when e.IsActionPressed("pause_game"):
                QuitRequested?.Invoke();
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    /// <summary>
    /// Drag + release. These run on the RAW input path (never consumed) because the finger
    /// can lift anywhere — including over the exit button, whose Stop mouse filter would
    /// otherwise swallow the release and strand the piece mid-drag forever. Releasing over a
    /// Button can't press it: Godot only fires a button whose press STARTED on it.
    /// </summary>
    public override void _Input(InputEvent e)
    {
        if (_dragIndex == -1) return;
        switch (e)
        {
            case InputEventScreenTouch { Pressed: false } t:
                Release((int)t.Index, t.Position);
                break;
            case InputEventScreenDrag d:
                if ((int)d.Index == _touchId) { _finger = d.Position; QueueRedraw(); }
                break;
            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left } mb:
                Release(int.MaxValue, mb.Position);
                break;
            case InputEventMouseMotion mm when (mm.ButtonMask & MouseButtonMask.Left) != 0:
                if (_touchId == int.MaxValue) { _finger = mm.Position; QueueRedraw(); }
                break;
        }
    }

    /// <summary>
    /// PICKING UP IS WIDE; DROPPING IS PRECISE. The asymmetry is the design, not an oversight.
    ///
    /// Picking up has NO ambiguity to resolve: the four slots partition the tray band, each
    /// holds at most one piece, and there is nothing else down there to hit — so a generous
    /// judgement costs literally nothing and there is no second interpretation it could steal.
    /// Dropping is the opposite: a release over a tray piece means FUSE, a release two pixels
    /// away means "nothing happened", and that call has to be made from the piece's real cells
    /// (see <see cref="MergeReach"/>). The complaint that started this was "it fuses when I only
    /// brush it" — a complaint about the DROP.
    ///
    /// A shared rect was tried and reverted: it shrank the pick-up target to the piece's own
    /// silhouette, which on the versus board (a 25px tray cell) is a 50×50px = 26pt = 4.1mm
    /// patch you must hit under time pressure. The "hand learns the wide slot, then gets
    /// betrayed on the drop" worry is real, but it is a job for the HIGHLIGHT — the merge rect
    /// is already outlined exactly as the hit test computes it (<see cref="DrawMergeTarget"/>) —
    /// not a reason to cripple the grab.
    /// </summary>
    private void Grab(int id, Vector2 pos)
    {
        if (_game.GameOver || _dragIndex != -1) return;
        // Reserve slot: grab the parked piece to place it back on the board. Whole rect, same
        // as on the way in (stash), so hold is symmetric in both directions.
        bool inBand = _holdSlot.HasPoint(pos);
        if (_game.Held is not null && inBand)
        {
            _dragIndex = HoldSlot; _touchId = id; _finger = pos;
            // The pickup (upstroke) cue — the other half of PlayPlace. A successful grab used to be
            // SILENT while a failed one clicked, so the tray only ever spoke to say "no". Quieter
            // and higher than the placement, like a real key coming back up.
            Bootstrap.Instance.Audio.PlayPlace(lift: true);
            // Sweep settled debris so nothing shares the screen with the piece in hand. The
            // tray branch has always done this; the hold branch silently didn't.
            _burst.Debris.ClearResting();
            ResetIdle();
            QueueRedraw();
            return;
        }
        if (_trayCell > 0f)
            for (int i = 0; i < 3; i++)
            {
                if (!_traySlot[i].HasPoint(pos)) continue;
                inBand = true;
                if (_game.Tray[i] is null) break;      // slots don't overlap — no other can match
                _dragIndex = i; _touchId = id; _finger = pos;
                Bootstrap.Instance.Audio.PlayPlace(lift: true);   // pickup upstroke — see the hold branch
                _burst.Debris.ClearResting();
                // Only a SUCCESSFUL grab retires the idle hint. Resetting it up front meant a
                // fumbled grab wiped the merge outlines and the "put it here" pulse — deleting
                // the one on-screen clue about where the targets are at the exact moment the
                // player just proved they needed it.
                ResetIdle();
                QueueRedraw();
                return;
            }

        // A press that landed in the tray band and picked up nothing was AIMED at a piece (there
        // is nothing else down there) and found an empty slot. Say so with the same "nothing
        // happened" click a rejected drop uses: silence here is indistinguishable from a frozen
        // app, which is exactly what a missed grab used to look like. Presses OUTSIDE the band
        // stay silent on purpose — a tap on the board or the header is not a failed grab, and
        // clicking at every stray touch would train the player to ignore the cue.
        if (inBand) Bootstrap.Instance.Audio.PlaySfx("move");
    }

    /// <summary>The piece currently being dragged (a tray piece, the held piece, or null).</summary>
    private BlockPiece? DraggedPiece() => _dragIndex switch
    {
        HoldSlot => _game.Held,
        >= 0 and < 3 => _game.Tray[_dragIndex],
        _ => null,
    };

    private void Release(int id, Vector2 pos)
    {
        if (_dragIndex == -1 || id != _touchId) return;
        int idx = _dragIndex;
        _dragIndex = -1; _touchId = int.MinValue;
        _finger = pos;
        ResetIdle();

        // Dragging the parked piece: place it on the board (or drop it back on hold to cancel).
        if (idx == HoldSlot) { ReleaseHeld(pos); QueueRedraw(); return; }

        // Reaching the board's BOTTOM ROW necessarily puts the finger ~0.6 cell below the board,
        // which is inside the top of the reserve slot's column. The whole-rect stash test used to
        // win there, so aiming at the bottom-left corner silently stashed instead. A verified
        // board aim (a real target the piece actually fits) now outranks the wide slot guess —
        // same principle as TargetCell: precise intent beats a rectangle's benefit of the doubt.
        var trayPiece = _game.Tray[idx];
        int gr = 0, gc = 0;
        bool boardOk = trayPiece is not null
                       && TargetCell(trayPiece, pos, out gr, out gc)
                       && _game.CanPlace(trayPiece, gr, gc);

        // Stash intent: released over the reserve slot → park this tray piece in hold (swaps if
        // the slot is already occupied). Checked before merge/placement so it always wins here.
        if (!boardOk && _holdSlot.HasPoint(pos) && trayPiece is not null)
        {
            if (_game.TryHold(idx))
            {
                Bootstrap.Instance.Audio.PlaySfx("hold");
                if (_game.GameOver) ShowGameOver();
            }
            QueueRedraw();
            return;
        }

        // Merge intent: released over a different, occupied tray slot → fuse the two pieces
        // into one larger composite (checked before board placement so it always wins here).
        int mergeInto = MergeTargetSlot(pos, idx);
        if (mergeInto >= 0 && _game.Tray[idx] is { } msrc)
        {
            MergeOffset(msrc, mergeInto, out int mr, out int mc);   // where the finger says to join
            // Three outcomes, three answers. "You are out of fuses" used to make the SAME "move"
            // click as "these two don't fit", so the player kept re-aiming a fuse that could never
            // fire; the counter is what explains it, so the counter is what blinks.
            switch (_game.CheckMerge(idx, mergeInto, mr, mc))
            {
                case MergeVerdict.Ok:
                    _game.TryMerge(idx, mergeInto, mr, mc);
                    Bootstrap.Instance.Audio.PlayFuse();        // the verb's own voice, not the stash cue
                    if (_game.GameOver) ShowGameOver();
                    break;
                case MergeVerdict.NoCharges:
                    Bootstrap.Instance.Audio.PlayFuseDenied();  // the fuse cue sagging, not a snap-back
                    _fuseMeter.Alert();
                    break;
                default:
                    Bootstrap.Instance.Audio.PlaySfx("move");   // overlap or too big to fit — re-aim
                    break;
            }
            QueueRedraw();
            return;
        }

        // Board placement, or — for every other release, including anywhere in the tray band —
        // nothing at all but the snap-back cue. "Nothing happened" is the correct default for an
        // ambiguous gesture; the piece stays in its slot and the player simply tries again.
        if (boardOk && trayPiece is not null)
            CommitPlacement(trayPiece, gr, gc, fromHold: false, trayIdx: idx);
        else
            Bootstrap.Instance.Audio.PlaySfx("move"); // snap-back cue
        QueueRedraw();
    }

    /// <summary>Release path for the parked (held) piece: place it on the board, or cancel (snap
    /// back into hold) if it was dropped anywhere that isn't a legal board target.</summary>
    private void ReleaseHeld(Vector2 pos)
    {
        var piece = _game.Held;
        if (piece is not null && TargetCell(piece, pos, out int gr, out int gc) && _game.CanPlace(piece, gr, gc))
            CommitPlacement(piece, gr, gc, fromHold: true, trayIdx: -1);
        else
            Bootstrap.Instance.Audio.PlaySfx("move");   // back into hold, untouched
    }

    /// <summary>Commit a legal drop (from a tray slot or the hold slot): snapshot the lines it
    /// completes, place it, and fire the audio + line-clear celebration + best-score update. The
    /// preview snapshot must happen BEFORE the place call, which clears those lines.</summary>
    private void CommitPlacement(BlockPiece piece, int gr, int gc, bool fromHold, int trayIdx)
    {
        _game.LinesClearedBy(piece, gr, gc, _pvRows, _pvCols);
        CaptureShardColors(piece, gr, gc);   // snapshot popped hues before the placement clears them
        // Where the blow landed: the centre of the piece just placed. Debris blasts outward from
        // here instead of from the middle of the line, so a corner placement reads as a corner hit.
        _strike = _boardOrigin + new Vector2((gc + piece.Width * 0.5f) * _cell, (gr + piece.Height * 0.5f) * _cell);
        if (fromHold) _game.TryPlaceHeld(gr, gc); else _game.TryPlace(trayIdx, gr, gc);
        // The placement voice: the player's chosen SOUND PACK, transposed by the equipped skin's
        // material and walked through the detune rotation — all of that lives in PlayPlace, which
        // is the point. Calling PlaySfx("lock", MaterialPitch(...)) straight from here is what
        // made the five packs audible in the settings preview and nowhere else in the game.
        Bootstrap.Instance.Audio.PlayPlace();
        if (_game.LastClearedRows + _game.LastClearedCols > 0)
        {
            Bootstrap.Instance.Audio.PlaySfx(_game.LastClearedRows + _game.LastClearedCols >= 2 ? "combo" : "line_clear");
            int lines = _game.LastClearedRows + _game.LastClearedCols;
            _combo.Text = lines >= 2 ? Loc.T("COMBO ×{0}", lines) : Loc.T("CLEAR");
            _comboFlash = 0.9f;
            SpawnClearFx();
        }
        if (_runBestStreak < _game.Streak) _runBestStreak = _game.Streak;
        if (_game.Score > CurrentBest())
        {
            SubmitBest(_game.Score);
            _newBest = true;
            _best.Text = Loc.T("BEST {0}", _game.Score);
        }
        if (_game.GameOver) ShowGameOver();
    }

    /// <summary>
    /// Grid origin the dragged piece snaps to — computed so the piece floats centred ABOVE the
    /// finger (fingertip never hides it). Returns FALSE when the finger is not actually pointing
    /// at the board, in which case a release must do NOTHING.
    ///
    /// The old version clamped the raw origin into range and returned true unconditionally. Once
    /// the merge test tightened to the destination piece's real cells, most of the tray band
    /// stopped claiming a release — and every one of those releases fell through to here, got
    /// clamped, and force-placed the piece at the clamped edge, which for a finger below the
    /// board always means the LAST ROW. Missing a fuse by 10px silently dumped the piece in the
    /// worst legal square on the board. An ambiguous gesture must default to no-op, never to the
    /// most destructive legal action.
    ///
    /// Rounding supplies the forgiveness: a target is accepted while the lifted origin is within
    /// half a cell of a legal grid origin, so every row and column stays reachable — including
    /// the bottom row, whose finger position necessarily sits ~0.6 cell BELOW the board — and
    /// one more half cell out is a clean, harmless miss.
    /// </summary>
    private bool TargetCell(BlockPiece p, Vector2 finger, out int gr, out int gc)
    {
        gr = 0; gc = 0;
        if (_cell <= 0f) return false;
        float lift = _cell * 0.6f;
        var topLeft = new Vector2(finger.X - p.Width * _cell / 2f, finger.Y - lift - p.Height * _cell);
        gc = Mathf.RoundToInt((topLeft.X - _boardOrigin.X) / _cell);
        gr = Mathf.RoundToInt((topLeft.Y - _boardOrigin.Y) / _cell);
        int maxC = BlockFitGame.Size - p.Width, maxR = BlockFitGame.Size - p.Height;
        bool on = gc >= 0 && gr >= 0 && gc <= maxC && gr <= maxR;
        gc = Mathf.Clamp(gc, 0, maxC);
        gr = Mathf.Clamp(gr, 0, maxR);
        return on;
    }

    /// <summary>
    /// Reach around the destination piece's silhouette in the MERGE hit test, in tray cells.
    /// (Grabbing does not use this — see <see cref="Grab"/> for why the two are asymmetric.)
    ///
    /// The rect is the piece's bounding box grown by this reach and then CLIPPED TO ITS OWN SLOT.
    /// The slots partition the tray band, so two neighbours claiming the same point is
    /// structurally impossible rather than arithmetically lucky — that is what lets the reach be
    /// this generous at all (the previous ceiling was 0.28 cells). With a missed drop now
    /// harmless (see <see cref="TargetCell"/>), half a cell is set for the HAND.
    ///
    /// THE MEASUREMENT, done properly. The design canvas is 720×1280; on an iPhone SE3 (375×667pt)
    /// the fit scale is min(375/720, 667/1280) ≈ 0.521, so 1 design px ≈ 0.521 pt. An iOS point is
    /// 1/163 inch = 0.1558 mm — NOT 1/72 inch. (An earlier version of this comment divided by 72
    /// and claimed 12mm; every physical figure it quoted was 2.2× too large.)
    ///
    ///   solo tray cell 33px → 1×1 merge target 66×66px = 34.4pt = 5.4mm
    ///   versus tray cell 25px → 1×1 merge target 50×50px = 26.0pt = 4.1mm
    ///   under the old 0.28 ceiling the same targets were: solo 51.5px = 26.8pt = 4.2mm,
    ///     versus 39.0px = 20.3pt = 3.2mm — and with no reach at all (a bare per-cell test)
    ///     simply the tray cell itself: solo 33px = 17.2pt = 2.7mm, versus 25px = 13.0pt = 2.0mm.
    ///     (An earlier draft of this list quoted one "44.9px = 23.4pt = 3.6mm" for the pre-reach
    ///     case. The pt/mm conversion in it was right but the pixel figure reproduces from no
    ///     layout here — it is neither board's cell nor either board's 0.28 rect — so it has been
    ///     replaced by the per-board arithmetic above. Both boards, always: this screen and the
    ///     duel have different cells and a single number can only mislead about one of them.)
    ///   platform minimum 44pt = 6.9mm · this file's <see cref="TouchTarget"/> 84px = 43.8pt = 6.8mm
    ///
    /// So state it plainly: THE MERGE TARGET IS BELOW THE PLATFORM MINIMUM — 78% of 44pt on the
    /// solo board and 59% on the versus board. It is deliberate for three reasons, and only
    /// because all three hold: (1) missing is free — a release that hits no merge rect and no
    /// legal board cell does nothing at all, so the cost of a miss is one repeated gesture, not a
    /// ruined board; (2) the player explicitly asked for a fuse that stops firing on a brush, and
    /// precision is the thing they asked for; (3) the accepted rect is not invisible — it is
    /// outlined on screen from this exact geometry (<see cref="DrawMergeTarget"/>) and magnified
    /// while aiming (<see cref="MergeLoupe"/> — on BOTH boards now, which is what lets the duel's
    /// smaller rect stand), so the target is aimed at, not guessed.
    /// If any of those three stops being true, this constant is wrong.
    /// </summary>
    private const float MergeReach = 0.5f;

    /// <summary>The merge target rect of one tray piece — the single geometry source shared by the
    /// merge hit test and the merge highlight, so what lights up is exactly what accepts a drop.
    /// (Grabbing uses the whole slot; see <see cref="Grab"/>.)</summary>
    private static Rect2 MergeHitRect(BlockPiece p, Vector2 origin, float cell, Rect2 slot)
        => new Rect2(origin, new Vector2(p.Width * cell, p.Height * cell))
            .Grow(cell * MergeReach).Intersection(slot);

    /// <summary>The occupied tray slot whose PIECE the finger is over — other than the dragged
    /// one — or -1. When ≥0 a release fuses the pieces (merge) instead of placing on the board.
    /// Both the release path and the hover preview call this, so "it looked green" and "it
    /// merged" can never disagree.</summary>
    private int MergeTargetSlot(Vector2 pos, int dragIdx)
    {
        if (_trayCell <= 0f) return -1;
        for (int i = 0; i < 3; i++)
        {
            if (i == dragIdx || _game.Tray[i] is not { } p) continue;
            if (MergeHitRect(p, TrayPieceOrigin(p, i), _trayCell, _traySlot[i]).HasPoint(pos))
                return i;
        }
        return -1;
    }

    /// <summary>Cell offset (relative to the destination piece's top-left in its slot) where the
    /// dragged source snaps for a merge — centred under the finger so the join follows the drop.</summary>
    private void MergeOffset(BlockPiece src, int dstSlot, out int rowOff, out int colOff)
    {
        var dst = _game.Tray[dstSlot]!;
        var borigin = TrayPieceOrigin(dst, dstSlot);
        float fc = (_finger.X - borigin.X) / _trayCell - (src.Width - 1) * 0.5f;
        float fr = (_finger.Y - borigin.Y) / _trayCell - (src.Height - 1) * 0.5f;
        colOff = Mathf.RoundToInt(fc);
        rowOff = Mathf.RoundToInt(fr);
    }

    private void ResetIdle() { _idle = 0f; _hintOn = false; _hintIdx = -1; _hintPulse = 0f; }

    private void SpawnClearFx()
    {
        // Informational band over each cleared line (always on): _pvRows/_pvCols were
        // captured just before the placement that completed them.
        foreach (int r in _pvRows) _bands.Add(new ClearBand { Row = true, Index = r });
        foreach (int c in _pvCols) _bands.Add(new ClearBand { Row = false, Index = c });
        if (_cell <= 0) return;

        BuildClearCells();
        if (Motion.Reduced)
        {
            // Reduced motion used to return here empty-handed: the accessibility setting cost the
            // player the entire "these cells, in these colours, popped" signal and left only the
            // two band strips. The calm substitute exists — a 140ms flat fade in place, no travel,
            // no spin, no shadow — and this is its one caller.
            _burst.Debris.EmitReducedFade(_clearCells, _boardOrigin, _cell, PreClearColor);
            return;
        }

        // Blast core biased to the placement, not to the middle of the line.
        _burst.Debris.SetStrike(_strike);

        // Scale counts down on big combos so a huge clear can't flood the particle pool.
        int lines = _pvRows.Count + _pvCols.Count;
        float budget = Mathf.Clamp(1f - 0.12f * (lines - 1), 0.4f, 1f);
        int n = BlockFitGame.Size;
        foreach (int r in _pvRows)
            _burst.EmitLine(_artifact, rowLine: true, r, _boardOrigin, _cell, n, budget, k => PreClearColor(r, k));
        foreach (int c in _pvCols)
            _burst.EmitLine(_artifact, rowLine: false, c, _boardOrigin, _cell, n, budget, k => PreClearColor(k, c));

        // Background wash, tinted and scaled to the equipped artifact.
        var (pulseCol, pulseBase) = _artifact switch
        {
            BurstArtifact.Supernova => (new Color(1f, 0.98f, 0.9f), 0.34f),
            BurstArtifact.Rainbow => (Palette.AccentViolet, 0.26f),
            BurstArtifact.Fireworks => (Palette.AccentGold, 0.28f),
            BurstArtifact.Confetti => (Palette.AccentGreen, 0.22f),
            BurstArtifact.Shards => (Palette.Accent, 0.24f),
            BurstArtifact.Aurora => (new Color(0.3f, 0.9f, 0.8f), 0.20f),
            BurstArtifact.Lightning => (Palette.Accent, 0.30f),
            BurstArtifact.BubblePop => (Palette.Accent, 0.18f),
            BurstArtifact.PrismBloom => (Palette.AccentViolet, 0.26f),
            BurstArtifact.Starfall => (new Color(0.9f, 0.85f, 1f), 0.24f),
            _ => (Palette.AccentGold, 0.22f),
        };
        Bootstrap.Instance.Bg.Pulse(pulseCol, Mathf.Min(0.6f, pulseBase + lines * 0.1f));
    }

    /// <summary>Snapshot the colours of the cells the just-placed piece completes, so the
    /// Shards artifact can shatter in the popped blocks' real hues (TryPlace clears them next).</summary>
    private void CaptureShardColors(BlockPiece piece, int gr, int gc)
    {
        _shardColors.Clear();
        int n = BlockFitGame.Size;
        void Snapshot(int r, int c)
        {
            if ((uint)r >= n || (uint)c >= n) return;
            var t = _game.At(r, c);
            if (t == PieceType.Empty) t = piece.Color;   // a cell the piece itself will fill
            _shardColors[r * n + c] = t;
        }
        foreach (var (dr, dc) in piece.Cells) _shardColors[(gr + dr) * n + (gc + dc)] = piece.Color;
        foreach (int r in _pvRows) for (int c = 0; c < n; c++) Snapshot(r, c);
        foreach (int c in _pvCols) for (int r = 0; r < n; r++) Snapshot(r, c);
    }

    /// <summary>The exact cell set a clear popped, deduped where a row and a column cross. Feeds
    /// the reduced-motion fade; the mark array keeps it allocation-free.</summary>
    private void BuildClearCells()
    {
        const int n = BlockFitGame.Size;
        _clearCells.Clear();
        Array.Clear(_clearMark, 0, _clearMark.Length);
        foreach (int r in _pvRows)
            for (int c = 0; c < n; c++)
                if (!_clearMark[r * n + c]) { _clearMark[r * n + c] = true; _clearCells.Add((r, c)); }
        foreach (int c in _pvCols)
            for (int r = 0; r < n; r++)
                if (!_clearMark[r * n + c]) { _clearMark[r * n + c] = true; _clearCells.Add((r, c)); }
    }

    /// <summary>
    /// The colour a popped cell was actually WEARING. This must go through
    /// <see cref="Palette.FitFill"/> with the same cellPhase the board draw uses (row+col), not
    /// through <see cref="Palette.ForPiece"/>: FitFill is where the equipped skin's ColorPlan
    /// lives, so the raw-hue version made a Mono skin like SUMI ("ONE INK") explode in seven
    /// colours it never shows on the board. The debris and the sparks now shatter in the board's
    /// own palette, whatever plan is equipped.
    /// </summary>
    private Color PreClearColor(int r, int c)
        => Palette.FitFill(_shardColors.TryGetValue(r * BlockFitGame.Size + c, out var t) ? t : PieceType.Garbage,
                           r + c);

    // ---- Render ----

    /// <summary>The under-board layer (z1): the panel, then the normal-alpha chunk pass. Debris
    /// lives behind the glass — chunks read through the empty cells they came from and can never
    /// hide a filled one, the clear preview, or the piece in the player's hand. Its glowing twin
    /// is drawn by <c>_fxAddUnder</c>, which sits immediately above this and below the cells.</summary>
    private void DrawUnder(CanvasItem ci)
    {
        if (_cell <= 0) return;
        float boardPx = _cell * BlockFitGame.Size;
        ci.DrawRect(new Rect2(_boardOrigin - new Vector2(6, 6), new Vector2(boardPx + 12, boardPx + 12)),
                    new Color(0.05f, 0.06f, 0.11f, 0.85f), filled: true);
        _burst.DrawDebrisNormal(ci);
    }

    public override void _Draw()
    {
        if (_cell <= 0) return;
        float boardPx = _cell * BlockFitGame.Size;
        var tex = TextureFactory.Cell(Mathf.Clamp((int)_cell, 8, 128));
        var glyph = Palette.EquippedGlyph;                      // the equipped skin's block stamp
        var mat = Palette.EquippedMaterial;                     // the equipped skin's finish
        bool reduced = Motion.Reduced;

        // ---------------------------------------------------------------------------------
        // Z-ORDER CONTRACT (bottom → top). Everything the player needs in order to choose the
        // NEXT move outranks everything that celebrates the LAST one:
        //   panel → debris → cells → clear bands → particles → tray/hold → hint → merge preview
        //   → piece in hand + clear preview.
        // Debris in particular is the heaviest, longest-lived, most opaque thing the FX engine
        // makes; letting BurstEngine.DrawNormal emit it last (its default) put rigid chunks over
        // the board cells, over the green clear preview and over the piece being dragged.
        // The first two rows of that list live on _under / _fxAddUnder (see _Ready): the panel
        // and BOTH debris passes — the additive one included, or the promise would hold for
        // eight materials and quietly break on the ninth.
        // ---------------------------------------------------------------------------------

        // Grid cells. Filled cells ride the shock wave the clear emitted — the board itself
        // recoils. BoardShock returns false when nothing is displacing (and ALWAYS under reduced
        // motion), so the common frame costs one early-out per cell and zero extra draws.
        for (int r = 0; r < BlockFitGame.Size; r++)
            for (int c = 0; c < BlockFitGame.Size; c++)
            {
                var cellRect = new Rect2(_boardOrigin + new Vector2(c * _cell, r * _cell) + new Vector2(1, 1),
                                         new Vector2(_cell - 2, _cell - 2));
                var t = _game.At(r, c);
                if (t == PieceType.Empty)
                {
                    DrawRect(cellRect, new Color(1, 1, 1, 0.045f), filled: false, width: 1f);
                    continue;
                }
                if (_burst.BoardShock(cellRect.GetCenter(), out var sdir, out var samp))
                    cellRect.Position += sdir * (samp * _cell * 0.18f);
                BlockRender.DrawCell(this, cellRect, _cell, t, 1f, mat, glyph, _shimmer, r + c, reduced: reduced);
            }

        // Line-clear celebration over the cells, but UNDER everything interactive.
        foreach (var b in _bands)
        {
            float bt = b.Age / ClearBandLife;               // 0 → 1
            float ba = (1f - bt) * (1f - bt);               // ease-out fade
            float thick = _cell * (1f + 0.5f * (1f - bt));  // swell then settle
            var bcol = new Color(1f, 1f, 1f, ba).Lerp(new Color(1f, 0.82f, 0.2f, ba), bt);
            if (b.Row)
                DrawRect(new Rect2(_boardOrigin.X, _boardOrigin.Y + (b.Index + 0.5f) * _cell - thick / 2f, boardPx, thick), bcol, filled: true);
            else
                DrawRect(new Rect2(_boardOrigin.X + (b.Index + 0.5f) * _cell - thick / 2f, _boardOrigin.Y, thick, boardPx), bcol, filled: true);
        }
        // Particle bodies (paper/glass) + Supernova vignette; debris was already drawn under the
        // cells above, and the glow half lives on _fxAdd.
        _burst.DrawNormal(this, _cell, new Rect2(Vector2.Zero, Bootstrap.Instance.SafeCanvasSize), includeDebris: false);

        // The drag verdict, resolved ONCE and reused by every preview below, so the highlights and
        // the release path can never tell different stories.
        var dragged = DraggedPiece();
        int dgr = 0, dgc = 0;
        bool dragOnBoard = false, dragOk = false;
        if (dragged is { } dpv)
        {
            dragOnBoard = TargetCell(dpv, _finger, out dgr, out dgc);
            dragOk = dragOnBoard && _game.CanPlace(dpv, dgr, dgc);
        }

        // Reserve (HOLD) slot: panel + parked piece. Highlights green when a tray piece is being
        // dragged over it (stash target). Only a tray drag (not the held piece itself) can stash,
        // and a verified board aim outranks it (see Release) — otherwise the highlight would
        // promise a stash that the bottom row is about to steal.
        bool stashHover = _dragIndex is >= 0 and < 3 && !dragOk && _holdSlot.HasPoint(_finger);
        DrawRect(_holdSlot.Grow(-3f), new Color(0.06f, 0.07f, 0.13f, 0.80f), filled: true);
        DrawRect(_holdSlot.Grow(-3f), stashHover ? new Color(0.35f, 1f, 0.6f, 0.95f) : new Color(1, 1, 1, 0.12f),
                 filled: false, width: stashHover ? 3f : 1.5f);
        if (_game.Held is { } heldPiece && _dragIndex != HoldSlot)
            DrawPiece(heldPiece, HoldPieceOrigin(heldPiece), _holdCell, 1f, TextureFactory.Cell(Mathf.Clamp((int)_holdCell, 8, 128)));

        // Tray pieces (skip the dragged one, and the merge target — the merge preview redraws it).
        // Merge only applies to tray drags, so the merge target is -1 while dragging the held piece.
        int mergeHover = _dragIndex is >= 0 and < 3 ? MergeTargetSlot(_finger, _dragIndex) : -1;
        var trayTex = TextureFactory.Cell(Mathf.Clamp((int)_trayCell, 8, 128));
        // Merge affordance. While a tray piece is in hand it outlines every OTHER tray piece —
        // the exact rects that accept a fuse. It ALSO comes up while the idle hint is showing:
        // before you ever lift a piece there is nothing on screen saying "fuse" exists, and the
        // idle moment (5s of no move — the player is scanning for options) is precisely when a
        // second option is worth naming. It costs at most 3 rects, retires the instant anything
        // is touched, and needs two occupied slots to mean anything.
        bool showMergeHint = _dragIndex is >= 0 and < 3 || (_hintOn && _dragIndex == -1 && OccupiedTraySlots() >= 2);
        for (int i = 0; i < 3; i++)
        {
            var p = _game.Tray[i];
            if (p is null || i == _dragIndex || i == mergeHover) continue;
            var porigin = TrayPieceOrigin(p, i);
            DrawPiece(p, porigin, _trayCell, 1f, trayTex);
            if (showMergeHint) DrawMergeTarget(p, porigin, _traySlot[i], hot: false);
        }

        // Idle hint (after 5s without a move): pulse the suggested placement + its tray slot
        // so a stuck player sees exactly where a piece fits (FindHint prefers a line-clearing spot).
        if (_hintOn && _dragIndex == -1 && _hintIdx >= 0 && _game.Tray[_hintIdx] is { } hp)
        {
            // Reduced motion gets the same cue held at a steady mid-brightness — the hint is
            // information, the throb is not.
            float pulse = Motion.Reduced ? 0.7f : 0.5f + 0.5f * Mathf.Sin(_hintPulse * 4f);
            var horigin = _boardOrigin + new Vector2(_hintC * _cell, _hintR * _cell);
            var fill = new Color(0.25f, 1f, 0.5f, 0.16f + 0.26f * pulse);
            var edge = new Color(0.4f, 1f, 0.6f, 0.55f + 0.35f * pulse);
            foreach (var (drr, dcc) in hp.Cells)
            {
                var hc = new Rect2(horigin + new Vector2(dcc * _cell, drr * _cell) + new Vector2(2, 2), new Vector2(_cell - 4, _cell - 4));
                DrawRect(hc, fill, filled: true);
                DrawRect(hc, edge, filled: false, width: 2f);
            }
            DrawRect(_traySlot[_hintIdx].Grow(-4f), edge, filled: false, width: 3f);

            // If the suggested move actually CLEARS something, mark the lines it would pop with a
            // thin gold rail. The suggestion itself stays green; the payoff is told by POSITION
            // (which row/column lights up) rather than by hue alone, so it survives a colour-blind
            // palette and it stops the hint from reading as "any legal square will do".
            _game.LinesClearedBy(hp, _hintR, _hintC, _pvRows, _pvCols);
            if (_pvRows.Count + _pvCols.Count > 0)
            {
                var gold = new Color(Palette.AccentGold.R, Palette.AccentGold.G, Palette.AccentGold.B, 0.30f + 0.35f * pulse);
                float rail = Mathf.Max(3f, _cell * 0.16f);
                foreach (int rr in _pvRows)
                    DrawRect(new Rect2(_boardOrigin.X, _boardOrigin.Y + (rr + 0.5f) * _cell - rail / 2f, boardPx, rail), gold, filled: true);
                foreach (int cc in _pvCols)
                    DrawRect(new Rect2(_boardOrigin.X + (cc + 0.5f) * _cell - rail / 2f, _boardOrigin.Y, rail, boardPx), gold, filled: true);
            }
        }

        // Merge preview: while dragging over another occupied tray slot, show the fused shape
        // LIVE in that slot — the source snaps to the finger cell-by-cell so the pieces join
        // EXACTLY where the player wants (green = legal, red = overlap / too big to fit).
        int mergeRow = 0, mergeCol = 0;
        var mergeVerdict = MergeVerdict.NoTarget;
        if (mergeHover >= 0 && _game.Tray[_dragIndex] is { } mdp)
        {
            var mdst = _game.Tray[mergeHover]!;
            MergeOffset(mdp, mergeHover, out mergeRow, out mergeCol);
            mergeVerdict = _game.CheckMerge(_dragIndex, mergeHover, mergeRow, mergeCol);
            bool mergeOk = mergeVerdict == MergeVerdict.Ok;
            bool spent = mergeVerdict == MergeVerdict.NoCharges;
            var borigin = TrayPieceOrigin(mdst, mergeHover);
            DrawPiece(mdst, borigin, _trayCell, 0.5f, trayTex);   // destination, dimmed
            // Bright version of the same outline the other slots wear — the hit area is
            // confirmed in place, so the player learns where the accepted zone actually is.
            var hitRect = MergeHitRect(mdst, borigin, _trayCell, _traySlot[mergeHover]);
            DrawMergeTarget(mdst, borigin, _traySlot[mergeHover], hot: true, spent: spent);
            // Amber, not red, when the budget is spent: red here means "these two shapes can't
            // join, try another offset", and that advice is useless — and misleading — when the
            // real answer is "clear a line first".
            var srcCol = mergeOk ? new Color(0.4f, 1f, 0.6f) : spent ? Palette.AccentGold : Palette.AccentRed;
            foreach (var (dr, dc) in mdp.Cells)
            {
                var rect = new Rect2(borigin + new Vector2((dc + mergeCol) * _trayCell, (dr + mergeRow) * _trayCell) + new Vector2(1, 1), new Vector2(_trayCell - 2, _trayCell - 2));
                DrawTextureRect(trayTex, rect, false, new Color(srcCol.R, srcCol.G, srcCol.B, mergeOk ? 0.95f : 0.6f));
            }
            // Shape channel for "spent": the target is struck through. Colour alone would put the
            // whole distinction on hue, and the loupe above draws the same slash so the two
            // pictures of the same fuse always agree.
            if (spent)
                DrawLine(hitRect.Position, hitRect.Position + hitRect.Size,
                         new Color(Palette.AccentGold.R, Palette.AccentGold.G, Palette.AccentGold.B, 0.9f), width: 3f);
        }

        // Dragged piece (a tray piece OR the held piece). While aiming at the board the piece
        // itself snaps onto the cells it will occupy (WYSIWYG) with alignment rails + clear
        // preview; while over the hold/merge slots those slot previews take over and the board
        // aim is suppressed — the magnifier lives on the MERGE path, where the tray cells are
        // genuinely too small to judge a cell-precise join.
        if (_dragIndex != -1 && dragged is { } dp)
        {
            // "aimingBoard" now means the finger is REALLY over the board. When it is false a
            // release does nothing at all, and the drawing says so: no footprint, no rails, no
            // clear preview — the piece just hangs off the finger. The affordance and the
            // outcome are the same fact, so a miss is visible before it happens.
            int gr = dgr, gc = dgc;
            bool aimingBoard = dragOnBoard && mergeHover < 0 && !stashHover;
            bool ok = aimingBoard && dragOk;
            var origin = _boardOrigin + new Vector2(gc * _cell, gr * _cell);

            if (aimingBoard && ok)
            {
                // Clear preview: if dropping here completes any line, flood that whole
                // row/column bright green so the payoff is unmistakable before releasing.
                _game.LinesClearedBy(dp, gr, gc, _pvRows, _pvCols);
                var glow = new Color(0.15f, 1f, 0.45f, 0.32f);
                var edge = new Color(0.3f, 1f, 0.55f, 0.9f);
                foreach (int rr in _pvRows)
                {
                    var band = new Rect2(_boardOrigin + new Vector2(0, rr * _cell), new Vector2(boardPx, _cell));
                    DrawRect(band, glow, filled: true);
                    DrawRect(band, edge, filled: false, width: 2f);
                }
                foreach (int cc in _pvCols)
                {
                    var band = new Rect2(_boardOrigin + new Vector2(cc * _cell, 0), new Vector2(_cell, boardPx));
                    DrawRect(band, glow, filled: true);
                    DrawRect(band, edge, filled: false, width: 2f);
                }
            }

            if (aimingBoard)
            {
                var col = ok ? Palette.ForPiece(dp.Color) : Palette.AccentRed;
                var bb = new Rect2(origin, new Vector2(dp.Width * _cell, dp.Height * _cell));

                // Alignment rails: the footprint's row band and column band extended to the board
                // edges. Reading "which row/column am I on" no longer needs a zoomed inset — the
                // rails answer it peripherally without covering a single cell.
                var rail = new Color(col.R, col.G, col.B, 0.28f);
                DrawRect(new Rect2(_boardOrigin.X, bb.Position.Y, boardPx, bb.Size.Y), rail, filled: false, width: 1.5f);
                DrawRect(new Rect2(bb.Position.X, _boardOrigin.Y, bb.Size.X, boardPx), rail, filled: false, width: 1.5f);
                DrawRect(bb.Grow(2f), new Color(col.R, col.G, col.B, 0.55f), filled: false, width: 2f);

                // Footprint: tinted FILL (not a hairline outline) so the landing cells read as a
                // solid target even under a hand, plus a bright per-cell border.
                foreach (var (drr, dcc) in dp.Cells)
                {
                    var gcell = new Rect2(origin + new Vector2(dcc * _cell, drr * _cell) + new Vector2(2, 2), new Vector2(_cell - 4, _cell - 4));
                    DrawRect(gcell, new Color(col.R, col.G, col.B, ok ? 0.30f : 0.22f), filled: true);
                    DrawRect(gcell, new Color(col.R, col.G, col.B, 0.95f), filled: false, width: 2.5f);
                }
            }

            // The piece itself. Over the board it is drawn ON the target cells — what you see is
            // exactly what lands, so no magnifier is needed; the drop point still sits clear of
            // the fingertip because TargetCell derives it from a lifted origin. Down in the tray
            // (just lifted, or aiming at hold/merge) it free-floats with the finger instead.
            var lift = _cell * 0.6f;
            var floatOrigin = aimingBoard
                ? origin
                : new Vector2(_finger.X - dp.Width * _cell / 2f, _finger.Y - lift - dp.Height * _cell);
            DrawPiece(dp, floatOrigin, _cell, aimingBoard && !ok ? 0.6f : 1f, tex);

            // Merge magnifier — drawn LAST so the floating piece can never cover it. The drawing
            // itself lives in the shared MergeLoupe so the duel screen shows the SAME panel.
            if (mergeHover >= 0 && _game.Tray[mergeHover] is { } mdst2)
                MergeLoupe.Draw(this, dp, mdst2, mergeRow, mergeCol, mergeVerdict,
                                Bootstrap.Instance.SafeCanvasSize, _boardOrigin.Y, _holdSlot.Position.Y,
                                _trayCell, _shimmer);
        }
    }

    private int OccupiedTraySlots()
    {
        int n = 0;
        for (int i = 0; i < 3; i++) if (_game.Tray[i] is not null) n++;
        return n;
    }

    /// <summary>Outlines the rect that actually accepts a merge drop — the very rect the hit test
    /// and the grab test use. Dim + thin while the finger is elsewhere (an affordance), bright +
    /// thick while it is over it (a confirmation) — the state reads from line weight as much as
    /// from hue, so it survives a colour-blind palette. <paramref name="spent"/> paints the hot
    /// outline amber instead of green: the rect is still the right target, it just cannot fire.
    /// Static (no pulse), so no Motion.Reduced gate is owed.</summary>
    private void DrawMergeTarget(BlockPiece p, Vector2 origin, Rect2 slot, bool hot, bool spent = false)
    {
        if (_trayCell <= 0f) return;
        var col = hot
            ? (spent ? new Color(Palette.AccentGold.R, Palette.AccentGold.G, Palette.AccentGold.B, 0.95f)
                     : new Color(0.4f, 1f, 0.6f, 0.95f))
            : new Color(Palette.Accent.R, Palette.Accent.G, Palette.Accent.B, 0.34f);
        DrawRect(MergeHitRect(p, origin, _trayCell, slot), col, filled: false, width: hot ? 3f : 1.5f);
    }

    private Vector2 TrayPieceOrigin(BlockPiece p, int slot)
    {
        var s = _traySlot[slot];
        float pw = p.Width * _trayCell, ph = p.Height * _trayCell;
        return s.Position + new Vector2((s.Size.X - pw) / 2f, (s.Size.Y - ph) / 2f);
    }

    /// <summary>Centre the held piece in the reserve slot, below the "HOLD" caption band.</summary>
    private Vector2 HoldPieceOrigin(BlockPiece p)
    {
        float pw = p.Width * _holdCell, ph = p.Height * _holdCell;
        return _holdSlot.Position + new Vector2(
            (_holdSlot.Size.X - pw) / 2f,
            HoldCaptionH + (_holdSlot.Size.Y - HoldCaptionH - ph) / 2f);
    }

    private void DrawPiece(BlockPiece p, Vector2 origin, float cell, float alpha, Texture2D tex)
    {
        var mat = Palette.EquippedMaterial;
        var glyph = Palette.EquippedGlyph;
        bool reduced = Motion.Reduced;
        foreach (var (dr, dc) in p.Cells)
        {
            var rect = new Rect2(origin + new Vector2(dc * cell, dr * cell) + new Vector2(1, 1), new Vector2(cell - 2, cell - 2));
            BlockRender.DrawCell(this, rect, cell, p.Color, alpha, mat, glyph, _shimmer, dr + dc, reduced: reduced);
        }
    }

    // ---- Game over ----

    private void BuildGameOverOverlay()
    {
        _overlay = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _uiHost.AddChild(_overlay);
        var scrim = new ColorRect { Color = new Color(0, 0, 0, 0.66f) };
        scrim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _overlay.AddChild(scrim);

        // CenterContainer keeps the card truly centered as its min size settles —
        // an anchor preset computed before the children exist pins the top-left instead,
        // which is why this card drifted to the bottom-right (same fix as GameController's
        // pause overlay).
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _overlay.AddChild(center);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 16);
        center.AddChild(box);

        var title = new Label { Text = Loc.T("GAME OVER"), HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 40);
        box.AddChild(title);
        // A run that ends should say what it was worth. Block Fit has no results screen on
        // purpose (a new screen would drag the interstitial gating in with it), so the payload
        // lands here: record badge, the score, the best it is measured against, and the run's
        // longest streak. Everything is spelled out in words/numbers — the gold badge is a
        // bonus signal, never the only one.
        _overBadge = new Label { Text = Loc.T("NEW BEST"), HorizontalAlignment = HorizontalAlignment.Center, Visible = false };
        _overBadge.AddThemeFontSizeOverride("font_size", 22);
        _overBadge.AddThemeColorOverride("font_color", Palette.AccentGold);
        box.AddChild(_overBadge);

        _overScore = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _overScore.AddThemeFontSizeOverride("font_size", 24);
        box.AddChild(_overScore);

        _overBest = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _overBest.AddThemeFontSizeOverride("font_size", 18);
        _overBest.AddThemeColorOverride("font_color", Palette.TextSecondary);
        box.AddChild(_overBest);

        _overStreak = new Label { HorizontalAlignment = HorizontalAlignment.Center, Visible = false };
        _overStreak.AddThemeFontSizeOverride("font_size", 18);
        _overStreak.AddThemeColorOverride("font_color", Palette.Accent);
        box.AddChild(_overStreak);

        var retry = new Button { Text = Loc.T("RETRY"), ThemeTypeVariation = "PrimaryButton", CustomMinimumSize = new Vector2(260, TouchTarget) };
        Motion.BindButtonFeel(retry);
        retry.Pressed += NewGame;
        box.AddChild(retry);
        var menu = new Button { Text = Loc.T("MENU"), CustomMinimumSize = new Vector2(260, TouchTarget) };
        Motion.BindButtonFeel(menu);
        menu.Pressed += () => QuitRequested?.Invoke();
        box.AddChild(menu);
    }

    // ---- First-run coaching ----

    /// <summary>
    /// Five lines, once, the first time anyone opens Block Fit — the mode the menu's PLAY
    /// card actually launches, which had no explanation anywhere in the game (the HOW TO PLAY
    /// tutorial teaches the falling-block game instead). This is an in-game overlay, not a
    /// screen: <see cref="SceneRouter.GoToStart"/> deliberately never force-launches a
    /// tutorial screen, and that decision stands. Dismissing it hands straight over to the
    /// existing idle-hint system, which then points at a real first move.
    /// </summary>
    /// <summary>
    /// The coach card's REVISION flag. <c>Save.BlockFitIntroSeen</c> is a plain bool that v1.4.2
    /// set the moment anyone read the 3-line version of this card, so the two lines added since —
    /// the ones that name FUSE and HOLD at all — could never reach an existing player. Those are
    /// exactly the players stranded by the tightened merge judgement: they don't know the feature
    /// exists, so they can't know why a drop did nothing.
    ///
    /// Versioning it needs a second, independent flag, and SaveManager belongs to another team.
    /// The owned-items set is the one string-keyed sticky store it already exposes, it is only
    /// ever read as <c>OwnsItem(catalogId)</c> (never enumerated for display, so a reserved
    /// non-catalog id is invisible), and its cloud merge is a union — precisely "seen, on any
    /// device, forever". Bump the suffix to re-show the card after a future revision.
    /// See the handoff note: a first-class <c>Save.SeenFlag(string)</c> would retire this.
    /// </summary>
    private const string CoachFlagV2 = "__coach_blockfit_v2";

    private static bool CoachSeen
        => Bootstrap.Instance.Save.BlockFitIntroSeen && Bootstrap.Instance.Save.OwnsItem(CoachFlagV2);

    private void BuildCoachCard()
    {
        var scrim = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        _coach = scrim;
        _uiHost.AddChild(scrim);
        scrim.Position = Vector2.Zero;
        scrim.Size = Bootstrap.Instance.SafeCanvasSize;
        var shade = new ColorRect { Color = new Color(0, 0, 0, 0.72f) };
        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        scrim.AddChild(shade);

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        scrim.AddChild(center);

        var card = new PanelContainer { ThemeTypeVariation = "Card" };
        center.AddChild(card);
        // Every wrapping label needs this width as its OWN minimum: a Label with autowrap
        // inside a container reports its min height for the width it has been given, and a
        // width of zero makes it one word per line — the card grew to 6387px tall.
        float textW = Mathf.Clamp(Bootstrap.Instance.SafeCanvasSize.X - 120f, 240f, 520f);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(textW, 0) };
        box.AddThemeConstantOverride("separation", 14);
        card.AddChild(box);

        // A returning player is not being taught the game, they are being told what they missed —
        // say so, or the card reads as a bug.
        var title = new Label
        {
            Text = Loc.T(Bootstrap.Instance.Save.BlockFitIntroSeen ? "NEW  ·  FUSE & HOLD" : "BLOCK FIT"),
            ThemeTypeVariation = "TitleLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 34);
        title.AddThemeColorOverride("font_color", Palette.Accent);
        box.AddChild(title);

        foreach (var line in new[]
                 {
                     Loc.T("1  ·  DRAG a piece from the tray onto the grid."),
                     Loc.T("2  ·  FILL a whole row or column and it clears."),
                     // Fusing and stashing were both fully built and mentioned NOWHERE: the odds
                     // of discovering "let go on top of another tray piece" by accident are ~0,
                     // and HOLD was a 15px caption. Two lines is the whole fix.
                     // The fuse budget has to be taught here too: a player who only meets it as a
                     // preview that suddenly refuses reads it as a bug, not a rule.
                     Loc.T("3  ·  DROP a piece on ANOTHER TRAY PIECE to fuse them — fuses are limited, and clearing a line earns one back."),
                     Loc.T("4  ·  DROP a piece on the HOLD slot to save it for later."),
                     Loc.T("5  ·  It ends when none of the three pieces fit."),
                 })
        {
            var l = new Label
            {
                Text = line,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(textW, 0),
            };
            l.AddThemeFontSizeOverride("font_size", 20);
            l.AddThemeColorOverride("font_color", Palette.TextPrimary);
            box.AddChild(l);
        }

        var go = new Button
        {
            Text = Loc.T("GOT IT"),
            ThemeTypeVariation = "PrimaryButton",
            CustomMinimumSize = new Vector2(Mathf.Min(240f, textW), TouchTarget),
        };
        Motion.BindButtonFeel(go);
        go.Pressed += DismissCoach;
        box.AddChild(go);
    }

    private void DismissCoach()
    {
        if (_coach is null || !GodotObject.IsInstanceValid(_coach)) { _coach = null; return; }
        var c = _coach;
        _coach = null;
        Bootstrap.Instance.Save.BlockFitIntroSeen = true;
        Bootstrap.Instance.Save.GrantItem(CoachFlagV2);
        // Hand over to the idle hint: instead of a fresh 5s wait, the first "put it here"
        // pulse comes up immediately, so the card's words are followed by a live example.
        _idle = HintDelay;
        c.MouseFilter = Control.MouseFilterEnum.Ignore;
        if (Motion.Reduced) { c.QueueFree(); return; }
        var tw = CreateTween();
        tw.TweenProperty(c, "modulate:a", 0f, 0.22f);
        tw.TweenCallback(Callable.From(c.QueueFree));
    }

    private void ShowGameOver()
    {
        _overScore.Text = Loc.T("SCORE {0}", _game.Score);
        _overBadge.Visible = _newBest;
        _overBest.Text = Loc.T("BEST {0}", CurrentBest());
        _overStreak.Visible = _runBestStreak >= 2;
        if (_overStreak.Visible) _overStreak.Text = Loc.T("BEST STREAK ×{0}", _runBestStreak);
        _overlay.Visible = true;
        Bootstrap.Instance.Audio.PlaySfx("game_over");
    }

    private void NewGame()
    {
        _game = _seed != 0 ? new BlockFitGame(_seed) : new BlockFitGame();
        _overlay.Visible = false;
        _dragIndex = -1; _touchId = int.MinValue;
        _rainTimer = 0f;
        _runBestStreak = 0; _newBest = false;
        _streak.Visible = false;
        _artifact = BurstArtifacts.FromId(Bootstrap.Instance.Save.EquippedArtifactId);
        _bands.Clear(); _burst.Clear(); _shardColors.Clear();
        ResetIdle();
        RedrawAll();
    }
}

/// <summary>
/// A bare canvas item that renders a host-supplied callback. Its whole reason to exist is
/// z-order: a Godot CanvasItem paints itself BEFORE its children, so a host that needs
/// something drawn underneath its own <c>_Draw</c> cannot do it in that method — it needs a
/// second item at a lower z. Normal alpha (the additive twin is <see cref="AdditiveFxLayer"/>).
/// </summary>
public sealed partial class BoardUnderLayer : Node2D
{
    private readonly Action<CanvasItem> _draw;
    public BoardUnderLayer(Action<CanvasItem> draw) => _draw = draw;
    public override void _Draw() => _draw(this);
}
