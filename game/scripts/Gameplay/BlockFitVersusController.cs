using Godot;
using System;
using Blockfall.Core;
using Blockfall.Core.BlockFit;
using Blockfall.Core.Localization;
using Blockfall.Theme;

namespace Blockfall.Gameplay;

/// <summary>
/// Block Fit CPU duel: two 10×10 placement boards stacked for portrait — the CPU on top
/// (view-only, its tray hidden), you on the bottom with your tray. Drag a piece into place;
/// clearing lines scatters garbage blockers onto the opponent. First side that can't place
/// a piece loses. Engine is the tested <see cref="BlockFitVersus"/>; this node is render +
/// touch (the player drives their board directly — a live match, like VersusController).
/// </summary>
public partial class BlockFitVersusController : Node2D
{
    public event Action? QuitRequested;
    public event Action? RematchRequested;

    private readonly BotDifficulty _diff;
    private readonly ulong _seed;
    private BlockFitVersus _match = null!;

    private Control _uiHost = null!;
    private Button _back = null!;
    private Label _title = null!, _botScore = null!, _pScore = null!, _flash = null!;
    private Control _overlay = null!;
    private Label _overTitle = null!, _overSub = null!;

    // Geometry (recomputed in Layout).
    private float _pCell, _pTrayCell, _bCell;
    private Vector2 _pOrigin, _bOrigin;
    private readonly Rect2[] _pTraySlot = new Rect2[3];

    // Player drag state.
    private int _dragIndex = -1;
    private Vector2 _finger;
    private int _touchId = int.MinValue;
    private readonly System.Collections.Generic.List<int> _pvRows = new(), _pvCols = new();

    // Feedback: bright band over each line the player just cleared + a spark burst, an
    // incoming-garbage red flash, and a floating "SENT / INCOMING" callout.
    private const float BandLife = 0.4f;
    private struct Band { public bool Row; public int Index; public float Age; }
    private struct Spark { public Vector2 Pos, Vel; public float Age, Life, Size; public Color Col; }
    private readonly System.Collections.Generic.List<Band> _bands = new();
    private readonly System.Collections.Generic.List<Spark> _fx = new();
    private readonly RandomNumberGenerator _fxRng = new();
    private float _hitFlash;   // red border pulse on the player board when hit
    private float _flashTtl;   // callout label fade
    private float _shimmer;    // drives the shared block material breathe/shimmer

    public BlockFitVersusController(BotDifficulty difficulty, ulong seed)
    {
        _diff = difficulty;
        _seed = seed;
    }

