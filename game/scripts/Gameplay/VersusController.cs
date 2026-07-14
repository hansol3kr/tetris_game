using Godot;
using System;
using Blockfall.Core;
using Blockfall.Core.Localization;
using Blockfall.Theme;

namespace Blockfall.Gameplay;

/// <summary>
/// Hosts a 1v1 CPU battle. Renders two boards side by side — the player's (with
/// input + juice) and the bot's — drives the engine-agnostic <see cref="VersusMatch"/>
/// each frame, and shows incoming-garbage meters. On a knockout it raises a result
/// overlay with rematch / menu. The AI and garbage exchange live entirely in
/// Blockfall.Core; this class is pure presentation + input.
/// </summary>
public partial class VersusController : Node2D
{
    public event Action? RematchRequested;
    public event Action? QuitRequested;

    private readonly BotDifficulty _difficulty;
    private readonly ulong _seed;

    private VersusMatch _match = null!;
    private InputController _input = null!;
    private BoardView _playerView = null!;
    private BoardView _botView = null!;
    private JuiceLayer _juice = null!;
    private Control _uiHost = null!;

    private Label _playerScore = null!, _botScore = null!;
    private ColorRect _playerMeter = null!, _botMeter = null!;
    private Control _overlay = null!;
    private bool _paused;
    private bool _resolved;

    public VersusController(BotDifficulty difficulty, ulong seed)
    {
        _difficulty = difficulty;
        _seed = seed;
    }

    public override void _Ready()
    {
        _match = new VersusMatch(_difficulty, _seed);
        _input = new InputController(_match.PlayerGame.Config);

        _playerView = new BoardView();
        _playerView.Bind(_match.PlayerGame);
        AddChild(_playerView);

        _botView = new BoardView();
        _botView.Bind(_match.BotGame);
        AddChild(_botView);

        _juice = new JuiceLayer();
        AddChild(_juice);
        _juice.Configure(_playerView, Bootstrap.Instance.Save.Settings.JuiceIntensity);

        // Viewport-sized host for anchor-based Controls (win/pause overlay, touch
        // controls). A Control under this Node2D otherwise gets a 0×0 rect and its
        // FullRect anchors collapse — the overlay scrim + centered card were invisible.
        // Same fix as Bootstrap.ScreenHost / GameController._uiHost.
        _uiHost = new Control { Name = "UiHost", MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_uiHost);
        _uiHost.Position = Vector2.Zero;
        _uiHost.Size = GetViewport().GetVisibleRect().Size;

        BuildHud();
        BuildOverlay();
        WireEvents();

        LayoutBoards();
        GetViewport().SizeChanged += LayoutBoards;

        if (TouchControls.ShouldShow())
            _uiHost.AddChild(BuildTouchControls());

        _match.Start();
        Bootstrap.Instance.Audio.PlayMusic("game");
    }

    // The root viewport outlives this controller; C# signal subscriptions are not
    // auto-disconnected on free — rematch loops would leak one handler per match.
    public override void _ExitTree() => GetViewport().SizeChanged -= LayoutBoards;

    // ---- Layout -----------------------------------------------------------
    private void LayoutBoards()
    {
        var vp = Bootstrap.Instance.SafeCanvasSize;   // safe-area rect (clears notch / home bar)
        if (GodotObject.IsInstanceValid(_uiHost)) { _uiHost.Position = Vector2.Zero; _uiHost.Size = vp; }
        float top = vp.Y * 0.16f;
        float h = vp.Y * 0.70f;
        float w = vp.X * 0.45f;

        _playerView.Layout(new Vector2(w, h), new Vector2(vp.X * 0.03f, top));
        _botView.Layout(new Vector2(w, h), new Vector2(vp.X * 0.52f, top));

        PositionLabelsAndMeters();
    }

    private void PositionLabelsAndMeters()
    {
        _playerScore.Position = new Vector2(_playerView.BoardOrigin.X, _playerView.BoardOrigin.Y - 44);
        _botScore.Position = new Vector2(_botView.BoardOrigin.X, _botView.BoardOrigin.Y - 44);
        UpdateMeters();
    }

