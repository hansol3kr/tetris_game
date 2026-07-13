using Godot;
using System;
using System.Collections.Generic;
using Blockfall.Core;
using Blockfall.Core.Localization;
using Blockfall.Theme;

namespace Blockfall.Gameplay;

/// <summary>
/// A hands-on onboarding flow: the player drives a REAL, gentle game while a step
/// list gates progress on actually performing each mechanic — move, soft drop,
/// rotate, hard drop, hold, and clearing a line. Uses the same deterministic input
/// path as normal play, so keyboard, gamepad, and touch all work; prompts adapt to
/// whichever the platform shows. Completion is saved so it only auto-offers once.
/// </summary>
public partial class TutorialController : Node2D
{
    public event Action? Finished;

    private Game _game = null!;
    private TutorialPieceGenerator _gen = null!;
    private ButtonSampler _sampler = null!;
    private InputProcessor _proc = null!;
    private BoardView _view = null!;
    private Control _root = null!;
    private double _tickAccum;

    private Label _title = null!, _hint = null!, _progress = null!;

    // Per-step action flags, reset on each step enter.
    private bool _movedLeft, _movedRight, _rotated, _hardDropped, _held, _clearedLine, _tSpun;
    private int _softTicks, _advAttempts;

    private readonly List<Step> _steps = new();
    private int _stepIndex = -1;
    private double _advanceDelay;
    private bool _stepSatisfied;

    private sealed class Step
    {
        public string Title = "";
        public string KeyHint = "";
        public string TouchHint = "";
        public Func<TutorialController, bool> IsDone = _ => true;
        public Action<TutorialController>? OnEnter;
        public Action<TutorialController>? OnLock;
    }

