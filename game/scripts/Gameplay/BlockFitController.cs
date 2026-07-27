using Godot;
using System;
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
    private Label _score = null!, _best = null!, _combo = null!;
    private Label _holdLabel = null!;
    private Control _overlay = null!;
    private Label _overScore = null!;

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
    private AdditiveFxLayer _fxAdd = null!;
    private float _shimmer;
    // Pre-clear cell colours (key = r*Size+c) captured before TryPlace so Shards fly in
    // the exact hues that shattered.
    private readonly System.Collections.Generic.Dictionary<int, PieceType> _shardColors = new();

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

        // "HOLD" caption over the reserve slot (positioned in Layout). Drawn on _uiHost so it
        // sits above the slot panel painted in _Draw.
        _holdLabel = new Label { Text = Loc.T("HOLD"), HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        _holdLabel.AddThemeFontSizeOverride("font_size", 15);
        _holdLabel.AddThemeColorOverride("font_color", Palette.TextSecondary);
        _uiHost.AddChild(_holdLabel);

        // Back button (top-left corner). Sized/positioned in Layout so it lines up with the
        // score in the header band.
        _back = new Button { Text = "‹", CustomMinimumSize = new Vector2(52, 52), MouseFilter = Control.MouseFilterEnum.Stop };
        _back.AddThemeFontSizeOverride("font_size", 32);
        _back.Pressed += () => QuitRequested?.Invoke();
        Motion.BindButtonFeel(_back);
        _back.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _uiHost.AddChild(_back);

        BuildGameOverOverlay();
        _best.Text = Loc.T("BEST {0}", CurrentBest());
        _artifact = BurstArtifacts.FromId(Bootstrap.Instance.Save.EquippedArtifactId);

        // Additive glow surface for the burst FX. A Node2D (NOT CanvasLayer — that would drop
        // modulate and break crossfades); drawn UNDER _uiHost so the score/back button stay on top.
        _fxAdd = new AdditiveFxLayer(_burst, () => _cell, () => new Rect2(Vector2.Zero, Bootstrap.Instance.SafeCanvasSize))
        {
            Position = Vector2.Zero,
        };
        AddChild(_fxAdd);
        MoveChild(_fxAdd, 0);   // draw before _uiHost (glow under UI)

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

    private void Layout()
    {
        var safe = Bootstrap.Instance.SafeCanvasSize;
        _uiHost.Position = Vector2.Zero;
        _uiHost.Size = safe;

        float headerH = Mathf.Clamp(safe.Y * 0.09f, 56f, 120f);
        // Board is width-bound on a phone; leave room for the tray below.
        _cell = Mathf.Floor(Mathf.Min(safe.X * 0.94f / BlockFitGame.Size, safe.Y * 0.58f / BlockFitGame.Size));
        float boardPx = _cell * BlockFitGame.Size;
        _boardOrigin = new Vector2((safe.X - boardPx) / 2f, headerH + safe.Y * 0.02f);

        float trayTop = _boardOrigin.Y + boardPx + safe.Y * 0.03f;
        float trayH = Mathf.Max(48f, safe.Y - trayTop - safe.Y * 0.03f);
        // Reserve a compact column on the left for the HOLD slot; the 3 tray slots split the rest.
        float holdW = safe.X * 0.22f;
        float slotW = (safe.X - holdW) / 3f;
        _holdSlot = new Rect2(0f, trayTop, holdW, trayH);
        // A tray piece is at most 5 cells wide / 3 tall; size the mini-cell to fit a slot.
        _trayCell = Mathf.Floor(Mathf.Min(_cell * 0.55f, Mathf.Min(slotW * 0.9f / 5f, trayH / 3.4f)));
        for (int i = 0; i < 3; i++)
            _traySlot[i] = new Rect2(holdW + i * slotW, trayTop, slotW, trayH);
        // The held piece can be up to 5×5; leave ~22px at the slot top for the "HOLD" caption.
        _holdCell = Mathf.Floor(Mathf.Max(6f, Mathf.Min(_trayCell, Mathf.Min((holdW - 10f) / 5f, (trayH - 24f) / 5f))));
        _holdLabel.Position = new Vector2(_holdSlot.Position.X, _holdSlot.Position.Y + 2f);
        _holdLabel.Size = new Vector2(_holdSlot.Size.X, 20f);

        // Header row: back button, score (left) and best (right) all vertically centred in
        // the header band so they line up (the score used to sit above a smaller button).
        float pad = Mathf.Max(12f, safe.X * 0.035f);
        const float backSize = 52f;
        _back.Size = new Vector2(backSize, backSize);
        _back.Position = new Vector2(pad, (headerH - backSize) / 2f);

        float scoreLeft = _back.Position.X + backSize + 14f;
        _score.Position = new Vector2(scoreLeft, 0f); _score.Size = new Vector2(Mathf.Max(40f, safe.X * 0.5f - scoreLeft), headerH);
        _best.Position = new Vector2(safe.X * 0.5f, 0f); _best.Size = new Vector2(safe.X * 0.5f - pad, headerH);
        _combo.Position = new Vector2(0, headerH); _combo.Size = new Vector2(safe.X, 40);
        if (GodotObject.IsInstanceValid(_overlay)) { _overlay.Position = Vector2.Zero; _overlay.Size = safe; }
        QueueRedraw();
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

        float dt = (float)delta;
        for (int i = _bands.Count - 1; i >= 0; i--)
        {
            var b = _bands[i]; b.Age += dt;
            if (b.Age >= ClearBandLife) _bands.RemoveAt(i); else _bands[i] = b;
        }
        _burst.Update(dt);
        _shimmer += dt;                 // drives material breathe / holo scroll / starfield twinkle

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
        QueueRedraw();
        _fxAdd.QueueRedraw();           // additive glow surface tracks the same particle lists
    }

    // ---- Input: grab a tray piece, drag it (floating above the finger), release to place ----

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventScreenTouch t:
                if (t.Pressed) Grab((int)t.Index, t.Position); else Release((int)t.Index, t.Position);
                break;
            case InputEventScreenDrag d:
                if ((int)d.Index == _touchId) { _finger = d.Position; QueueRedraw(); }
                break;
            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Left:
                if (mb.Pressed) Grab(int.MaxValue, mb.Position); else Release(int.MaxValue, mb.Position);
                break;
            case InputEventMouseMotion mm when (mm.ButtonMask & MouseButtonMask.Left) != 0:
                if (_touchId == int.MaxValue) { _finger = mm.Position; QueueRedraw(); }
                break;
        }
    }

    private void Grab(int id, Vector2 pos)
    {
        if (_game.GameOver || _dragIndex != -1) return;
        ResetIdle();
        // Reserve slot: grab the parked piece to place it back on the board.
        if (_game.Held is not null && _holdSlot.HasPoint(pos))
        {
            _dragIndex = HoldSlot; _touchId = id; _finger = pos;
            QueueRedraw();
            return;
        }
        for (int i = 0; i < 3; i++)
            if (_game.Tray[i] is not null && _traySlot[i].HasPoint(pos))
            {
                _dragIndex = i; _touchId = id; _finger = pos;
                QueueRedraw();
                return;
            }
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

        // Stash intent: released over the reserve slot → park this tray piece in hold (swaps if
        // the slot is already occupied). Checked before merge/placement so it always wins here.
        if (_holdSlot.HasPoint(pos) && _game.Tray[idx] is not null)
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
            if (_game.TryMerge(idx, mergeInto, mr, mc))
            {
                Bootstrap.Instance.Audio.PlaySfx("hold");   // fuse cue
                if (_game.GameOver) ShowGameOver();
            }
            else Bootstrap.Instance.Audio.PlaySfx("move");  // overlap or too big to fit
            QueueRedraw();
            return;
        }

        var piece = _game.Tray[idx];
        if (piece is not null && TargetCell(piece, pos, out int gr, out int gc) && _game.CanPlace(piece, gr, gc))
            CommitPlacement(piece, gr, gc, fromHold: false, trayIdx: idx);
        else
            Bootstrap.Instance.Audio.PlaySfx("move"); // snap-back cue
        QueueRedraw();
    }

    /// <summary>Release path for the parked (held) piece: place it on the board, or cancel (snap
    /// back into hold) if it was dropped over the reserve slot again.</summary>
    private void ReleaseHeld(Vector2 pos)
    {
        if (_holdSlot.HasPoint(pos)) { Bootstrap.Instance.Audio.PlaySfx("move"); return; }
        var piece = _game.Held;
        if (piece is not null && TargetCell(piece, pos, out int gr, out int gc) && _game.CanPlace(piece, gr, gc))
            CommitPlacement(piece, gr, gc, fromHold: true, trayIdx: -1);
        else
            Bootstrap.Instance.Audio.PlaySfx("move");
    }

    /// <summary>Commit a legal drop (from a tray slot or the hold slot): snapshot the lines it
    /// completes, place it, and fire the audio + line-clear celebration + best-score update. The
    /// preview snapshot must happen BEFORE the place call, which clears those lines.</summary>
    private void CommitPlacement(BlockPiece piece, int gr, int gc, bool fromHold, int trayIdx)
    {
        _game.LinesClearedBy(piece, gr, gc, _pvRows, _pvCols);
        CaptureShardColors(piece, gr, gc);   // snapshot popped hues before the placement clears them
        if (fromHold) _game.TryPlaceHeld(gr, gc); else _game.TryPlace(trayIdx, gr, gc);
        Bootstrap.Instance.Audio.PlaySfx("lock");
        if (_game.LastClearedRows + _game.LastClearedCols > 0)
        {
            Bootstrap.Instance.Audio.PlaySfx(_game.LastClearedRows + _game.LastClearedCols >= 2 ? "combo" : "line_clear");
            int lines = _game.LastClearedRows + _game.LastClearedCols;
            _combo.Text = lines >= 2 ? Loc.T("COMBO ×{0}", lines) : Loc.T("CLEAR");
            _comboFlash = 0.9f;
            SpawnClearFx();
        }
        if (_game.Score > CurrentBest())
        {
            SubmitBest(_game.Score);
            _best.Text = Loc.T("BEST {0}", _game.Score);
        }
        if (_game.GameOver) ShowGameOver();
    }

    /// <summary>Grid origin the dragged piece snaps to — computed so the piece floats
    /// centred ABOVE the finger (fingertip never hides it).</summary>
    private bool TargetCell(BlockPiece p, Vector2 finger, out int gr, out int gc)
    {
        float lift = _cell * 0.6f;
        var topLeft = new Vector2(finger.X - p.Width * _cell / 2f, finger.Y - lift - p.Height * _cell);
        gc = Mathf.RoundToInt((topLeft.X - _boardOrigin.X) / _cell);
        gr = Mathf.RoundToInt((topLeft.Y - _boardOrigin.Y) / _cell);
        gc = Mathf.Clamp(gc, 0, BlockFitGame.Size - p.Width);
        gr = Mathf.Clamp(gr, 0, BlockFitGame.Size - p.Height);
        return true;
    }

    /// <summary>The occupied tray slot the finger is over — other than the dragged one — or
    /// -1. When ≥0 a release fuses the pieces (merge) instead of placing on the board.</summary>
    private int MergeTargetSlot(Vector2 pos, int dragIdx)
    {
        for (int i = 0; i < 3; i++)
            if (i != dragIdx && _game.Tray[i] is not null && _traySlot[i].HasPoint(pos))
                return i;
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

        if (Motion.Reduced || _cell <= 0) return; // the burst artifact is pure juice

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

    private Color PreClearColor(int r, int c)
        => Palette.ForPiece(_shardColors.TryGetValue(r * BlockFitGame.Size + c, out var t) ? t : PieceType.Garbage);

    // ---- Render ----

    public override void _Draw()
    {
        if (_cell <= 0) return;
        float boardPx = _cell * BlockFitGame.Size;
        var tex = TextureFactory.Cell(Mathf.Clamp((int)_cell, 8, 128));
        var glyph = Palette.EquippedGlyph;                      // the equipped skin's block stamp
        var mat = Palette.EquippedMaterial;                     // the equipped skin's finish
        bool reduced = Motion.Reduced;

        // Board panel + empty grid cells.
        DrawRect(new Rect2(_boardOrigin - new Vector2(6, 6), new Vector2(boardPx + 12, boardPx + 12)),
                 new Color(0.05f, 0.06f, 0.11f, 0.85f), filled: true);
        for (int r = 0; r < BlockFitGame.Size; r++)
            for (int c = 0; c < BlockFitGame.Size; c++)
            {
                var cellRect = new Rect2(_boardOrigin + new Vector2(c * _cell, r * _cell) + new Vector2(1, 1),
                                         new Vector2(_cell - 2, _cell - 2));
                var t = _game.At(r, c);
                if (t == PieceType.Empty)
                    DrawRect(cellRect, new Color(1, 1, 1, 0.045f), filled: false, width: 1f);
                else
                    BlockRender.DrawCell(this, cellRect, _cell, t, 1f, mat, glyph, _shimmer, r + c, reduced: reduced);
            }

        // Reserve (HOLD) slot: panel + parked piece. Highlights green when a tray piece is being
        // dragged over it (stash target). Only a tray drag (not the held piece itself) can stash.
        bool stashHover = _dragIndex is >= 0 and < 3 && _holdSlot.HasPoint(_finger);
        DrawRect(_holdSlot.Grow(-3f), new Color(0.06f, 0.07f, 0.13f, 0.80f), filled: true);
        DrawRect(_holdSlot.Grow(-3f), stashHover ? new Color(0.35f, 1f, 0.6f, 0.95f) : new Color(1, 1, 1, 0.12f),
                 filled: false, width: stashHover ? 3f : 1.5f);
        if (_game.Held is { } heldPiece && _dragIndex != HoldSlot)
            DrawPiece(heldPiece, HoldPieceOrigin(heldPiece), _holdCell, 1f, TextureFactory.Cell(Mathf.Clamp((int)_holdCell, 8, 128)));

        // Tray pieces (skip the dragged one, and the merge target — the merge preview redraws it).
        // Merge only applies to tray drags, so the merge target is -1 while dragging the held piece.
        int mergeHover = _dragIndex is >= 0 and < 3 ? MergeTargetSlot(_finger, _dragIndex) : -1;
        var trayTex = TextureFactory.Cell(Mathf.Clamp((int)_trayCell, 8, 128));
        for (int i = 0; i < 3; i++)
        {
            var p = _game.Tray[i];
            if (p is null || i == _dragIndex || i == mergeHover) continue;
            DrawPiece(p, TrayPieceOrigin(p, i), _trayCell, 1f, trayTex);
        }

        // Idle hint (after 5s without a move): pulse the suggested placement + its tray slot
        // so a stuck player sees exactly where a piece fits (FindHint prefers a line-clearing spot).
        if (_hintOn && _dragIndex == -1 && _hintIdx >= 0 && _game.Tray[_hintIdx] is { } hp)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(_hintPulse * 4f);
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
        }

        // Merge preview: while dragging over another occupied tray slot, show the fused shape
        // LIVE in that slot — the source snaps to the finger cell-by-cell so the pieces join
        // EXACTLY where the player wants (green = legal, red = overlap / too big to fit).
        int mergeRow = 0, mergeCol = 0;
        bool mergeOk = false;
        if (mergeHover >= 0 && _game.Tray[_dragIndex] is { } mdp)
        {
            var mdst = _game.Tray[mergeHover]!;
            MergeOffset(mdp, mergeHover, out mergeRow, out mergeCol);
            mergeOk = _game.CanMerge(_dragIndex, mergeHover, mergeRow, mergeCol);
            var borigin = TrayPieceOrigin(mdst, mergeHover);
            DrawRect(_traySlot[mergeHover].Grow(-4f), new Color(0.55f, 0.85f, 1f, 0.4f), filled: false, width: 2f);
            DrawPiece(mdst, borigin, _trayCell, 0.5f, trayTex);   // destination, dimmed
            var srcCol = mergeOk ? new Color(0.4f, 1f, 0.6f) : Palette.AccentRed;
            foreach (var (dr, dc) in mdp.Cells)
            {
                var rect = new Rect2(borigin + new Vector2((dc + mergeCol) * _trayCell, (dr + mergeRow) * _trayCell) + new Vector2(1, 1), new Vector2(_trayCell - 2, _trayCell - 2));
                DrawTextureRect(trayTex, rect, false, new Color(srcCol.R, srcCol.G, srcCol.B, mergeOk ? 0.95f : 0.6f));
            }
        }

        // Dragged piece (a tray piece OR the held piece). While aiming at the board the piece
        // itself snaps onto the cells it will occupy (WYSIWYG) with alignment rails + clear
        // preview; while over the hold/merge slots those slot previews take over and the board
        // aim is suppressed — the magnifier lives on the MERGE path, where the tray cells are
        // genuinely too small to judge a cell-precise join.
        var dragged = DraggedPiece();
        if (_dragIndex != -1 && dragged is { } dp)
        {
            bool aimingBoard = mergeHover < 0 && !stashHover;
            TargetCell(dp, _finger, out int gr, out int gc);   // always returns a clamped target
            bool ok = _game.CanPlace(dp, gr, gc);
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
            bool snapped = aimingBoard && _finger.Y < _holdSlot.Position.Y;
            var floatOrigin = snapped
                ? origin
                : new Vector2(_finger.X - dp.Width * _cell / 2f, _finger.Y - lift - dp.Height * _cell);
            DrawPiece(dp, floatOrigin, _cell, aimingBoard && !ok ? 0.6f : 1f, tex);

            // Merge magnifier — drawn LAST so the floating piece can never cover it.
            if (mergeHover >= 0 && _game.Tray[mergeHover] is { } mdst2)
                DrawMergeLoupe(dp, mdst2, mergeRow, mergeCol, mergeOk);
        }

        // Line-clear celebration (top layer): bright bands over cleared lines + sparks.
        foreach (var b in _bands)
        {
            float t = b.Age / ClearBandLife;               // 0 → 1
            float a = (1f - t) * (1f - t);                 // ease-out fade
            float thick = _cell * (1f + 0.5f * (1f - t));  // swell then settle
            var col = new Color(1f, 1f, 1f, a).Lerp(new Color(1f, 0.82f, 0.2f, a), t);
            if (b.Row)
                DrawRect(new Rect2(_boardOrigin.X, _boardOrigin.Y + (b.Index + 0.5f) * _cell - thick / 2f, boardPx, thick), col, filled: true);
            else
                DrawRect(new Rect2(_boardOrigin.X + (b.Index + 0.5f) * _cell - thick / 2f, _boardOrigin.Y, thick, boardPx), col, filled: true);
        }
        // Particle bodies (paper/glass) + Supernova vignette; the glow half is on _fxAdd.
        _burst.DrawNormal(this, _cell, new Rect2(Vector2.Zero, Bootstrap.Instance.SafeCanvasSize));
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
        const float labelH = 22f;
        float pw = p.Width * _holdCell, ph = p.Height * _holdCell;
        return _holdSlot.Position + new Vector2(
            (_holdSlot.Size.X - pw) / 2f,
            labelH + (_holdSlot.Size.Y - labelH - ph) / 2f);
    }

    /// <summary>
    /// Draw the merge magnifier: a zoomed view of the fused shape while the player drags one tray
    /// piece over another, parked in the empty band just ABOVE the tray so the dragging hand never
    /// covers it. This is where zoom actually earns its keep — a tray cell is at most 0.55× a board
    /// cell, far too small to judge a cell-precise join, whereas board placement is already
    /// WYSIWYG (the piece is drawn on the cells it will occupy). Destination keeps its own colour
    /// (the fused piece inherits it); the source reads green when the fuse is legal and red when
    /// the cells overlap or the result could never fit the board.
    /// </summary>
    private void DrawMergeLoupe(BlockPiece src, BlockPiece dst, int mr, int mc, bool ok)
    {
        var safe = Bootstrap.Instance.SafeCanvasSize;

        // Union bounding box of dst (anchored at 0,0) + src (at the finger-chosen offset), padded
        // by one cell so the join reads against empty space instead of butting the panel edge.
        const int margin = 1;
        int r0 = Mathf.Min(0, mr) - margin, c0 = Mathf.Min(0, mc) - margin;
        int rows = Mathf.Max(dst.Height, mr + src.Height) - r0 + margin;
        int cols = Mathf.Max(dst.Width, mc + src.Width) - c0 + margin;

        float trayTop = _holdSlot.Position.Y;
        float roomY = trayTop - _boardOrigin.Y - 28f;
        float lc = Mathf.Floor(Mathf.Min(Mathf.Min(safe.X * 0.86f / cols, roomY / rows), _trayCell * 3.2f));
        if (lc <= _trayCell * 1.25f) return;   // not enough room to be a meaningful magnification

        float w = cols * lc, h = rows * lc;
        var origin = new Vector2((safe.X - w) / 2f, trayTop - 16f - h);

        // Backing panel — border colour doubles as the legal/illegal verdict at a glance.
        var pad = new Vector2(10, 10);
        var panel = new Rect2(origin - pad, new Vector2(w, h) + pad * 2f);
        var accent = ok ? new Color(0.35f, 1f, 0.60f) : Palette.AccentRed;
        DrawRect(panel, new Color(0.04f, 0.05f, 0.10f, 0.95f), filled: true);
        DrawRect(panel, new Color(accent.R, accent.G, accent.B, 0.75f), filled: false, width: 2.5f);

        var glyph = Palette.EquippedGlyph;
        var mat = Palette.EquippedMaterial;
        bool reduced = Motion.Reduced;

        // Faint lattice so the gaps around the join are countable.
        for (int rr = 0; rr < rows; rr++)
            for (int cc = 0; cc < cols; cc++)
                DrawRect(new Rect2(origin + new Vector2(cc * lc, rr * lc) + new Vector2(1, 1), new Vector2(lc - 2, lc - 2)),
                         new Color(1, 1, 1, 0.05f), filled: false, width: 1f);

        // Destination piece, rendered with the equipped skin so the preview matches the board.
        foreach (var (dr, dc) in dst.Cells)
        {
            var rect = new Rect2(origin + new Vector2((dc - c0) * lc, (dr - r0) * lc) + new Vector2(1, 1), new Vector2(lc - 2, lc - 2));
            BlockRender.DrawCell(this, rect, lc, dst.Color, 1f, mat, glyph, _shimmer, dr + dc, reduced: reduced);
        }

        // Source piece at the finger-chosen offset.
        foreach (var (dr, dc) in src.Cells)
        {
            var rect = new Rect2(origin + new Vector2((dc + mc - c0) * lc, (dr + mr - r0) * lc) + new Vector2(1, 1), new Vector2(lc - 2, lc - 2));
            DrawRect(rect, new Color(accent.R, accent.G, accent.B, ok ? 0.90f : 0.45f), filled: true);
            DrawRect(rect, accent, filled: false, width: 2f);
        }
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

        var box = new VBoxContainer();
        box.SetAnchorsPreset(Control.LayoutPreset.Center);
        box.AddThemeConstantOverride("separation", 16);
        _overlay.AddChild(box);

        var title = new Label { Text = Loc.T("GAME OVER"), HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 40);
        box.AddChild(title);
        _overScore = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _overScore.AddThemeFontSizeOverride("font_size", 24);
        box.AddChild(_overScore);

        var retry = new Button { Text = Loc.T("RETRY"), ThemeTypeVariation = "PrimaryButton", CustomMinimumSize = new Vector2(220, 54) };
        Motion.BindButtonFeel(retry);
        retry.Pressed += NewGame;
        box.AddChild(retry);
        var menu = new Button { Text = Loc.T("MENU"), CustomMinimumSize = new Vector2(220, 48) };
        Motion.BindButtonFeel(menu);
        menu.Pressed += () => QuitRequested?.Invoke();
        box.AddChild(menu);
    }

    private void ShowGameOver()
    {
        _overScore.Text = Loc.T("SCORE {0}", _game.Score);
        _overlay.Visible = true;
        Bootstrap.Instance.Audio.PlaySfx("game_over");
    }

    private void NewGame()
    {
        _game = _seed != 0 ? new BlockFitGame(_seed) : new BlockFitGame();
        _overlay.Visible = false;
        _dragIndex = -1; _touchId = int.MinValue;
        _rainTimer = 0f;
        _artifact = BurstArtifacts.FromId(Bootstrap.Instance.Save.EquippedArtifactId);
        _bands.Clear(); _burst.Clear(); _shardColors.Clear();
        ResetIdle();
        QueueRedraw();
    }
}
