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
/// rotation). Fill a full row/column to clear it. The dragged piece floats ABOVE
/// the finger so the fingertip never hides it — the key mobile control detail.
/// Engine is the tested <see cref="BlockFitGame"/>; this node is render + touch.
/// </summary>
public partial class BlockFitController : Node2D
{
    public event Action? QuitRequested;

    private BlockFitGame _game = new();
    private Control _uiHost = null!;
    private Button _back = null!;
    private Label _score = null!, _best = null!, _combo = null!;
    private Control _overlay = null!;
    private Label _overScore = null!;

    // Geometry (recomputed in Layout).
    private float _cell, _trayCell;
    private Vector2 _boardOrigin;
    private readonly Rect2[] _traySlot = new Rect2[3];

    // Drag state.
    private int _dragIndex = -1;          // tray slot being dragged, or -1
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
    private struct FxSpark { public Vector2 Pos, Vel; public float Age, Life, Size, Grav; public Color Col; }
    private struct FxRing { public Vector2 Pos; public float Age, Life, MaxR; public Color Col; }
    private readonly System.Collections.Generic.List<ClearBand> _bands = new();
    private readonly System.Collections.Generic.List<FxSpark> _fx = new();
    private readonly System.Collections.Generic.List<FxRing> _rings = new();
    private readonly RandomNumberGenerator _fxRng = new();

    // The equipped burst-FX artifact — the cosmetic line-clear style, read from the save on entry.
    private BurstArtifact _artifact;
    private static readonly Color[] PartyColors =
    {
        new(1f, 0.35f, 0.45f), new(1f, 0.8f, 0.3f), new(0.4f, 0.95f, 0.6f), new(0.35f, 0.8f, 1f),
        new(0.75f, 0.5f, 1f), new(1f, 0.55f, 0.85f), new(0.5f, 1f, 0.9f),
    };

    // Idle hint: after HintDelay seconds without a placement, surface a valid move
    // (FindHint prefers one that clears a line) as a pulsing green "put it here" cue.
    private const float HintDelay = 5f;
    private float _idle;
    private bool _hintOn;
    private int _hintIdx = -1, _hintR, _hintC;
    private float _hintPulse;

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

        // Back button (top-left corner). Sized/positioned in Layout so it lines up with the
        // score in the header band.
        _back = new Button { Text = "‹", CustomMinimumSize = new Vector2(52, 52), MouseFilter = Control.MouseFilterEnum.Stop };
        _back.AddThemeFontSizeOverride("font_size", 32);
        _back.Pressed += () => QuitRequested?.Invoke();
        Motion.BindButtonFeel(_back);
        _back.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _uiHost.AddChild(_back);

