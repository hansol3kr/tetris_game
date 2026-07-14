using Godot;
using System.Collections.Generic;
using Blockfall.Core;
using Blockfall.Theme;

namespace Blockfall.Gameplay;

/// <summary>
/// Renders the playfield. Split into two canvas items for mobile perf:
/// this node draws the STATIC content (glass panel, grid, locked stack) and
/// only redraws on state changes (lock / clear / garbage / short settle
/// animations); a child <see cref="BoardFx"/> draws the DYNAMIC content every
/// frame (active piece with motion smoothing, ghost, lock flashes, clear
/// streaks, hard-drop trails, danger vignette) — under ~10 cells of work.
/// Cells are baked once per cell-size into textures (TextureFactory.Cell) and
/// drawn as tinted quads; the glow underlay is modulated with the overbright
/// emissive color so it seeds the HDR-2D bloom. Pure view: reads the
/// <see cref="Game"/>, never mutates it.
/// </summary>
public partial class BoardView : Node2D
{
    private Game _game = null!;
    private float _cell = 32f;
    private Vector2 _boardOrigin;
    private int _visibleRows;
    private int _cols;

    // Baked per-cell-size textures.
    private Texture2D _cellTex = null!;
    private Texture2D _glowTex = null!;
    private int _bakedPx = -1;
    private StyleBoxTexture _panelSb = null!;

    private BoardFx _fx = null!;

    // INVISIBLE modifier: hide the locked stack; briefly glimpse it when a piece locks.
    private bool _blind;
    private float _reveal;

    // Row-settle: rows above a clear drop into place over ~75ms.
    private readonly Dictionary<int, int> _settleDrop = new(); // board row -> rows fallen
    private float _settleAge = 1f;
    private const float SettleDur = 0.075f;

    // Death wave (game over): rows desaturate bottom-to-top, ~30ms per row.
    private float _deathAge = -1f;
    private const float DeathRowStep = 0.03f;

    // Danger: 0 = calm, 1 = stack at the ceiling. Drives the FX red pulse.
    private float _danger;
    public float Danger => _danger;

    public void Bind(Game game)
    {
        _game = game;
        _fx = new BoardFx(this);
        AddChild(_fx);

        game.PieceSpawned += _ => _fx.OnSpawn();
        game.HardDropped += OnHardDropped;
        game.PieceLocked += OnLocked;
        game.GarbageReceived += _ => { UpdateDanger(); QueueRedraw(); };
    }

    internal Game Game => _game;
    internal bool Blind => _blind;
    internal Texture2D CellTexture => _cellTex;
    internal Texture2D GlowTexture => _glowTex;

    /// <summary>Enable "blind" play: the locked stack is hidden (INVISIBLE modifier).</summary>
    public void SetBlind(bool on) => _blind = on;

    /// <summary>Flash the hidden stack into view for a brief, fading glimpse (on each lock).</summary>
    public void RevealBriefly() { _reveal = 1f; QueueRedraw(); }

    /// <summary>Kick off the clear flash + streak on rows that just cleared (called by the controller).</summary>
    public void FlashRows(IReadOnlyList<int> rows) => _fx.OnClear(rows);

    /// <summary>Game-over choreography: the stack dies bottom-to-top.</summary>
    public void PlayDeathWave()
    {
        _deathAge = Motion.Reduced ? float.MaxValue : 0f; // reduced motion: dim instantly
        QueueRedraw();
    }

    /// <summary>Cancel the death wave (a revive brought the run back).</summary>
    public void ResetDeathWave()
    {
        _deathAge = -1f;
        QueueRedraw();
    }

    // ---- Geometry accessors (used by BoardFx / JuiceLayer to align effects) ----
    public Vector2 BoardOrigin => _boardOrigin;
    public float CellSize => _cell;
    public int Columns => _cols;

    /// <summary>Top-edge Y (in this node's local space) of a board-absolute row.</summary>
    public float VisibleRowY(int boardRow) => _boardOrigin.Y + (boardRow - _game.Board.VisibleTop) * _cell;