    public override void _Ready()
    {
        // A calm, no-fail sandbox: slow gravity, never tops out.
        var cfg = new GameConfig { BaseGravity = 2.5, MaxGravityLevel = 0, GhostEnabled = true };
        var mode = new GameMode { Id = GameModeId.Zen, Name = "Tutorial", CanTopOut = false, StartLevel = 1, Config = cfg };
        _gen = new TutorialPieceGenerator(new XorShiftRandom(20260710));
        _game = new Game(mode, _gen);
        _game.CommitPendingGarbageOnLock = true; // so the DEFEND lesson's garbage actually drops in
        _sampler = new ButtonSampler();
        _proc = new InputProcessor(_game.Config);

        _view = new BoardView();
        _view.Bind(_game);
        AddChild(_view);
        LayoutBoard();
        GetViewport().SizeChanged += LayoutBoard;

        WireEvents();
        BuildHud();
        if (TouchControls.ShouldShow())
            _root.AddChild(BuildTouchControls());

        BuildSteps();
        _game.Start();
        NextStep();
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= LayoutBoard;

    private void LayoutBoard()
    {
        var vp = GetViewport().GetVisibleRect().Size;
        if (GodotObject.IsInstanceValid(_root)) { _root.Position = Vector2.Zero; _root.Size = vp; }
        _view.Layout(new Vector2(vp.X * 0.60f, vp.Y * 0.62f), new Vector2(vp.X * 0.20f, vp.Y * 0.26f));
    }

    private void WireEvents()
    {
        var audio = Bootstrap.Instance.Audio;
        _game.PieceMoved += kind =>
        {
            if (kind == MoveKind.Left) _movedLeft = true;
            if (kind == MoveKind.Right) _movedRight = true;
            audio.PlaySfx("move");
        };
        _game.PieceRotated += r => { if (r.Success) { _rotated = true; audio.PlaySfx("rotate"); } };
        _game.HardDropped += d => { if (d > 0) { _hardDropped = true; audio.PlaySfx("hard_drop"); } };
        _game.HoldChanged += _ => { _held = true; audio.PlaySfx("hold"); };
        _game.PieceLocked += ev =>
        {
            if (ev.Result.Spin != SpinType.None) _tSpun = true;
            if (ev.ClearedRows.Count > 0) { _clearedLine = true; _view.FlashRows(ev.ClearedRows); audio.PlayLineClear(ev.Result); }
            else audio.PlaySfx("lock");
            if (_stepIndex >= 0 && _stepIndex < _steps.Count) _steps[_stepIndex].OnLock?.Invoke(this);
        };
    }

    private void BuildSteps()
    {
        bool touch = TouchControls.ShouldShow();
        _steps.Add(new Step
        {
            Title = Loc.T("MOVE  —  slide the piece left and right"),
            KeyHint = Loc.T("◀  ▶  arrow keys"), TouchHint = Loc.T("◀  ▶  buttons"),
            IsDone = t => t._movedLeft && t._movedRight,
        });
        _steps.Add(new Step
        {
            Title = Loc.T("ROTATE  —  turn the piece"),
            KeyHint = Loc.T("▲ (or Z)"), TouchHint = Loc.T("↻ / ↺"),
            IsDone = t => t._rotated,
        });
        _steps.Add(new Step
        {
            Title = Loc.T("SOFT DROP  —  hold to fall faster"),
            KeyHint = Loc.T("▼ down arrow"), TouchHint = Loc.T("▼ button"),
            IsDone = t => t._softTicks > 12,
        });
        _steps.Add(new Step
        {
            Title = Loc.T("HARD DROP  —  slam it straight down"),
            KeyHint = Loc.T("SPACE"), TouchHint = Loc.T("⤓ button"),
            IsDone = t => t._hardDropped,
        });
        _steps.Add(new Step
        {
            Title = Loc.T("HOLD  —  stash a piece for later"),
            KeyHint = Loc.T("C"), TouchHint = Loc.T("H button"),
            IsDone = t => t._held,
        });
        _steps.Add(new Step
        {
            Title = Loc.T("CLEAR A LINE  —  fill the row, drop into the gap"),
            KeyHint = Loc.T("fill the bottom row"), TouchHint = Loc.T("fill the bottom row"),
            OnEnter = t => t.SetupLineClear(),
            IsDone = t => t._clearedLine,
        });
        _steps.Add(new Step
        {
            Title = Loc.T("T-SPIN  —  rotate a T into the notch for bonus points"),
            KeyHint = Loc.T("spin the T (▲ / Z) into the slot"), TouchHint = Loc.T("spin the T (↻ / ↺) into the slot"),
            OnEnter = t => t.SetupTSpin(),
            OnLock = t => { t._advAttempts++; if (!t._tSpun) t.SetupTSpin(); }, // fresh slot + T each try
            IsDone = t => t._tSpun || t._advAttempts >= 8,                       // never soft-lock
        });
        _steps.Add(new Step
        {
            Title = Loc.T("DEFEND  —  clear a line to fight off incoming garbage"),
            KeyHint = Loc.T("fill the gap to clear a garbage row"), TouchHint = Loc.T("fill the gap to clear a garbage row"),
            OnEnter = t => t.SetupGarbage(),
            OnLock = t => t._advAttempts++,
            IsDone = t => t._clearedLine || t._advAttempts >= 8,
        });
    }

    // Pre-fill the bottom row except a single gap so the next drop can clear it.
    private void SetupLineClear()
    {
        var b = _game.Board;
        b.Clear(); // wipe debris from earlier steps so the gap column is open top-to-bottom
        int row = b.TotalRows - 1;
        for (int c = 0; c < b.Width; c++) b[row, c] = PieceType.Garbage;
        b[row, b.Width - 1] = PieceType.Empty; // leave the rightmost column open
    }

    // Build a classic T-spin-double slot (T caps cols 3-5, stem drops to col 4) with
    // a left-shoulder overhang so the T must be SPUN in, and guarantee a T is next.
    private void SetupTSpin()
    {
        var b = _game.Board;
        b.Clear();
        int r = b.TotalRows - 1;
        for (int c = 0; c < b.Width; c++) { b[r, c] = PieceType.Garbage; b[r - 1, c] = PieceType.Garbage; }
        b[r, 4] = PieceType.Empty;                                   // stem lands here (completes bottom row)
        b[r - 1, 3] = b[r - 1, 4] = b[r - 1, 5] = PieceType.Empty;   // T cap (completes the row above)
        b[r - 2, 3] = PieceType.Garbage;                             // overhang -> must be tucked/spun in
        _gen.Force(PieceType.T);
    }

    // Drop a wall of incoming garbage (shared hole column); clearing a line fights back.
    private void SetupGarbage()
    {
        _game.Board.Clear();
        _game.ReceiveGarbage(5); // commits on the next lock (CommitPendingGarbageOnLock is on)
    }

    public override void _Process(double delta)
    {
        if (_game.Status == GameStatus.Running)
        {
            _tickAccum += delta;
            if (_tickAccum > 0.25) _tickAccum = 0.25;
            while (_tickAccum >= Sim.TickDt && _game.Status == GameStatus.Running)
            {
                _tickAccum -= Sim.TickDt;
                var b = _sampler.Sample();
                if ((b & Buttons.Soft) != 0) _softTicks++;
                _proc.Step(b, _game);
                _game.Update(Sim.TickDt);
            }
        }

        // Advance when the current step's goal is met (with a short celebratory beat).
        if (_stepIndex >= 0 && _stepIndex < _steps.Count)
        {
            if (!_stepSatisfied && _steps[_stepIndex].IsDone(this))
            {
                _stepSatisfied = true;
                _advanceDelay = 0.7;
                _title.Text = Loc.T("✓  {0}", _steps[_stepIndex].Title);
                _title.AddThemeColorOverride("font_color", Palette.Accent);
                Bootstrap.Instance.Audio.PlaySfx("level_up");
            }
            if (_stepSatisfied)
            {
                _advanceDelay -= delta;
                if (_advanceDelay <= 0) NextStep();
            }
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("pause_game")) { Finished?.Invoke(); GetViewport().SetInputAsHandled(); return; }
        if (_game.Status != GameStatus.Running) return;
        if (e.IsActionPressed("rotate_cw")) _sampler.LatchRotateCw();
        else if (e.IsActionPressed("rotate_ccw")) _sampler.LatchRotateCcw();
        else if (e.IsActionPressed("rotate_180")) _sampler.LatchRotate180();
        else if (e.IsActionPressed("hard_drop")) _sampler.LatchHardDrop();
        else if (e.IsActionPressed("hold_piece")) _sampler.LatchHold();
    }

    private void NextStep()
    {
        _stepIndex++;
        _stepSatisfied = false;
        _movedLeft = _movedRight = _rotated = _hardDropped = _held = _clearedLine = _tSpun = false;
        _softTicks = 0;
        _advAttempts = 0;

        if (_stepIndex >= _steps.Count)
        {
            Bootstrap.Instance.Save.MarkTutorialDone();
            _title.Text = Loc.T("ALL SET  —  you're ready to play!");
            _title.AddThemeColorOverride("font_color", Palette.AccentGold);
            _hint.Text = "";
            _progress.Text = "";
            GetTree().CreateTimer(1.6).Timeout += () => { if (IsInstanceValid(this)) Finished?.Invoke(); };
            return;
        }

        var step = _steps[_stepIndex];
        step.OnEnter?.Invoke(this);
        _title.Text = step.Title;
        _title.AddThemeColorOverride("font_color", Palette.TextPrimary);
        _hint.Text = TouchControls.ShouldShow() ? step.TouchHint : step.KeyHint;
        _progress.Text = Loc.T("{0} / {1}", _stepIndex + 1, _steps.Count);
    }

    private void BuildHud()
    {
        // Viewport-sized host: a Control parented to this Node2D gets no rect, so
        // FullRect anchors would collapse the tutorial UI (title/hint/skip) to 0×0.
        // Size it explicitly (default TopLeft anchors) and keep it synced in LayoutBoard.
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        UiTheme.ApplyTo(root);
        root.Position = Vector2.Zero;
        root.Size = GetViewport().GetVisibleRect().Size;
        _root = root;
        AddChild(root);

        var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        box.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        box.OffsetTop = 40;
        box.AddThemeConstantOverride("separation", 6);

        _progress = new Label { HorizontalAlignment = HorizontalAlignment.Center, ThemeTypeVariation = "DimLabel" };
        _progress.AddThemeFontSizeOverride("font_size", 15);
        _title = new Label { HorizontalAlignment = HorizontalAlignment.Center, ThemeTypeVariation = "TitleLabel" };
        _title.AddThemeFontSizeOverride("font_size", 26);
        _hint = new Label { HorizontalAlignment = HorizontalAlignment.Center, ThemeTypeVariation = "DimLabel" };
        _hint.AddThemeFontSizeOverride("font_size", 18);
        _hint.AddThemeColorOverride("font_color", Palette.Accent);
        box.AddChild(_progress);
        box.AddChild(_title);
        box.AddChild(_hint);
        root.AddChild(box);

        var skip = new Button { Text = Loc.T("SKIP"), ThemeTypeVariation = "GhostButton", CustomMinimumSize = new Vector2(96, 44) };
        skip.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        skip.OffsetLeft = -116; skip.OffsetTop = 24; skip.OffsetRight = -20; skip.OffsetBottom = 68;
        Motion.BindButtonFeel(skip);
        skip.Pressed += () => { Bootstrap.Instance.Save.MarkTutorialDone(); Finished?.Invoke(); };
        root.AddChild(skip);
    }

    private TouchControls BuildTouchControls()
    {
        var tc = new TouchControls();
        tc.LeftPressed += () => _sampler.SetTouchLeft(true);
        tc.LeftReleased += () => _sampler.SetTouchLeft(false);
        tc.RightPressed += () => _sampler.SetTouchRight(true);
        tc.RightReleased += () => _sampler.SetTouchRight(false);
        tc.SoftPressed += () => _sampler.SetTouchSoft(true);
        tc.SoftReleased += () => _sampler.SetTouchSoft(false);
        tc.HardDrop += () => _sampler.LatchHardDrop();
        tc.RotateCw += () => _sampler.LatchRotateCw();
        tc.RotateCcw += () => _sampler.LatchRotateCcw();
        tc.Hold += () => _sampler.LatchHold();
        return tc;
    }
}