        BuildGameOverOverlay();
        _best.Text = Loc.T("BEST {0}", (long)(Bootstrap.Instance.Save.BlockFitBest));
        _artifact = BurstArtifacts.FromId(Bootstrap.Instance.Save.EquippedArtifactId);

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
        // A tray piece is at most 5 cells wide / 3 tall; size the mini-cell to fit a slot.
        float slotW = safe.X / 3f;
        _trayCell = Mathf.Floor(Mathf.Min(_cell * 0.55f, Mathf.Min(slotW * 0.9f / 5f, trayH / 3.4f)));
        for (int i = 0; i < 3; i++)
            _traySlot[i] = new Rect2(i * slotW, trayTop, slotW, trayH);

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
        for (int i = _fx.Count - 1; i >= 0; i--)
        {
            var s = _fx[i];
            s.Age += dt; s.Vel.Y += s.Grav * dt; s.Pos += s.Vel * dt; s.Vel *= 0.94f;
            if (s.Age >= s.Life) _fx.RemoveAt(i); else _fx[i] = s;
        }
        for (int i = _rings.Count - 1; i >= 0; i--)
        {
            var rg = _rings[i]; rg.Age += dt;
            if (rg.Age >= rg.Life) _rings.RemoveAt(i); else _rings[i] = rg;
        }

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
        QueueRedraw();
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
        for (int i = 0; i < 3; i++)
            if (_game.Tray[i] is not null && _traySlot[i].HasPoint(pos))
            {
                _dragIndex = i; _touchId = id; _finger = pos;
                QueueRedraw();
                return;
            }
    }

    private void Release(int id, Vector2 pos)
    {
        if (_dragIndex == -1 || id != _touchId) return;
        int idx = _dragIndex;
        _dragIndex = -1; _touchId = int.MinValue;
        _finger = pos;
        ResetIdle();

        // Merge intent: released over a different, occupied tray slot → fuse the two pieces
        // into one larger composite (checked before board placement so it always wins here).
        int mergeInto = MergeTargetSlot(pos, idx);
        if (mergeInto >= 0)
        {
            if (_game.TryMerge(srcIndex: idx, dstIndex: mergeInto))
            {
                Bootstrap.Instance.Audio.PlaySfx("hold");   // fuse cue
                if (_game.GameOver) ShowGameOver();
            }
            else Bootstrap.Instance.Audio.PlaySfx("move");  // refused (result too big to fit)
            QueueRedraw();
            return;
        }

        var piece = _game.Tray[idx];
        if (piece is not null && TargetCell(piece, pos, out int gr, out int gc) && _game.CanPlace(piece, gr, gc))
        {
            // Capture the exact lines this drop completes BEFORE placing — TryPlace
            // then clears them, so the celebration needs the indices up front.
            _game.LinesClearedBy(piece, gr, gc, _pvRows, _pvCols);
            _game.TryPlace(idx, gr, gc);
            Bootstrap.Instance.Audio.PlaySfx("lock");
            if (_game.LastClearedRows + _game.LastClearedCols > 0)
            {
                Bootstrap.Instance.Audio.PlaySfx(_game.LastClearedRows + _game.LastClearedCols >= 2 ? "combo" : "line_clear");
                int lines = _game.LastClearedRows + _game.LastClearedCols;
                _combo.Text = lines >= 2 ? Loc.T("COMBO ×{0}", lines) : Loc.T("CLEAR");
                _comboFlash = 0.9f;
                SpawnClearFx();
            }
            if (_game.Score > (long)Bootstrap.Instance.Save.BlockFitBest)
            {
                Bootstrap.Instance.Save.BlockFitBest = _game.Score;
                _best.Text = Loc.T("BEST {0}", _game.Score);
            }
            if (_game.GameOver) ShowGameOver();
        }
        else
        {
            Bootstrap.Instance.Audio.PlaySfx("move"); // snap-back cue
        }
        QueueRedraw();
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

    private void ResetIdle() { _idle = 0f; _hintOn = false; _hintIdx = -1; _hintPulse = 0f; }

    private void SpawnClearFx()
    {
        // Informational band over each cleared line (always on): _pvRows/_pvCols were
        // captured just before the placement that completed them.
        foreach (int r in _pvRows) _bands.Add(new ClearBand { Row = true, Index = r });
        foreach (int c in _pvCols) _bands.Add(new ClearBand { Row = false, Index = c });

        if (Motion.Reduced || _cell <= 0) return; // the burst artifact is pure juice

        foreach (int r in _pvRows) BurstLine(rowLine: true, r);
        foreach (int c in _pvCols) BurstLine(rowLine: false, c);

        // Background wash, tinted and scaled to the equipped artifact.
        int lines = _pvRows.Count + _pvCols.Count;
        var (pulseCol, pulseBase) = _artifact switch
        {
            BurstArtifact.Supernova => (new Color(1f, 0.98f, 0.9f), 0.34f),
            BurstArtifact.Rainbow => (Palette.AccentViolet, 0.26f),
            BurstArtifact.Fireworks => (Palette.AccentGold, 0.28f),
            BurstArtifact.Confetti => (Palette.AccentGreen, 0.22f),
            BurstArtifact.Shards => (Palette.Accent, 0.24f),
            _ => (Palette.AccentGold, 0.22f),
        };
        Bootstrap.Instance.Bg.Pulse(pulseCol, Mathf.Min(0.6f, pulseBase + lines * 0.1f));
    }

    /// <summary>Spawn the equipped artifact's particle burst along one cleared row/column.</summary>
    private void BurstLine(bool rowLine, int index)
    {
        var lineCenter = rowLine
            ? _boardOrigin + new Vector2(BlockFitGame.Size * 0.5f * _cell, (index + 0.5f) * _cell)
            : _boardOrigin + new Vector2((index + 0.5f) * _cell, BlockFitGame.Size * 0.5f * _cell);

        switch (_artifact)
        {
            case BurstArtifact.Fireworks:
                EmitAlong(rowLine, index, 3, 80f, 260f, 0.5f, 0.9f, 2.5f, 5f, 150f, _ => BrightRandom());
                _rings.Add(new FxRing { Pos = lineCenter, Life = 0.45f, MaxR = _cell * 3.5f, Col = BrightRandom() });
                break;
            case BurstArtifact.Confetti:
                EmitAlong(rowLine, index, 3, 40f, 170f, 0.7f, 1.2f, 3f, 5.5f, 340f, _ => BrightRandom());
                break;
            case BurstArtifact.Supernova:
                EmitAlong(rowLine, index, 2, 140f, 340f, 0.4f, 0.7f, 3f, 6f, 0f, _ => new Color(1f, 0.97f, 0.85f));
                _rings.Add(new FxRing { Pos = lineCenter, Life = 0.55f, MaxR = _cell * BlockFitGame.Size * 0.6f, Col = new Color(1f, 0.98f, 0.9f) });
                break;
            case BurstArtifact.Shards:
                EmitAlong(rowLine, index, 2, 160f, 380f, 0.35f, 0.6f, 4f, 8f, 220f, _ => Palette.ForPiece(RandomPieceColor()));
                break;
            case BurstArtifact.Rainbow:
                EmitAlong(rowLine, index, 2, 80f, 240f, 0.5f, 0.8f, 3f, 5f, 60f, k => Color.FromHsv((float)k / BlockFitGame.Size, 0.85f, 1f));
                break;
            default: // Sparks (free default)
                EmitAlong(rowLine, index, 2, 60f, 220f, 0.35f, 0.6f, 2.5f, 5f, 0f, _ => new Color(1f, 0.95f, 0.6f));
                break;
        }
    }

    /// <summary>Emit <paramref name="count"/> particles from each cell along a line, coloured
    /// by <paramref name="colorFn"/> (its arg is the along-line cell index, for gradients).</summary>
    private void EmitAlong(bool rowLine, int index, int count,
        float spdMin, float spdMax, float lifeMin, float lifeMax,
        float sizeMin, float sizeMax, float grav, System.Func<int, Color> colorFn)
    {
        for (int k = 0; k < BlockFitGame.Size; k++)
        {
            var center = rowLine
                ? _boardOrigin + new Vector2((k + 0.5f) * _cell, (index + 0.5f) * _cell)
                : _boardOrigin + new Vector2((index + 0.5f) * _cell, (k + 0.5f) * _cell);
            var col = colorFn(k);
            for (int s = 0; s < count; s++)
            {
                float ang = _fxRng.RandfRange(0f, Mathf.Tau);
                float spd = _fxRng.RandfRange(spdMin, spdMax);
                _fx.Add(new FxSpark
                {
                    Pos = center,
                    Vel = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * spd,
                    Life = _fxRng.RandfRange(lifeMin, lifeMax),
                    Size = _fxRng.RandfRange(sizeMin, sizeMax),
                    Grav = grav,
                    Col = col,
                });
            }
        }
    }

    private Color BrightRandom() => PartyColors[_fxRng.RandiRange(0, PartyColors.Length - 1)];
    private PieceType RandomPieceColor() => BlockShapes.Colors[_fxRng.RandiRange(0, BlockShapes.Colors.Length - 1)];

    // ---- Render ----

    public override void _Draw()
    {
        if (_cell <= 0) return;
        float boardPx = _cell * BlockFitGame.Size;
        var tex = TextureFactory.Cell(Mathf.Clamp((int)_cell, 8, 128));
        var glyph = Palette.EquippedGlyph;                      // the equipped skin's block stamp
        var glyphInk = new Color(0.05f, 0.06f, 0.10f, 0.82f);   // dark mark that reads on bright neon

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
                {
                    DrawTextureRect(tex, cellRect, false, Palette.ForPiece(t));
                    if (glyph != SkinGlyph.None && _cell >= 18f)
                        GlyphArt.Draw(this, glyph, cellRect.GetCenter(), _cell * 0.62f, glyphInk);
                }
            }

        // Tray pieces (skip the one being dragged).
        for (int i = 0; i < 3; i++)
        {
            var p = _game.Tray[i];
            if (p is null || i == _dragIndex) continue;
            DrawPiece(p, TrayPieceOrigin(p, i), _trayCell, 1f, tex: TextureFactory.Cell(Mathf.Clamp((int)_trayCell, 8, 128)));
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

        // Merge preview: while dragging over another occupied tray slot, ring it in cyan —
        // releasing there fuses the two pieces instead of dropping on the board.
        int mergeHover = _dragIndex == -1 ? -1 : MergeTargetSlot(_finger, _dragIndex);
        if (mergeHover >= 0 && _game.Tray[_dragIndex] is { } mdp)
        {
            DrawRect(_traySlot[mergeHover].Grow(-6f), new Color(0.55f, 0.85f, 1f, 0.85f), filled: false, width: 3f);
            float lift0 = _cell * 0.6f;
            DrawPiece(mdp, new Vector2(_finger.X - mdp.Width * _cell / 2f, _finger.Y - lift0 - mdp.Height * _cell), _cell, 1f, tex);
        }

        // Dragged piece: snapped ghost on the board (valid = bright, invalid = red).
        if (mergeHover < 0 && _dragIndex != -1 && _game.Tray[_dragIndex] is { } dp && TargetCell(dp, _finger, out int gr, out int gc))
        {
            bool ok = _game.CanPlace(dp, gr, gc);
            var origin = _boardOrigin + new Vector2(gc * _cell, gr * _cell);

            // Clear preview: if dropping here completes any line, flood that whole
            // row/column bright green so the payoff is unmistakable before releasing
            // (Block Blast "these lines pop" cue). Drawn under the ghost outline.
            if (ok)
            {
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

            // Ghost footprint.
            foreach (var (drr, dcc) in dp.Cells)
            {
                var gcell = new Rect2(origin + new Vector2(dcc * _cell, drr * _cell) + new Vector2(2, 2), new Vector2(_cell - 4, _cell - 4));
                var col = ok ? Palette.ForPiece(dp.Color) : Palette.AccentRed;
                DrawRect(gcell, new Color(col.R, col.G, col.B, 0.9f), filled: false, width: 2.5f);
            }
            // The floating piece itself, drawn above the finger.
            var lift = _cell * 0.6f;
            var floatOrigin = new Vector2(_finger.X - dp.Width * _cell / 2f, _finger.Y - lift - dp.Height * _cell);
            DrawPiece(dp, floatOrigin, _cell, ok ? 1f : 0.6f, tex);
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
        foreach (var rg in _rings)
        {
            float rt = rg.Age / rg.Life;
            float rad = rg.MaxR * Mathf.Sqrt(Mathf.Clamp(rt, 0f, 1f));   // fast expansion easing out
            float ra = 1f - rt;
            DrawArc(rg.Pos, rad, 0f, Mathf.Tau, 48, new Color(rg.Col.R, rg.Col.G, rg.Col.B, ra * 0.9f), Mathf.Max(2f, _cell * 0.12f));
        }
        foreach (var s in _fx)
        {
            float a = 1f - s.Age / s.Life;
            DrawCircle(s.Pos, s.Size * a, new Color(s.Col.R, s.Col.G, s.Col.B, a));
        }
    }

    private Vector2 TrayPieceOrigin(BlockPiece p, int slot)
    {
        var s = _traySlot[slot];
        float pw = p.Width * _trayCell, ph = p.Height * _trayCell;
        return s.Position + new Vector2((s.Size.X - pw) / 2f, (s.Size.Y - ph) / 2f);
    }

    private void DrawPiece(BlockPiece p, Vector2 origin, float cell, float alpha, Texture2D tex)
    {
        var color = Palette.ForPiece(p.Color);
        var glyph = Palette.EquippedGlyph;
        foreach (var (dr, dc) in p.Cells)
        {
            var rect = new Rect2(origin + new Vector2(dc * cell, dr * cell) + new Vector2(1, 1), new Vector2(cell - 2, cell - 2));
            DrawTextureRect(tex, rect, false, new Color(color.R, color.G, color.B, alpha));
            if (glyph != SkinGlyph.None && cell >= 14f)
                GlyphArt.Draw(this, glyph, rect.GetCenter(), cell * 0.62f, new Color(0.05f, 0.06f, 0.10f, 0.82f * alpha));
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
        _game = new BlockFitGame();
        _overlay.Visible = false;
        _dragIndex = -1; _touchId = int.MinValue;
        _artifact = BurstArtifacts.FromId(Bootstrap.Instance.Save.EquippedArtifactId);
        _bands.Clear(); _fx.Clear(); _rings.Clear();
        ResetIdle();
        QueueRedraw();
    }
}