    internal Vector2 CellPos(int boardRow, int col)
    {
        int visibleRow = boardRow - _game.Board.VisibleTop;
        return _boardOrigin + new Vector2(col * _cell, visibleRow * _cell);
    }

    internal Rect2 PanelRect()
    {
        float boardW = _cell * _cols;
        float boardH = _cell * _visibleRows;
        return new Rect2(_boardOrigin - new Vector2(10, 10), new Vector2(boardW + 20, boardH + 20));
    }

    /// <summary>
    /// On a phone held portrait the board is given almost the full width (a
    /// compact HUD strip lives along the top instead of side columns), so cells
    /// are far larger than the desktop side-column layout. Returns null on
    /// desktop or in landscape, where callers keep their own roomier layout.
    /// </summary>
    public static (Vector2 area, Vector2 offset)? MobilePortraitArea(Vector2 vp)
    {
        if (!(TouchControls.ShouldShow() && vp.Y >= vp.X)) return null;
        float top = vp.Y * 0.12f;     // compact HUD strip (stats · HOLD · NEXT)
        float bottom = vp.Y * 0.06f;  // thumb room for the drag / aux touch controls
        return (new Vector2(vp.X * 0.96f, vp.Y - top - bottom), new Vector2(vp.X * 0.02f, top));
    }

    /// <summary>Compute cell size + centering for a given available pixel area.</summary>
    public void Layout(Vector2 areaSize, Vector2 areaOffset)
    {
        _cols = _game.Board.Width;
        _visibleRows = _game.Board.VisibleRows;
        float cw = areaSize.X / _cols;
        float ch = areaSize.Y / _visibleRows;
        _cell = Mathf.Floor(Mathf.Min(cw, ch));
        float boardW = _cell * _cols;
        float boardH = _cell * _visibleRows;
        _boardOrigin = areaOffset + new Vector2((areaSize.X - boardW) / 2f, (areaSize.Y - boardH) / 2f);

        int px = Mathf.Clamp((int)_cell, 8, 128);
        if (px != _bakedPx)
        {
            _bakedPx = px;
            _cellTex = TextureFactory.Cell(px);
            _glowTex = TextureFactory.CellGlow(Mathf.Clamp((int)(px * 1.7f), 12, 192));
            _panelSb = TextureFactory.GlassStyle(12,
                new Color(0.045f, 0.055f, 0.105f, 0.85f), new Color(0.022f, 0.028f, 0.065f, 0.90f),
                Palette.GlassBorder, 1.2f, 0.10f, 0, 0);
        }
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        bool busy = false;

        if (_reveal > 0f) { _reveal = Mathf.Max(0f, _reveal - dt * 2.5f); busy = true; }
        if (_settleAge < SettleDur) { _settleAge += dt; busy = true; }
        if (_deathAge >= 0f && _deathAge < float.MaxValue)
        {
            float total = _visibleRows * DeathRowStep;
            if (_deathAge <= total + 0.1f) { _deathAge += dt; busy = true; }
        }

        if (busy) QueueRedraw(); // static layer redraws only while an animation runs
    }

    // ---- Event reactions -----------------------------------------------------

    private void OnLocked(LockEvent ev)
    {
        _fx.OnLock(ev.Piece);
        if (ev.ClearedRows.Count > 0)
        {
            // Rows above a cleared line fell by the number of cleared lines
            // below them: draw them offset upward, settling down over ~75ms.
            _settleDrop.Clear();
            var board = _game.Board;
            for (int row = board.VisibleTop; row < board.TotalRows; row++)
            {
                int fell = 0;
                foreach (int c in ev.ClearedRows)
                    if (c >= row) fell++;
                if (fell > 0) _settleDrop[row] = fell;
            }
            _settleAge = Motion.Reduced ? SettleDur : 0f;
        }
        if (_blind) RevealBriefly();
        UpdateDanger();
        QueueRedraw();
    }

    private void OnHardDropped(int distance)
    {
        if (distance <= 0 || _game.Current is null) return;
        _fx.OnHardDrop(_game.Current, distance);
    }

