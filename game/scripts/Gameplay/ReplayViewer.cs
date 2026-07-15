using Godot;
using System;
using Blockfall.Core;
using Blockfall.Theme;

namespace Blockfall.Gameplay;

/// <summary>
/// Plays back a <see cref="ReplayData"/> by deterministically re-simulating it with
/// a <see cref="ReplayPlayer"/> and rendering the result through the normal
/// <see cref="BoardView"/>. Supports pause and 1x/2x/4x speed, and returns via
/// keyboard (Esc), gamepad (start), or the on-screen BACK button (touch).
/// </summary>
public partial class ReplayViewer : Node2D
{
    public event Action? BackRequested;

    private readonly ReplayData _data;
    private ReplayPlayer _player = null!;
    private BoardView _view = null!;
    private Control _root = null!;
    private double _accum;
    private float _speed = 1f;
    private bool _paused;

    private Label _score = null!, _lines = null!, _progress = null!;
    private Button _playPauseBtn = null!, _speedBtn = null!;

    public ReplayViewer(ReplayData data) => _data = data;

    public override void _Ready()
    {
        _player = new ReplayPlayer(_data);

        _view = new BoardView();
        _view.Bind(_player.Game);
        AddChild(_view);
        LayoutBoard();
        GetViewport().SizeChanged += LayoutBoard;

        BuildHud();
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= LayoutBoard;

    private void LayoutBoard()
    {
        // Child of the safe-inset ScreenHost → inherits its offset (verified headless),
        // so lay out from a ZERO origin at the safe-canvas size. Re-adding the inset here
        // double-insets and pushes content past the safe area.
        var origin = Vector2.Zero;
        var vp = Bootstrap.Instance.SafeCanvasSize;
        if (GodotObject.IsInstanceValid(_root)) { _root.Position = origin; _root.Size = vp; }
        _view.Layout(new Vector2(vp.X * 0.62f, vp.Y * 0.78f), origin + new Vector2(vp.X * 0.19f, vp.Y * 0.10f));
    }

    public override void _Process(double delta)
    {
        if (!_paused && !_player.Finished)
        {
            _accum += delta * _speed;
            int guard = 0;
            while (_accum >= Sim.TickDt && !_player.Finished && guard++ < 600)
            {
                _accum -= Sim.TickDt;
                _player.StepOne();
            }
        }

        _score.Text = $"SCORE  {_player.Game.Scoring.Score:N0}";
        _lines.Text = $"LINES  {_player.Game.Scoring.LinesCleared}";
        _progress.Text = _player.Finished ? "END" : $"{_player.Progress * 100:0}%";
        _playPauseBtn.Text = _player.Finished ? "REPLAY" : (_paused ? "PLAY" : "PAUSE");
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("pause_game")) { BackRequested?.Invoke(); GetViewport().SetInputAsHandled(); }
    }

    private void BuildHud()
    {
        // Viewport-sized host: a Control parented to this Node2D gets no rect, so
        // FullRect anchors would collapse the whole replay UI to 0×0. Size it
        // explicitly (default TopLeft anchors) and keep it synced in LayoutBoard.
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        UiTheme.ApplyTo(root);
        root.Position = Vector2.Zero;
        root.Size = Bootstrap.Instance.SafeCanvasSize;   // safe from the start (see GameController)
        _root = root;
        AddChild(root);

        var title = new Label { Text = "REPLAY", ThemeTypeVariation = "TitleLabel", Position = new Vector2(24, 24) };
        title.AddThemeFontSizeOverride("font_size", 26);
        title.AddThemeColorOverride("font_color", Palette.Accent);
        root.AddChild(title);

        var stats = new VBoxContainer { Position = new Vector2(24, 64) };
        stats.AddThemeConstantOverride("separation", 4);
        _score = Stat(); _lines = Stat(); _progress = Stat();
        stats.AddChild(_score); stats.AddChild(_lines); stats.AddChild(_progress);
        root.AddChild(stats);

        // Bottom control bar: PLAY/PAUSE · speed · BACK (buttons work for touch + mouse).
        var bar = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        bar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom);
        bar.OffsetTop = -84; bar.OffsetBottom = -24;
        bar.AddThemeConstantOverride("separation", 12);

        _playPauseBtn = Btn("PAUSE", TogglePlay);
        _speedBtn = Btn("1x", CycleSpeed);
        var back = Btn("BACK", () => BackRequested?.Invoke());
        bar.AddChild(_playPauseBtn);
        bar.AddChild(_speedBtn);
        bar.AddChild(back);
        root.AddChild(bar);
    }

    private static Label Stat()
    {
        var l = new Label { ThemeTypeVariation = "DimLabel" };
        l.AddThemeFontSizeOverride("font_size", 18);
        return l;
    }

    private static Button Btn(string text, Action onPressed)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(120, 52), ThemeTypeVariation = "GhostButton" };
        Motion.BindButtonFeel(b);
        b.Pressed += () => onPressed();
        return b;
    }

    private void TogglePlay()
    {
        if (_player.Finished) { _player = new ReplayPlayer(_data); _view.Bind(_player.Game); _accum = 0; _paused = false; return; }
        _paused = !_paused;
    }

    private void CycleSpeed()
    {
        _speed = _speed switch { 1f => 2f, 2f => 4f, _ => 1f };
        _speedBtn.Text = $"{_speed:0}x";
    }
}
