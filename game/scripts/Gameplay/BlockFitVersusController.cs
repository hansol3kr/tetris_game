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
    private Label _title = null!, _botScore = null!, _pScore = null!, _flash = null!, _pFuse = null!;
    private FuseMeter _fuseMeter = null!;   // shared with solo — see FuseMeter for why it announces
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

    // 44pt on the 720×1280 design canvas (the exit strip carved off the tray band is
    // measured from the button's real minimum size in Layout).
    private const float TouchTarget = 84f;

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

        // Exit: bottom-left strip under the tray at the 44pt touch floor, not the unreachable
        // top-left corner (see BlockFitController — same layout language in both Block Fit
        // screens so "leave" is always in the same place).
        _back = MakeGlassButton("‹", () => QuitRequested?.Invoke());
        _uiHost.AddChild(_back);

        _title = MakeLabel(HorizontalAlignment.Center, 22, Palette.AccentViolet);
        _title.Text = Loc.T("VS CPU · {0}", _diff.Name.ToUpperInvariant());
        _botScore = MakeLabel(HorizontalAlignment.Center, 18, Palette.TextSecondary);
        _pScore = MakeLabel(HorizontalAlignment.Center, 20, Palette.TextPrimary);
        // Fuse budget. Same treatment as solo and for the same reasons (see BlockFitController):
        // font 22 in the bottom exit strip, right beside the tray where fusing happens, instead of
        // font 16 (≈8.3pt) parked on the score row above the board. It matters MORE here than in
        // solo — the CPU never fuses, so the budget is a one-sided constraint and the player has to
        // see what spending it costs them while the clock runs.
        _pFuse = MakeLabel(HorizontalAlignment.Left, 22, Palette.TextSecondary);
        _fuseMeter = new FuseMeter(_pFuse);
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

    /// <summary>A round glass button at the 44pt touch floor (see BlockFitController).</summary>
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
        b.AddThemeFontSizeOverride("font_size", 28);   // keeps the min size at exactly 84×84 (see BlockFitController)
        b.AddThemeColorOverride("font_color", Palette.TextPrimary);
        Motion.BindButtonFeel(b);
        b.Pressed += onPressed;
        return b;
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

        // Measured, never assumed: a Button never shrinks below its own minimum size.
        var backMin = _back.GetCombinedMinimumSize();
        float exitH = Mathf.Max(TouchTarget, backMin.Y);
        float exitBand = exitH + 12f;

        float headerH = Mathf.Clamp(safe.Y * 0.075f, 46f, 92f);
        float pad = Mathf.Max(12f, safe.X * 0.035f);
        _back.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
        _back.Size = new Vector2(Mathf.Max(TouchTarget, backMin.X), exitH);
        _back.Position = new Vector2(pad, safe.Y - exitH - 6f);
        _title.Position = new Vector2(safe.X * 0.06f, 0); _title.Size = new Vector2(safe.X * 0.88f, headerH);

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
        float pScoreY = botBottom + safe.Y * 0.004f;
        float pScoreH = safe.Y * 0.025f;
        _pScore.Position = new Vector2(0, pScoreY); _pScore.Size = new Vector2(safe.X, pScoreH);
        pTop += safe.Y * 0.03f;
        _pCell = Mathf.Floor(Mathf.Min(safe.X * 0.92f / n, (safe.Y - pTop - trayReserve) / n));
        _pCell = Mathf.Max(_pCell, 8f);
        float pBoardPx = _pCell * n;
        _pOrigin = new Vector2((safe.X - pBoardPx) / 2f, pTop);

        float trayTop = _pOrigin.Y + pBoardPx + safe.Y * 0.02f;
        // Leave the exit strip clear at the bottom: a button over a tray slot would steal the
        // grab, and the slots only ever use ~3.4 mini-cells of the band's height.
        float trayH = Mathf.Max(46f, safe.Y - trayTop - exitBand);
        float slotW = safe.X / 3f;
        _pTrayCell = Mathf.Floor(Mathf.Min(_pCell * 0.55f, Mathf.Min(slotW * 0.9f / 5f, trayH / 3.4f)));
        for (int i = 0; i < 3; i++) _pTraySlot[i] = new Rect2(i * slotW, trayTop, slotW, trayH);

        // FUSE lives in the exit strip beside the exit button, exactly as in solo. That strip is
        // the band between the tray's bottom edge (safe.Y - exitBand) and the canvas floor, so the
        // read-out covers neither board nor tray slot. Measured at 720×1280: strip y 1190→1274,
        // tray band ends 1184, player board ends 1021.9, exit button x 25→109, label starts 121.
        float fuseX = _back.Position.X + _back.Size.X + 12f;
        _pFuse.Position = new Vector2(fuseX, _back.Position.Y);
        _pFuse.Size = new Vector2(Mathf.Max(40f, safe.X - fuseX - pad), exitH);

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
        _fuseMeter.Sync(_match.PlayerGame.Merges, dt);

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

    /// <summary>Grabs only — presses take the UNhandled path so the exit button wins its own taps.</summary>
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
            case InputEventKey when e.IsActionPressed("pause_game"):
                QuitRequested?.Invoke();
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    /// <summary>Drag + release on the raw path: the finger may lift over the exit button, whose
    /// Stop filter would otherwise swallow the release and strand the piece mid-drag.</summary>
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
    /// Whole-SLOT pick-up, like solo — see <see cref="BlockFitController.Grab"/> for why grabbing
    /// is wide while merging stays precise. This board is where a shared rect hurt most: the tray
    /// cell here is 25px against solo's 33px (a smaller board plus a second one above it), so
    /// piece-sized grabbing meant hunting a 50×50px = 26pt = 4.1mm patch — 8% of the 240×136px
    /// slot it sits in — on the one screen where the clock is running and a fumbled pick-up is a
    /// lost duel. (Layout at 720×1280: _pCell 46 → _pTrayCell 25, trayH 136.)
    /// </summary>
    private void Grab(int id, Vector2 pos)
    {
        if (_match.IsOver || _dragIndex != -1 || _pTrayCell <= 0f) return;
        bool inBand = false;
        for (int i = 0; i < 3; i++)
        {
            if (!_pTraySlot[i].HasPoint(pos)) continue;
            inBand = true;                            // slots don't overlap — no other can match
            if (_match.PlayerGame.Tray[i] is null) break;
            _dragIndex = i; _touchId = id; _finger = pos;
            Bootstrap.Instance.Audio.PlayPlace(lift: true);   // pickup upstroke, same as solo
            QueueRedraw();
            return;
        }
        // Aimed at the tray, came up empty: the same "nothing happened" click a rejected drop
        // uses, so a missed grab never reads as a frozen game. Silent everywhere else.
        if (inBand) Bootstrap.Instance.Audio.PlaySfx("move");
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
            // Three outcomes, three answers — identical to solo (see BlockFitController.Release).
            switch (_match.PlayerGame.CheckMerge(idx, mergeInto, mr, mc))
            {
                case MergeVerdict.Ok:
                    // PlayerMerge can still refuse if the duel ended between the drag and the
                    // release, so the cue follows the COMMIT, not the verdict.
                    if (_match.PlayerMerge(idx, mergeInto, mr, mc)) Bootstrap.Instance.Audio.PlayFuse();
                    break;
                case MergeVerdict.NoCharges:
                    Bootstrap.Instance.Audio.PlayFuseDenied();
                    _fuseMeter.Alert();
                    break;
                default:
                    Bootstrap.Instance.Audio.PlaySfx("move");
                    break;
            }
            QueueRedraw();
            return;
        }

        var piece = _match.PlayerGame.Tray[idx];
        if (piece is not null && TargetCell(piece, pos, out int gr, out int gc) && _match.PlayerGame.CanPlace(piece, gr, gc))
        {
            _match.PlayerGame.LinesClearedBy(piece, gr, gc, _pvRows, _pvCols); // capture before the clear
            int lines = _match.PlayerPlace(idx, gr, gc);
            // The player's chosen sound pack, transposed by the equipped skin's material — all of
            // it inside PlayPlace, so solo and the duel can never sound different (and so the packs
            // are audible in the GAME, not only in the settings preview).
            Bootstrap.Instance.Audio.PlayPlace();
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

    /// <summary>Reach around a tray piece in the MERGE hit test, as a fraction of a tray cell.
    /// Kept identical to BlockFitController.MergeReach — the two screens are the same control and
    /// must not feel different; see that constant for the real pt/mm measurements (including the
    /// honest note that this target sits under the platform minimum, and why) and for why
    /// clipping to the slot is what makes a generous reach safe. Condition (3) over there — "the
    /// accepted rect is outlined AND magnified while aiming" — only became true for this board
    /// when <see cref="MergeLoupe"/> was shared into it; before that the screen with the smallest
    /// target (50×50px = 4.1mm, 59% of the 44pt floor) was also the one with no magnifier.</summary>
    private const float MergeReach = 0.5f;

    /// <summary>Merge target rect of one tray piece — the single geometry source shared by the
    /// merge hit test and the merge highlight. Pick-up uses the whole slot (see Grab).</summary>
    private static Rect2 MergeHitRect(BlockPiece p, Vector2 origin, float cell, Rect2 slot)
        => new Rect2(origin, new Vector2(p.Width * cell, p.Height * cell))
            .Grow(cell * MergeReach).Intersection(slot);

    /// <summary>The occupied tray slot whose PIECE the finger is over, or -1. Release and hover
    /// preview share it, so the green preview and the actual fuse can never disagree.</summary>
    private int MergeTargetSlot(Vector2 pos, int dragIdx)
    {
        if (_pTrayCell <= 0f) return -1;
        for (int i = 0; i < 3; i++)
        {
            if (i == dragIdx || _match.PlayerGame.Tray[i] is not { } p) continue;
            if (MergeHitRect(p, TrayPieceOrigin(p, i), _pTrayCell, _pTraySlot[i]).HasPoint(pos))
                return i;
        }
        return -1;
    }

    /// <summary>Outlines the rect that accepts a merge drop, from the same geometry the hit test
    /// uses. Dim/thin = "aimable", bright/thick = "locked on" — weight carries the state, not
    /// hue alone. <paramref name="spent"/> turns the hot outline amber: right target, no charges.
    /// Static, so no Motion.Reduced gate is owed.</summary>
    private void DrawMergeTarget(BlockPiece p, Vector2 origin, Rect2 slot, bool hot, bool spent = false)
    {
        if (_pTrayCell <= 0f) return;
        var col = hot
            ? (spent ? new Color(Palette.AccentGold.R, Palette.AccentGold.G, Palette.AccentGold.B, 0.95f)
                     : new Color(0.4f, 1f, 0.6f, 0.95f))
            : new Color(Palette.Accent.R, Palette.Accent.G, Palette.Accent.B, 0.34f);
        DrawRect(MergeHitRect(p, origin, _pTrayCell, slot), col, filled: false, width: hot ? 3f : 1.5f);
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

    /// <summary>Grid origin the dragged piece snaps to, or FALSE when the finger is not really
    /// over the board — an ambiguous release must do nothing rather than force-place the piece on
    /// the clamped edge (which, for a finger down in the tray, always meant the bottom row).
    /// Twin of BlockFitController.TargetCell; see there for the full reasoning.</summary>
    private bool TargetCell(BlockPiece p, Vector2 finger, out int gr, out int gc)
    {
        gr = 0; gc = 0;
        if (_pCell <= 0f) return false;
        float lift = _pCell * 0.6f;
        var topLeft = new Vector2(finger.X - p.Width * _pCell / 2f, finger.Y - lift - p.Height * _pCell);
        gc = Mathf.RoundToInt((topLeft.X - _pOrigin.X) / _pCell);
        gr = Mathf.RoundToInt((topLeft.Y - _pOrigin.Y) / _pCell);
        int maxC = BlockFitGame.Size - p.Width, maxR = BlockFitGame.Size - p.Height;
        bool on = gc >= 0 && gr >= 0 && gc <= maxC && gr <= maxR;
        gc = Mathf.Clamp(gc, 0, maxC);
        gr = Mathf.Clamp(gr, 0, maxR);
        return on;
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
            var porigin = TrayPieceOrigin(p, i);
            DrawPiece(p, porigin, _pTrayCell, 1f, trayTex, glyph);
            // Merge affordance while a piece is in hand: the fuse target is the piece's cells,
            // not the slot, so show exactly those cells.
            if (_dragIndex != -1) DrawMergeTarget(p, porigin, _pTraySlot[i], hot: false);
        }

        var tex = TextureFactory.Cell(Mathf.Clamp((int)_pCell, 8, 128));

        // Merge preview: live fused shape in the target slot (source snaps to the finger), or
        // the board ghost for the dragged piece.
        if (mergeHover >= 0 && _match.PlayerGame.Tray[_dragIndex] is { } mdp)
        {
            var mdst = _match.PlayerGame.Tray[mergeHover]!;
            MergeOffset(mdp, mergeHover, out int mr, out int mc);
            // Three states, not two — see BlockFitController for why "no charges" must not wear
            // the same red as "these two shapes won't join".
            var verdict = _match.PlayerGame.CheckMerge(_dragIndex, mergeHover, mr, mc);
            bool okMerge = verdict == MergeVerdict.Ok;
            bool spent = verdict == MergeVerdict.NoCharges;
            var borigin = TrayPieceOrigin(mdst, mergeHover);
            DrawPiece(mdst, borigin, _pTrayCell, 0.5f, trayTex, glyph);
            var hitRect = MergeHitRect(mdst, borigin, _pTrayCell, _pTraySlot[mergeHover]);
            DrawMergeTarget(mdst, borigin, _pTraySlot[mergeHover], hot: true, spent: spent);
            var srcCol = okMerge ? new Color(0.4f, 1f, 0.6f) : spent ? Palette.AccentGold : Palette.AccentRed;
            foreach (var (dr, dc) in mdp.Cells)
            {
                var rect = new Rect2(borigin + new Vector2((dc + mc) * _pTrayCell, (dr + mr) * _pTrayCell) + new Vector2(1, 1), new Vector2(_pTrayCell - 2, _pTrayCell - 2));
                DrawTextureRect(trayTex, rect, false, new Color(srcCol.R, srcCol.G, srcCol.B, okMerge ? 0.95f : 0.6f));
            }
            if (spent)   // shape channel, so the state never rides on hue alone
                DrawLine(hitRect.Position, hitRect.Position + hitRect.Size,
                         new Color(Palette.AccentGold.R, Palette.AccentGold.G, Palette.AccentGold.B, 0.9f), width: 3f);

            // The piece in hand still follows the finger while aiming at a fuse. It used to
            // vanish the instant the drag entered another slot (this branch simply never drew
            // it), which read as "I dropped it" on the one screen where a lost piece costs a
            // duel. Board-cell sized and lifted off the fingertip, exactly as solo draws it.
            float mlift = _pCell * 0.6f;
            DrawPiece(mdp, new Vector2(_finger.X - mdp.Width * _pCell / 2f, _finger.Y - mlift - mdp.Height * _pCell),
                      _pCell, 1f, tex, glyph);

            // Merge magnifier — the SAME panel solo draws, from the same function, so the two
            // Block Fit screens cannot drift apart. Drawn LAST so the floating piece can never
            // cover it. This board carries the smallest merge rect in the game (50×50px = 4.1mm)
            // AND a running clock, so it needs the confirmation-outside-the-thumb more than solo
            // does. See MergeLoupe for why it parks over the lower board and nothing else.
            MergeLoupe.Draw(this, mdp, mdst, mr, mc, verdict,
                            Bootstrap.Instance.SafeCanvasSize, _pOrigin.Y, _pTraySlot[0].Position.Y,
                            _pTrayCell, _shimmer);
        }
        else if (mergeHover < 0 && _dragIndex != -1 && _match.PlayerGame.Tray[_dragIndex] is { } dp)
        {
            // The board ghost is drawn ONLY when the finger really points at the board. Off the
            // board there is no ghost at all — matching the fact that a release there now does
            // nothing — but the piece itself must still follow the finger, or it would vanish
            // out of the player's hand the moment they drift off the grid.
            bool onBoard = TargetCell(dp, _finger, out int gr, out int gc);
            bool ok = onBoard && _match.PlayerGame.CanPlace(dp, gr, gc);
            if (onBoard)
            {
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

        // CenterContainer, not a Center anchor preset: the preset is computed before the
        // children exist, so it pinned the card's top-left and the card drifted off-centre.
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _overlay.AddChild(center);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 16);
        center.AddChild(box);

        _overTitle = new Label { Text = Loc.T("YOU WIN!"), HorizontalAlignment = HorizontalAlignment.Center };
        _overTitle.AddThemeFontSizeOverride("font_size", 40);
        box.AddChild(_overTitle);
        _overSub = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _overSub.AddThemeFontSizeOverride("font_size", 22);
        box.AddChild(_overSub);

        var rematch = new Button { Text = Loc.T("REMATCH"), ThemeTypeVariation = "PrimaryButton", CustomMinimumSize = new Vector2(260, TouchTarget) };
        Motion.BindButtonFeel(rematch);
        rematch.Pressed += () => RematchRequested?.Invoke();
        box.AddChild(rematch);
        var menu = new Button { Text = Loc.T("MENU"), CustomMinimumSize = new Vector2(260, TouchTarget) };
        Motion.BindButtonFeel(menu);
        menu.Pressed += () => QuitRequested?.Invoke();
        box.AddChild(menu);
    }
}