    private void UpdateDanger()
    {
        var board = _game.Board;
        int depth = board.VisibleRows;
        for (int row = board.VisibleTop; row < board.TotalRows; row++)
        {
            bool any = false;
            for (int col = 0; col < board.Width; col++)
                if (board[row, col] != PieceType.Empty) { any = true; break; }
            if (any) { depth = row - board.VisibleTop; break; }
        }
        _danger = Mathf.Clamp(1f - depth / 5f, 0f, 1f);
    }

    // ---- Static drawing --------------------------------------------------------

    public override void _Draw()
    {
        if (_game is null || _panelSb is null) return;
        DrawPanel();
        DrawGrid();
        if (!_blind)
            DrawLockedCells();
        else if (_reveal > 0f)
            DrawLockedCells(_reveal); // fading glimpse on lock
    }

    private void DrawPanel()
    {
        _panelSb.Draw(GetCanvasItem(), PanelRect());
    }

    private void DrawGrid()
    {
        for (int c = 1; c < _cols; c++)
        {
            var x = _boardOrigin.X + c * _cell;
            DrawLine(new Vector2(x, _boardOrigin.Y),
                     new Vector2(x, _boardOrigin.Y + _cell * _visibleRows), Palette.GridLine, 1f);
        }
        for (int r = 1; r < _visibleRows; r++)
        {
            var y = _boardOrigin.Y + r * _cell;
            DrawLine(new Vector2(_boardOrigin.X, y),
                     new Vector2(_boardOrigin.X + _cell * _cols, y), Palette.GridLine, 1f);
        }
    }

    private void DrawLockedCells(float alpha = 1f)
    {
        var board = _game.Board;
        float settleT = Mathf.Clamp(_settleAge / SettleDur, 0f, 1f);
        // cubic-out settle
        float settleEase = 1f - (1f - settleT) * (1f - settleT) * (1f - settleT);

        // Death wave: rows at or below the wave line are already "dead".
        float waveRow = _deathAge < 0f ? float.MaxValue
            : _deathAge >= float.MaxValue ? -1f
            : board.TotalRows - _deathAge / DeathRowStep;

        for (int row = board.VisibleTop; row < board.TotalRows; row++)
        {
            float yOff = 0f;
            if (settleT < 1f && _settleDrop.TryGetValue(row, out int fell))
                yOff = -fell * _cell * (1f - settleEase);

            bool dead = row >= waveRow || waveRow < 0f;
            for (int col = 0; col < board.Width; col++)
            {
                var type = board[row, col];
                if (type == PieceType.Empty) continue;
                var pos = CellPos(row, col) + new Vector2(0, yOff);
                if (dead)
                {
                    // Desaturated husk — the run is over.
                    var c = Palette.ForPiece(type);
                    float luma = (c.R + c.G + c.B) / 3f * 0.45f;
                    DrawCell(pos, new Color(luma, luma, luma * 1.15f, alpha), default, 0f);
                }
                else
                {
                    DrawCell(pos, Palette.ForPiece(type), Palette.Emissive(type),
                             type == PieceType.Garbage ? 0f : 0.35f * alpha, alpha);
                }
            }
        }
    }

    /// <summary>Draws one cell: optional emissive glow underlay + tinted baked quad.</summary>
    internal void DrawCell(Vector2 pos, Color color, Color emissive, float glowAlpha, float alpha = 1f)
    {
        float gap = Mathf.Max(1f, _cell * 0.05f);
        var rect = new Rect2(pos + new Vector2(gap, gap), new Vector2(_cell - gap * 2, _cell - gap * 2));
        if (glowAlpha > 0.01f)
        {
            float grow = _cell * 0.30f;
            var glowRect = new Rect2(rect.Position - new Vector2(grow, grow), rect.Size + new Vector2(grow * 2, grow * 2));
            DrawTextureRect(_glowTex, glowRect, false,
                new Color(emissive.R, emissive.G, emissive.B, glowAlpha * alpha));
        }
        DrawTextureRect(_cellTex, rect, false, new Color(color.R, color.G, color.B, color.A * alpha));
    }
}