    // ---- HUD --------------------------------------------------------------
    private void BuildHud()
    {
        _playerScore = MakeTag(Loc.T("YOU"), Palette.Accent);
        _botScore = MakeTag(Loc.T("CPU · {0}", _difficulty.Name), new Color(0.98f, 0.36f, 0.45f));
        AddChild(_playerScore);
        AddChild(_botScore);

        _playerMeter = new ColorRect { Color = new Color(Palette.AccentRed.R, Palette.AccentRed.G, Palette.AccentRed.B, 0.85f) };
        _botMeter = new ColorRect { Color = new Color(Palette.AccentRed.R, Palette.AccentRed.G, Palette.AccentRed.B, 0.85f) };
        AddChild(_playerMeter);
        AddChild(_botMeter);
    }

    private static Label MakeTag(string text, Color color)
    {
        var l = new Label { Text = text };
        l.AddThemeFontOverride("font", Fonts.UiBold);
        l.AddThemeFontSizeOverride("font_size", 21);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }

    private void UpdateMeters()
    {
        UpdateMeter(_playerMeter, _playerView, _match.PlayerIncoming, innerRight: true);
        UpdateMeter(_botMeter, _botView, _match.BotIncoming, innerRight: false);
    }

    private void UpdateMeter(ColorRect meter, BoardView view, int pending, bool innerRight)
    {
        int rows = _match.PlayerGame.Board.VisibleRows;
        float cell = view.CellSize;
        float boardH = cell * rows;
        float boardBottom = view.BoardOrigin.Y + boardH;
        float boardRight = view.BoardOrigin.X + cell * view.Columns;
        const float barW = 10f;

        float height = Mathf.Min(pending, rows) * cell;
        float x = innerRight ? boardRight + 3f : view.BoardOrigin.X - barW - 3f;
        meter.Position = new Vector2(x, boardBottom - height);
        meter.Size = new Vector2(barW, height);
        meter.Visible = pending > 0;
    }

    // ---- Event wiring -----------------------------------------------------
    private void WireEvents()
    {
        var audio = Bootstrap.Instance.Audio;
        var pg = _match.PlayerGame;

        pg.PieceLocked += ev =>
        {
            if (ev.ClearedRows.Count > 0)
            {
                _playerView.FlashRows(ev.ClearedRows);
                _juice.OnLineClear(ev.ClearedRows, ev.Result, ev.Piece.Type);
                audio.PlayLineClear(ev.Result);
            }
            else audio.PlaySfx("lock");
        };
        pg.HardDropped += dist => { if (dist > 0) { audio.PlaySfx("hard_drop"); _juice.OnHardDrop(dist); } };
        pg.PieceRotated += r => { if (r.Success) audio.PlaySfx("rotate"); };
        pg.AttackPerformed += n => { if (n > 0) _juice.OnAttack(n); };
        pg.GarbageReceived += n => { if (n > 0) { audio.PlaySfx("garbage"); _juice.OnGarbage(n); } };

        // Bot board: light visual feedback only.
        _match.BotGame.PieceLocked += ev => { if (ev.ClearedRows.Count > 0) _botView.FlashRows(ev.ClearedRows); };

        _match.MatchEnded += Resolve;
    }