    public override void _Ready()
    {
        _uiHost = new Control { Name = "UiHost", MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_uiHost);
        UiTheme.ApplyTo(_uiHost);

        _back = new Button { Text = "‹", CustomMinimumSize = new Vector2(52, 52), MouseFilter = Control.MouseFilterEnum.Stop };
        _back.AddThemeFontSizeOverride("font_size", 32);
        _back.Pressed += () => QuitRequested?.Invoke();
        Motion.BindButtonFeel(_back);
        _back.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _uiHost.AddChild(_back);

        _title = MakeLabel(HorizontalAlignment.Center, 22, Palette.AccentViolet);
        _title.Text = Loc.T("VS CPU · {0}", _diff.Name.ToUpperInvariant());
        _botScore = MakeLabel(HorizontalAlignment.Center, 18, Palette.TextSecondary);
        _pScore = MakeLabel(HorizontalAlignment.Center, 20, Palette.TextPrimary);
        _flash = MakeLabel(HorizontalAlignment.Center, 26, Palette.AccentRed);
        _flash.Visible = false;

        BuildOverlay();
        StartMatch();

        GetViewport().SizeChanged += Layout;
        Layout();
        Bootstrap.Instance.Audio.PlayMusic("game");
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= Layout;

    private void StartMatch()
    {
        _match = new BlockFitVersus(_diff, unchecked((int)_seed));
        _match.MatchEnded += OnMatchEnded;
        _match.PlayerHit += OnPlayerHit;
        _match.BotHit += OnBotHit;
    }

    private Label MakeLabel(HorizontalAlignment align, int size, Color color)
    {
        var l = new Label { HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        _uiHost.AddChild(l);
        return l;
    }

    private void Layout()
    {
        var safe = Bootstrap.Instance.SafeCanvasSize;
        _uiHost.Position = Vector2.Zero;
        _uiHost.Size = safe;
        int n = BlockFitGame.Size;

        float headerH = Mathf.Clamp(safe.Y * 0.075f, 46f, 92f);
        const float backSize = 52f;
        float pad = Mathf.Max(12f, safe.X * 0.035f);
        _back.Size = new Vector2(backSize, backSize);
        _back.Position = new Vector2(pad, (headerH - backSize) / 2f);
        _title.Position = new Vector2(safe.X * 0.2f, 0); _title.Size = new Vector2(safe.X * 0.6f, headerH);

        // CPU board (top, smaller) with its score label above it.
        float botLabelY = headerH + safe.Y * 0.004f;
        const float botLabelH = 24f;
        _botScore.Position = new Vector2(0, botLabelY); _botScore.Size = new Vector2(safe.X, botLabelH);
        _bCell = Mathf.Floor(Mathf.Min(safe.X * 0.5f / n, safe.Y * 0.30f / n));
        float bBoardPx = _bCell * n;
        _bOrigin = new Vector2((safe.X - bBoardPx) / 2f, botLabelY + botLabelH + 4f);
        float botBottom = _bOrigin.Y + bBoardPx;

        // Player board (bottom, larger) + tray, sized from the remaining vertical space.
        float pTop = botBottom + safe.Y * 0.03f;
        float trayReserve = safe.Y * 0.20f;
        _pScore.Position = new Vector2(0, botBottom + safe.Y * 0.004f); _pScore.Size = new Vector2(safe.X, safe.Y * 0.025f);
        pTop += safe.Y * 0.03f;
        _pCell = Mathf.Floor(Mathf.Min(safe.X * 0.92f / n, (safe.Y - pTop - trayReserve) / n));
        _pCell = Mathf.Max(_pCell, 8f);
        float pBoardPx = _pCell * n;
        _pOrigin = new Vector2((safe.X - pBoardPx) / 2f, pTop);

        float trayTop = _pOrigin.Y + pBoardPx + safe.Y * 0.02f;
        float trayH = Mathf.Max(46f, safe.Y - trayTop - safe.Y * 0.015f);
        float slotW = safe.X / 3f;
        _pTrayCell = Mathf.Floor(Mathf.Min(_pCell * 0.55f, Mathf.Min(slotW * 0.9f / 5f, trayH / 3.4f)));
        for (int i = 0; i < 3; i++) _pTraySlot[i] = new Rect2(i * slotW, trayTop, slotW, trayH);

        _flash.Position = new Vector2(0, _pOrigin.Y - safe.Y * 0.03f); _flash.Size = new Vector2(safe.X, safe.Y * 0.03f);
        if (GodotObject.IsInstanceValid(_overlay)) { _overlay.Position = Vector2.Zero; _overlay.Size = safe; }
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _shimmer += dt;
        if (!_match.IsOver) _match.Update(delta);

        _pScore.Text = Loc.T("YOU {0}", _match.PlayerGame.Score.ToString("N0"));
        _botScore.Text = Loc.T("CPU {0}", _match.BotGame.Score.ToString("N0"));

        for (int i = _bands.Count - 1; i >= 0; i--)
        {
            var b = _bands[i]; b.Age += dt;
            if (b.Age >= BandLife) _bands.RemoveAt(i); else _bands[i] = b;
        }
        for (int i = _fx.Count - 1; i >= 0; i--)
        {
            var s = _fx[i]; s.Age += dt; s.Pos += s.Vel * dt; s.Vel *= 0.92f;
            if (s.Age >= s.Life) _fx.RemoveAt(i); else _fx[i] = s;
        }
        if (_hitFlash > 0f) _hitFlash -= dt;
        if (_flashTtl > 0f)
        {
            _flashTtl -= dt;
            _flash.Modulate = new Color(1, 1, 1, Mathf.Clamp(_flashTtl / 0.9f, 0, 1));
            if (_flashTtl <= 0f) _flash.Visible = false;
        }
        QueueRedraw();
    }

    // ---- Input (player board only) -----------------------------------------

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
        if (_match.IsOver || _dragIndex != -1) return;
        for (int i = 0; i < 3; i++)
            if (_match.PlayerGame.Tray[i] is not null && _pTraySlot[i].HasPoint(pos))
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

        // Merge onto another occupied tray slot (same as solo — no attack), joining exactly
        // where the finger says.
        int mergeInto = MergeTargetSlot(pos, idx);
        if (mergeInto >= 0 && _match.PlayerGame.Tray[idx] is { } msrc)
        {
            MergeOffset(msrc, mergeInto, out int mr, out int mc);
            if (_match.PlayerMerge(idx, mergeInto, mr, mc)) Bootstrap.Instance.Audio.PlaySfx("hold");
            else Bootstrap.Instance.Audio.PlaySfx("move");
            QueueRedraw();
            return;
        }

        var piece = _match.PlayerGame.Tray[idx];
        if (piece is not null && TargetCell(piece, pos, out int gr, out int gc) && _match.PlayerGame.CanPlace(piece, gr, gc))
        {
            _match.PlayerGame.LinesClearedBy(piece, gr, gc, _pvRows, _pvCols); // capture before the clear
            int lines = _match.PlayerPlace(idx, gr, gc);
            Bootstrap.Instance.Audio.PlaySfx("lock");
            if (lines > 0)
            {
                Bootstrap.Instance.Audio.PlaySfx(lines >= 2 ? "combo" : "line_clear");
                SpawnClearFx();
            }
        }
        else
        {
            Bootstrap.Instance.Audio.PlaySfx("move");
        }
        QueueRedraw();
    }

    private int MergeTargetSlot(Vector2 pos, int dragIdx)
    {
        for (int i = 0; i < 3; i++)
            if (i != dragIdx && _match.PlayerGame.Tray[i] is not null && _pTraySlot[i].HasPoint(pos))
                return i;
        return -1;
    }

    private void MergeOffset(BlockPiece src, int dstSlot, out int rowOff, out int colOff)
    {
        var dst = _match.PlayerGame.Tray[dstSlot]!;
        var borigin = TrayPieceOrigin(dst, dstSlot);
        float fc = (_finger.X - borigin.X) / _pTrayCell - (src.Width - 1) * 0.5f;
        float fr = (_finger.Y - borigin.Y) / _pTrayCell - (src.Height - 1) * 0.5f;
        colOff = Mathf.RoundToInt(fc);
        rowOff = Mathf.RoundToInt(fr);
    }

    private bool TargetCell(BlockPiece p, Vector2 finger, out int gr, out int gc)
    {
        float lift = _pCell * 0.6f;
        var topLeft = new Vector2(finger.X - p.Width * _pCell / 2f, finger.Y - lift - p.Height * _pCell);
        gc = Mathf.RoundToInt((topLeft.X - _pOrigin.X) / _pCell);
        gr = Mathf.RoundToInt((topLeft.Y - _pOrigin.Y) / _pCell);
        gc = Mathf.Clamp(gc, 0, BlockFitGame.Size - p.Width);
        gr = Mathf.Clamp(gr, 0, BlockFitGame.Size - p.Height);
        return true;
    }

    // ---- Match callbacks ---------------------------------------------------

    private void OnPlayerHit(int cells)
    {
        _hitFlash = 0.5f;
        ShowCallout(Loc.T("INCOMING +{0}", cells), Palette.AccentRed);
        Bootstrap.Instance.Audio.PlaySfx("garbage");
    }

    private void OnBotHit(int cells) => ShowCallout(Loc.T("SENT +{0}", cells), Palette.AccentGreen);

    private void ShowCallout(string text, Color color)
    {
        _flash.Text = text;
        _flash.AddThemeColorOverride("font_color", color);
        _flash.Visible = true;
        _flash.Modulate = Colors.White;
        _flashTtl = 0.9f;
    }

    private void SpawnClearFx()
    {
        foreach (int r in _pvRows) _bands.Add(new Band { Row = true, Index = r });
        foreach (int c in _pvCols) _bands.Add(new Band { Row = false, Index = c });
        if (Motion.Reduced || _pCell <= 0) return;
        foreach (int r in _pvRows) Burst(true, r);
        foreach (int c in _pvCols) Burst(false, c);
        Bootstrap.Instance.Bg.Pulse(Palette.AccentGold, Mathf.Min(0.5f, 0.22f + (_pvRows.Count + _pvCols.Count) * 0.1f));
    }

    private void Burst(bool rowLine, int index)
    {
        for (int k = 0; k < BlockFitGame.Size; k++)
        {
            var center = rowLine
                ? _pOrigin + new Vector2((k + 0.5f) * _pCell, (index + 0.5f) * _pCell)
                : _pOrigin + new Vector2((index + 0.5f) * _pCell, (k + 0.5f) * _pCell);
            for (int s = 0; s < 2; s++)
            {
                float ang = _fxRng.RandfRange(0f, Mathf.Tau);
                float spd = _fxRng.RandfRange(60f, 220f);
                _fx.Add(new Spark
                {
                    Pos = center,
                    Vel = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * spd,
                    Life = _fxRng.RandfRange(0.35f, 0.6f),
                    Size = _fxRng.RandfRange(2.5f, 5f),
                    Col = new Color(1f, 0.95f, 0.6f),
                });
            }
        }
    }

    private void OnMatchEnded(VersusSide winner)
    {
        bool won = winner == VersusSide.Player;
        _overTitle.Text = won ? Loc.T("YOU WIN!") : Loc.T("DEFEATED");
        _overTitle.AddThemeColorOverride("font_color", won ? Palette.AccentGold : Palette.AccentRed);
        _overSub.Text = Loc.T("YOU {0}  ·  CPU {1}", _match.PlayerGame.Score, _match.BotGame.Score);
        _overlay.Visible = true;
        Bootstrap.Instance.Audio.PlaySfx(won ? "level_up" : "game_over");
    }

    // ---- Render ------------------------------------------------------------

    public override void _Draw()
    {
        if (_pCell <= 0) return;
        var glyph = Palette.EquippedGlyph;

        // CPU board (top) — view only.
        DrawBoard(_match.BotGame, _bOrigin, _bCell, glyph);

        // Player board (bottom) + red incoming-flash border.
        DrawBoard(_match.PlayerGame, _pOrigin, _pCell, glyph);
        if (_hitFlash > 0f)
        {
            float boardPx = _pCell * BlockFitGame.Size;
            float a = _hitFlash / 0.5f;
            DrawRect(new Rect2(_pOrigin - new Vector2(4, 4), new Vector2(boardPx + 8, boardPx + 8)),
                     new Color(Palette.AccentRed.R, Palette.AccentRed.G, Palette.AccentRed.B, 0.8f * a), filled: false, width: 4f);
        }

        // Player tray (skip the dragged slot, and the merge target — the preview redraws it).
        int mergeHover = _dragIndex == -1 ? -1 : MergeTargetSlot(_finger, _dragIndex);
        var trayTex = TextureFactory.Cell(Mathf.Clamp((int)_pTrayCell, 8, 128));
        for (int i = 0; i < 3; i++)
        {
            var p = _match.PlayerGame.Tray[i];
            if (p is null || i == _dragIndex || i == mergeHover) continue;
            DrawPiece(p, TrayPieceOrigin(p, i), _pTrayCell, 1f, trayTex, glyph);
        }

        var tex = TextureFactory.Cell(Mathf.Clamp((int)_pCell, 8, 128));

        // Merge preview: live fused shape in the target slot (source snaps to the finger), or
        // the board ghost for the dragged piece.
        if (mergeHover >= 0 && _match.PlayerGame.Tray[_dragIndex] is { } mdp)
        {
            var mdst = _match.PlayerGame.Tray[mergeHover]!;
            MergeOffset(mdp, mergeHover, out int mr, out int mc);
            bool okMerge = _match.PlayerGame.CanMerge(_dragIndex, mergeHover, mr, mc);
            var borigin = TrayPieceOrigin(mdst, mergeHover);
            DrawRect(_pTraySlot[mergeHover].Grow(-4f), new Color(0.55f, 0.85f, 1f, 0.4f), filled: false, width: 2f);
            DrawPiece(mdst, borigin, _pTrayCell, 0.5f, trayTex, glyph);
            var srcCol = okMerge ? new Color(0.4f, 1f, 0.6f) : Palette.AccentRed;
            foreach (var (dr, dc) in mdp.Cells)
            {
                var rect = new Rect2(borigin + new Vector2((dc + mc) * _pTrayCell, (dr + mr) * _pTrayCell) + new Vector2(1, 1), new Vector2(_pTrayCell - 2, _pTrayCell - 2));
                DrawTextureRect(trayTex, rect, false, new Color(srcCol.R, srcCol.G, srcCol.B, okMerge ? 0.95f : 0.6f));
            }
        }
        else if (mergeHover < 0 && _dragIndex != -1 && _match.PlayerGame.Tray[_dragIndex] is { } dp && TargetCell(dp, _finger, out int gr, out int gc))
        {
            bool ok = _match.PlayerGame.CanPlace(dp, gr, gc);
            var origin = _pOrigin + new Vector2(gc * _pCell, gr * _pCell);
            if (ok)
            {
                _match.PlayerGame.LinesClearedBy(dp, gr, gc, _pvRows, _pvCols);
                float boardPx = _pCell * BlockFitGame.Size;
                var glow = new Color(0.15f, 1f, 0.45f, 0.32f);
                foreach (int rr in _pvRows) DrawRect(new Rect2(_pOrigin + new Vector2(0, rr * _pCell), new Vector2(boardPx, _pCell)), glow, filled: true);
                foreach (int cc in _pvCols) DrawRect(new Rect2(_pOrigin + new Vector2(cc * _pCell, 0), new Vector2(_pCell, boardPx)), glow, filled: true);
            }
            foreach (var (drr, dcc) in dp.Cells)
            {
                var gcell = new Rect2(origin + new Vector2(dcc * _pCell, drr * _pCell) + new Vector2(2, 2), new Vector2(_pCell - 4, _pCell - 4));
                var col = ok ? Palette.ForPiece(dp.Color) : Palette.AccentRed;
                DrawRect(gcell, new Color(col.R, col.G, col.B, 0.9f), filled: false, width: 2.5f);
            }
            float lift = _pCell * 0.6f;
            DrawPiece(dp, new Vector2(_finger.X - dp.Width * _pCell / 2f, _finger.Y - lift - dp.Height * _pCell), _pCell, ok ? 1f : 0.6f, tex, glyph);
        }

        // Player clear celebration.
        float pBoard = _pCell * BlockFitGame.Size;
        foreach (var b in _bands)
        {
            float t = b.Age / BandLife;
            float aa = (1f - t) * (1f - t);
            float thick = _pCell * (1f + 0.5f * (1f - t));
            var col = new Color(1f, 1f, 1f, aa).Lerp(new Color(1f, 0.82f, 0.2f, aa), t);
            if (b.Row)
                DrawRect(new Rect2(_pOrigin.X, _pOrigin.Y + (b.Index + 0.5f) * _pCell - thick / 2f, pBoard, thick), col, filled: true);
            else
                DrawRect(new Rect2(_pOrigin.X + (b.Index + 0.5f) * _pCell - thick / 2f, _pOrigin.Y, thick, pBoard), col, filled: true);
        }
        foreach (var s in _fx)
        {
            float a = 1f - s.Age / s.Life;
            DrawCircle(s.Pos, s.Size * a, new Color(s.Col.R, s.Col.G, s.Col.B, a));
        }
    }

    private void DrawBoard(BlockFitGame g, Vector2 origin, float cell, SkinGlyph glyph)
    {
        int n = BlockFitGame.Size;
        float boardPx = cell * n;
        var mat = Palette.EquippedMaterial;
        bool reduced = Motion.Reduced;
        DrawRect(new Rect2(origin - new Vector2(6, 6), new Vector2(boardPx + 12, boardPx + 12)), new Color(0.05f, 0.06f, 0.11f, 0.85f), filled: true);
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
            {
                var cellRect = new Rect2(origin + new Vector2(c * cell, r * cell) + new Vector2(1, 1), new Vector2(cell - 2, cell - 2));
                var t = g.At(r, c);
                if (t == PieceType.Empty)
                    DrawRect(cellRect, new Color(1, 1, 1, 0.045f), filled: false, width: 1f);
                else
                    BlockRender.DrawCell(this, cellRect, cell, t, 1f, mat, glyph, _shimmer, r + c, reduced: reduced);
            }
    }

    private Vector2 TrayPieceOrigin(BlockPiece p, int slot)
    {
        var s = _pTraySlot[slot];
        float pw = p.Width * _pTrayCell, ph = p.Height * _pTrayCell;
        return s.Position + new Vector2((s.Size.X - pw) / 2f, (s.Size.Y - ph) / 2f);
    }

    private void DrawPiece(BlockPiece p, Vector2 origin, float cell, float alpha, Texture2D tex, SkinGlyph glyph)
    {
        var mat = Palette.EquippedMaterial;
        bool reduced = Motion.Reduced;
        foreach (var (dr, dc) in p.Cells)
        {
            var rect = new Rect2(origin + new Vector2(dc * cell, dr * cell) + new Vector2(1, 1), new Vector2(cell - 2, cell - 2));
            BlockRender.DrawCell(this, rect, cell, p.Color, alpha, mat, glyph, _shimmer, dr + dc, reduced: reduced);
        }
    }

    // ---- Overlay -----------------------------------------------------------

    private void BuildOverlay()
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

        _overTitle = new Label { Text = Loc.T("YOU WIN!"), HorizontalAlignment = HorizontalAlignment.Center };
        _overTitle.AddThemeFontSizeOverride("font_size", 40);
        box.AddChild(_overTitle);
        _overSub = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _overSub.AddThemeFontSizeOverride("font_size", 22);
        box.AddChild(_overSub);

        var rematch = new Button { Text = Loc.T("REMATCH"), ThemeTypeVariation = "PrimaryButton", CustomMinimumSize = new Vector2(220, 54) };
        Motion.BindButtonFeel(rematch);
        rematch.Pressed += () => RematchRequested?.Invoke();
        box.AddChild(rematch);
        var menu = new Button { Text = Loc.T("MENU"), CustomMinimumSize = new Vector2(220, 48) };
        Motion.BindButtonFeel(menu);
        menu.Pressed += () => QuitRequested?.Invoke();
        box.AddChild(menu);
    }
}