/// <summary>
/// The per-frame layer of the board: active piece (with 30ms residual motion
/// smoothing — logic position always snaps the same frame; only single-cell
/// steps trail, DAS/ARR teleports snap), ghost with a slow pulse, lock flashes,
/// clear flash + expanding streak, hard-drop beam trails, and the danger
/// border/vignette pulse. All effect state lives in pooled lists — no per-frame
/// allocations.
/// </summary>
public partial class BoardFx : Node2D
{
    private readonly BoardView _view;

    // Active-piece smoothing.
    private Piece? _tracked;
    private Vector2I _trackedOrigin;
    private Vector2 _offset;
    private float _spawnAge = 1f;

    // Lock flashes: cells of recently locked pieces.
    private struct LockFlash { public Vector2I[] Cells; public float Age; }
    private readonly List<LockFlash> _locks = new();

    // Clear flash + streak per row.
    private struct ClearFx { public int Row; public float Age; }
    private readonly List<ClearFx> _clears = new();

    // Hard-drop beams: one per column of the dropped piece.
    private struct Beam { public float X, TopY, BottomY, Age; public Color Color; }
    private readonly List<Beam> _beams = new();

    private float _dangerPhase;

    public BoardFx(BoardView view) => _view = view;

    public void OnSpawn()
    {
        _tracked = null;
        _offset = Vector2.Zero;
        _spawnAge = Motion.Reduced ? 1f : 0f;
    }

    public void OnLock(Piece piece)
    {
        if (Motion.Reduced) return;
        var cells = new List<Vector2I>();
        foreach (var c in piece.Cells())
            if (c.Row >= _view.Game.Board.VisibleTop)
                cells.Add(new Vector2I(c.Row, c.Col));
        if (cells.Count > 0)
            _locks.Add(new LockFlash { Cells = cells.ToArray(), Age = 0f });
    }

    public void OnClear(IReadOnlyList<int> rows)
    {
        foreach (int r in rows)
            _clears.Add(new ClearFx { Row = r, Age = 0f });
    }