    // ---- Frame loop -------------------------------------------------------
    public override void _Process(double delta)
    {
        if (_resolved) return;
        if (!_paused && !_match.IsOver)
        {
            _input.Update(delta, _match.PlayerGame);
            _match.Update(delta);
        }
        _playerScore.Text = Loc.T("YOU   {0}L", _match.PlayerGame.Scoring.LinesCleared);
        _botScore.Text = Loc.T("CPU · {0}   {1}L", _difficulty.Name, _match.BotGame.Scoring.LinesCleared);
        UpdateMeters();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_resolved) return;
        if (@event.IsActionPressed("pause_game")) { TogglePause(); return; }
        if (!_paused && !_match.IsOver)
            _input.HandleEvent(@event, _match.PlayerGame, TogglePause);
    }

    // ---- Match resolution -------------------------------------------------
    private void Resolve(VersusSide winner)
    {
        if (_resolved) return;
        _resolved = true;
        bool playerWon = winner == VersusSide.Player;
        Bootstrap.Instance.Audio.PlaySfx(playerWon ? "win" : "game_over");
        ShowOverlay(playerWon ? Loc.T("YOU WIN!") : Loc.T("DEFEATED"),
                    playerWon ? Palette.Accent : new Color(0.98f, 0.36f, 0.45f),
                    (Loc.T("REMATCH"), () => RematchRequested?.Invoke()),
                    (Loc.T("MENU"), () => QuitRequested?.Invoke()));
    }

    private void BuildOverlay()
    {
        var scrim = new ColorRect { Color = new Color(0, 0, 0, 0.72f), Visible = false };
        UiTheme.ApplyTo(scrim); // hangs off a Node2D — the theme never propagates here
        scrim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _overlay = scrim;
        _uiHost.AddChild(_overlay);
    }

    // Builds the overlay with exactly the buttons passed in (first one is the
    // primary action). Buttons are added to the new box BEFORE it is parented
    // (so no fragile post-hoc child search), and stale cards are queue-freed —
    // safe even when called from a button's own signal.
    private void ShowOverlay(string text, Color color, params (string label, Action action)[] buttons)
    {
        foreach (var c in _overlay.GetChildren()) ((Node)c).QueueFree();

        // CenterContainer keeps the card truly centered as its min size settles —
        // anchor presets computed before children exist pin the top-left instead.
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var card = new PanelContainer { ThemeTypeVariation = "Card" };

        var box = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) };
        box.AddThemeConstantOverride("separation", 16);

        var title = new Label
        {
            Text = text,
            ThemeTypeVariation = "TitleLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 42);
        title.AddThemeColorOverride("font_color", color);
        box.AddChild(title);

        for (int i = 0; i < buttons.Length; i++)
            box.AddChild(MakeButton(buttons[i].label, buttons[i].action, primary: i == 0));

        card.AddChild(box);
        center.AddChild(card);
        _overlay.AddChild(center);
        _overlay.Visible = true;
        Motion.PopIn(card);
    }

    // ---- Pause ------------------------------------------------------------
    private void TogglePause()
    {
        if (_resolved) return;
        _paused = !_paused;
        if (_paused)
        {
            _match.PlayerGame.Pause();
            _match.BotGame.Pause();
            // Pause menu: RESUME + MENU only — never REMATCH (that abandons a live match).
            ShowOverlay(Loc.T("PAUSED"), Palette.Accent,
                (Loc.T("RESUME"), TogglePause),
                (Loc.T("MENU"), () => QuitRequested?.Invoke()));
        }
        else
        {
            _match.PlayerGame.Resume();
            _match.BotGame.Resume();
            _overlay.Visible = false;
        }
    }

    private static Button MakeButton(string text, Action onPressed, bool primary)
    {
        var b = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(260, 54),
            ThemeTypeVariation = primary ? "PrimaryButton" : "GhostButton",
        };
        Motion.BindButtonFeel(b);
        b.Pressed += () => onPressed();
        return b;
    }

    private TouchControls BuildTouchControls()
    {
        var g = _match.PlayerGame;
        var tc = new TouchControls();
        tc.LeftPressed += () => _input.PressLeft(g);
        tc.LeftReleased += () => _input.ReleaseHorizontal(-1);
        tc.RightPressed += () => _input.PressRight(g);
        tc.RightReleased += () => _input.ReleaseHorizontal(1);
        tc.SoftPressed += () => _input.PressSoftDrop(g);
        tc.SoftReleased += () => _input.ReleaseSoftDrop(g);
        tc.HardDrop += () => g.HardDrop();
        tc.RotateCw += () => g.RotateCw();
        tc.RotateCcw += () => g.RotateCcw();
        tc.Hold += () => g.HoldPiece();
        return tc;
    }
}