    public void OnHardDrop(Piece landed, int distance)
    {
        _offset = Vector2.Zero; // land is crunchy, never smoothed
        if (Motion.Reduced) return;
        var color = Palette.ForPiece(landed.Type);
        // Topmost cell per column → beam from (top - distance) down to it.
        var top = new Dictionary<int, int>();
        foreach (var c in landed.Cells())
            if (!top.TryGetValue(c.Col, out int row) || c.Row < row)
                top[c.Col] = c.Row;
        foreach (var (col, row) in top)
        {
            float x = _view.CellPos(row, col).X + _view.CellSize * 0.5f;
            float bottom = _view.CellPos(row, col).Y + _view.CellSize * 0.4f;
            float topY = Mathf.Max(_view.BoardOrigin.Y, bottom - distance * _view.CellSize);
            _beams.Add(new Beam { X = x, TopY = topY, BottomY = bottom, Age = 0f, Color = color });
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        for (int i = _locks.Count - 1; i >= 0; i--)
        {
            var f = _locks[i]; f.Age += dt;
            if (f.Age >= 0.11f) _locks.RemoveAt(i); else _locks[i] = f;
        }
        for (int i = _clears.Count - 1; i >= 0; i--)
        {
            var f = _clears[i]; f.Age += dt;
            if (f.Age >= 0.25f) _clears.RemoveAt(i); else _clears[i] = f;
        }
        for (int i = _beams.Count - 1; i >= 0; i--)
        {
            var b = _beams[i]; b.Age += dt;
            if (b.Age >= 0.22f) _beams.RemoveAt(i); else _beams[i] = b;
        }

        if (_spawnAge < 1f) _spawnAge = Mathf.Min(1f, _spawnAge + dt / 0.08f);
        _dangerPhase += dt;

        TrackPieceMotion(dt);
        QueueRedraw();
    }

    private void TrackPieceMotion(float dt)
    {
        var cur = _view.Game.Current;
        if (cur is null) { _tracked = null; _offset = Vector2.Zero; return; }

        var origin = new Vector2I(cur.Origin.Row, cur.Origin.Col);
        if (!ReferenceEquals(cur, _tracked))
        {
            _tracked = cur;
            _trackedOrigin = origin;
        }
        else if (origin != _trackedOrigin)
        {
            var stepCells = origin - _trackedOrigin;
            _trackedOrigin = origin;
            // Only single-cell horizontal steps get the residual trail; anything
            // faster (ARR teleport, gravity chains, drops) snaps — soft movement
            // in a skill game reads as input lag.
            if (Mathf.Abs(stepCells.Y) == 1 && stepCells.X == 0 && !Motion.Reduced)
                _offset.X += -stepCells.Y * _view.CellSize; // origin.Y is the column
            else
                _offset = Vector2.Zero;
        }

        // Exponential decay, ~30ms time constant: catches up within two frames.
        float k = Mathf.Exp(-dt / 0.03f);
        _offset *= k;
        if (_offset.LengthSquared() < 0.25f) _offset = Vector2.Zero;
    }

    public override void _Draw()
    {
        var game = _view.Game;
        if (game is null) return;

        // Ghost leaks stack height, so it stays hidden while blind.
        var ghost = _view.Blind ? null : game.GhostPiece();
        if (ghost is not null && game.Current is not null)
        {
            // Landing preview: bright enough to clearly show WHERE the piece drops
            // (so you can line it up / slot it into a gap), but still pulsing so it
            // reads as a projection, not a solid block.
            float pulse = Motion.Reduced ? 0.34f : 0.28f + 0.09f * (0.5f + 0.5f * Mathf.Sin(_dangerPhase * 5.2f));
            var color = Palette.ForPiece(ghost.Type);
            float gap = Mathf.Max(1f, _view.CellSize * 0.08f);
            foreach (var c in ghost.Cells())
                if (c.Row >= game.Board.VisibleTop)
                {
                    var pos = _view.CellPos(c.Row, c.Col);
                    var rect = new Rect2(pos + new Vector2(gap, gap),
                                         new Vector2(_view.CellSize - gap * 2, _view.CellSize - gap * 2));
                    DrawRect(rect, new Color(color.R, color.G, color.B, pulse * 0.55f), filled: true);
                    DrawRect(rect, new Color(color.R, color.G, color.B, Mathf.Min(1f, pulse + 0.45f)), filled: false, width: 2.5f);
                }
        }

        DrawBeams();
        DrawCurrentPiece();
        DrawLockFlashes();
        DrawClearFx();
        DrawDanger();
    }

    private void DrawCurrentPiece()
    {
        var game = _view.Game;
        if (game.Current is null) return;
        var type = game.Current.Type;
        var color = Palette.ForPiece(type);
        var emissive = Palette.Emissive(type);
        float alpha = Mathf.Lerp(0.35f, 1f, _spawnAge); // spawn fade-in (80ms, visual only)
        foreach (var c in game.Current.Cells())
            if (c.Row >= game.Board.VisibleTop)
                _viewDrawCellHere(_view.CellPos(c.Row, c.Col) + _offset, color, emissive, 0.55f, alpha);
    }

    // Draw a cell in THIS canvas item using the view's baked textures.
    private void _viewDrawCellHere(Vector2 pos, Color color, Color emissive, float glowAlpha, float alpha)
    {
        float cell = _view.CellSize;
        float gap = Mathf.Max(1f, cell * 0.05f);
        var rect = new Rect2(pos + new Vector2(gap, gap), new Vector2(cell - gap * 2, cell - gap * 2));
        if (glowAlpha > 0.01f)
        {
            float grow = cell * 0.30f;
            var glowRect = new Rect2(rect.Position - new Vector2(grow, grow), rect.Size + new Vector2(grow * 2, grow * 2));
            DrawTextureRect(_view.GlowTexture, glowRect, false, new Color(emissive.R, emissive.G, emissive.B, glowAlpha * alpha));
        }
        DrawTextureRect(_view.CellTexture, rect, false, new Color(color.R, color.G, color.B, color.A * alpha));
    }

    private void DrawLockFlashes()
    {
        float cell = _view.CellSize;
        foreach (var f in _locks)
        {
            float t = f.Age / 0.11f;
            float a = (1f - t) * 0.55f;
            foreach (var c in f.Cells)
            {
                var pos = _view.CellPos(c.X, c.Y);
                float gap = Mathf.Max(1f, cell * 0.05f);
                DrawTextureRect(_view.CellTexture,
                    new Rect2(pos + new Vector2(gap, gap), new Vector2(cell - gap * 2, cell - gap * 2)),
                    false, new Color(1.4f, 1.4f, 1.5f, a));
            }
        }
    }

    private void DrawClearFx()
    {
        var game = _view.Game;
        float cell = _view.CellSize;
        float width = cell * _view.Columns;
        foreach (var f in _clears)
        {
            if (f.Row < game.Board.VisibleTop) continue;
            float y = _view.VisibleRowY(f.Row);

            // Core flash: quick, capped at 0.65 alpha (photosensitivity budget).
            float coreT = Mathf.Clamp(f.Age / 0.09f, 0f, 1f);
            float coreA = (1f - coreT) * 0.65f;
            if (coreA > 0.01f)
                DrawRect(new Rect2(new Vector2(_view.BoardOrigin.X, y), new Vector2(width, cell)),
                         new Color(1f, 1f, 1f, coreA), filled: true);

            // Expanding streak: a thin band widening from the center, 250ms.
            float t = Mathf.Clamp(f.Age / 0.25f, 0f, 1f);
            float ease = 1f - (1f - t) * (1f - t);
            float w = width * ease;
            float streakA = (1f - t) * 0.4f;
            var center = new Vector2(_view.BoardOrigin.X + width / 2f, y + cell * 0.5f);
            DrawRect(new Rect2(center - new Vector2(w / 2f, cell * 0.10f), new Vector2(w, cell * 0.20f)),
                     new Color(1.6f, 1.7f, 1.9f, streakA), filled: true);
        }
    }

    private void DrawBeams()
    {
        float cell = _view.CellSize;
        foreach (var b in _beams)
        {
            float t = b.Age / 0.22f;
            float a = (1f - t) * 0.30f;
            float w = cell * 0.66f * (1f - t * 0.35f);
            var c = new Color(b.Color.R, b.Color.G, b.Color.B, a);
            DrawRect(new Rect2(new Vector2(b.X - w / 2f, b.TopY), new Vector2(w, b.BottomY - b.TopY)), c, filled: true);
        }
    }

    private void DrawDanger()
    {
        float danger = _view.Danger;
        if (danger <= 0.01f) return;

        // Pulse speeds up as the stack climbs; alpha ceiling 0.25 so the top
        // rows the player must read are never obscured.
        float period = Mathf.Lerp(0.9f, 0.5f, danger);
        float pulse = 0.5f + 0.5f * Mathf.Sin(_dangerPhase * Mathf.Tau / period);
        if (Motion.Reduced) pulse = 0.6f;
        float a = danger * pulse;

        var rect = _view.PanelRect();
        DrawRect(rect, new Color(Palette.AccentRed.R, Palette.AccentRed.G, Palette.AccentRed.B, 0.35f * a),
                 filled: false, width: 2f);

        // Top vignette: three fading bands.
        float bandH = _view.CellSize * 1.2f;
        for (int i = 0; i < 3; i++)
        {
            float bandA = a * 0.125f * (1f - i / 3.0f);
            DrawRect(new Rect2(new Vector2(_view.BoardOrigin.X, _view.BoardOrigin.Y + i * bandH),
                               new Vector2(_view.CellSize * _view.Columns, bandH)),
                     new Color(Palette.AccentRed.R, Palette.AccentRed.G, Palette.AccentRed.B, bandA), filled: true);
        }
    }
}
