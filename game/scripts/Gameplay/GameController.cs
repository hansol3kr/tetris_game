using Godot;
using System;
using Blockfall.Core;
using Blockfall.Core.Localization;
using Blockfall.Theme;

namespace Blockfall.Gameplay;

/// <summary>
/// The runtime that hosts one play session. It owns the engine-agnostic
/// <see cref="Game"/>, ticks it every frame, routes input, and reacts to engine
/// events with visuals + audio. When the run ends it raises <see cref="RunFinished"/>.
/// </summary>
public partial class GameController : Node2D
{
    public event Action<RunResults>? RunFinished;
    public event Action? QuitRequested;

    private readonly GameModeId _modeId;
    private readonly ulong _seed;
    private readonly string? _dailyKey;
    private readonly GameModifier[] _modifiers;

    private Game _game = null!;
    private FinesseTracker _finesse = null!;
    // Deterministic input path: a Godot sampler feeds a fixed-tick core processor,
    // and every tick's buttons are recorded so the run can be replayed exactly.
    private ButtonSampler _sampler = null!;
    private InputProcessor _proc = null!;
    private ReplayRecorder _recorder = null!;
    private double _tickAccum;
    private BoardView _view = null!;
    private JuiceLayer _juice = null!;
    private Control _uiHost = null!;
    private Hud _hud = null!;
    private Control _pauseOverlay = null!;
    private bool _finished;
    private bool _blind;
    private Node? _touchLayer; // gesture overlay OR classic d-pad, per settings

    // Optional ghost race: your best saved replay for this mode, run in lockstep to
    // show a live pace comparison.
    private readonly ReplayData? _ghostData;
    private ReplayPlayer? _ghost;
    private Label? _ghostLabel;

    /// <param name="dailyKey">
    /// Non-null only for the daily challenge ("yyyy-MM-dd"); routes the score to the
    /// per-date best instead of the per-mode best.
    /// </param>
    public GameController(GameModeId modeId, ulong seed, string? dailyKey = null,
        GameModifier[]? modifiers = null, ReplayData? ghost = null)
    {
        _modeId = modeId;
        _seed = seed;
        _dailyKey = dailyKey;
        _ghostData = ghost;
        _modifiers = modifiers ?? System.Array.Empty<GameModifier>();
    }

    public override void _Ready()
    {
        // Apply player handling settings (DAS/ARR/ghost) on top of the mode's config.
        var settings = Bootstrap.Instance.Save.Settings;
        var cfg = GameMode.ById(_modeId).Config
            .With(das: settings.DasSeconds, arr: settings.ArrSeconds, ghost: settings.GhostEnabled);
        if (_modifiers.Length > 0) cfg = ModifierSet.Apply(cfg, _modifiers);
        _game = Game.Create(_modeId, _seed, cfg);
        _finesse = new FinesseTracker();
        _sampler = new ButtonSampler();
        _proc = new InputProcessor(_game.Config, _finesse);
        _recorder = new ReplayRecorder(_seed, _modeId, _modifiers, _game.Config.Das, _game.Config.Arr);

        _view = new BoardView();
        _view.Bind(_game);
        _blind = System.Array.IndexOf(_modifiers, GameModifier.Invisible) >= 0;
        if (_blind) _view.SetBlind(true);
        AddChild(_view);
        LayoutBoard();
        GetViewport().SizeChanged += LayoutBoard;

        // Juice overlay draws on top of the board (added after it).
        _juice = new JuiceLayer();
        AddChild(_juice);
        _juice.Configure(_view, Bootstrap.Instance.Save.Settings.JuiceIntensity);

        // Full-screen UI (HUD, ghost label, touch controls, pause/revive scrims)
        // MUST live under a Control that actually has the viewport's rect. A Control
        // parented directly to this Node2D gets a 0×0 anchorable rect, so its
        // FullRect/edge anchors collapse — the HUD's right column rendered off-screen
        // and the pause/revive scrims were invisible (0×0). This host is sized to the
        // viewport and kept in sync in LayoutBoard — the same fix Bootstrap.ScreenHost
        // applies to menu screens. Mouse-transparent so gameplay input passes through.
        _uiHost = new Control { Name = "UiHost", MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_uiHost);
        _uiHost.Position = Vector2.Zero;
        _uiHost.Size = GetViewport().GetVisibleRect().Size;

        _hud = new Hud();
        _hud.Bind(_game);
        _hud.BindFinesse(_finesse);
        _uiHost.AddChild(_hud);

        // Ghost race: run the best replay in lockstep and show a live pace readout.
        if (_ghostData is not null)
        {
            _ghost = new ReplayPlayer(_ghostData);
            _ghostLabel = new Label { Text = Loc.T("GHOST"), HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(220, 0) };
            _ghostLabel.AddThemeFontSizeOverride("font_size", 20);
            _ghostLabel.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
            _ghostLabel.Position = new Vector2(-110, 8);
            _uiHost.AddChild(_ghostLabel);
        }

        if (TouchControls.ShouldShow())
            ShowTouchControls(true);

        BuildPauseOverlay();
        WireEngineEvents();

        _game.Start();
        Bootstrap.Instance.Audio.PlayMusic("game");
    }

    // The root viewport outlives this controller and C# signal subscriptions are
    // NOT auto-disconnected on free — without this, every finished run leaks a
    // handler and the next window resize calls into a disposed node.
    public override void _ExitTree() => GetViewport().SizeChanged -= LayoutBoard;

    private void LayoutBoard()
    {
        var vp = Bootstrap.Instance.SafeCanvasSize;   // safe-area rect (clears notch / home bar)
        if (GodotObject.IsInstanceValid(_uiHost)) { _uiHost.Position = Vector2.Zero; _uiHost.Size = vp; }
        // Phone portrait: near-full-width board (HUD moves to a top strip).
        // Desktop / landscape: roomy side columns for the HUD.
        var m = BoardView.MobilePortraitArea(vp);
        if (m is { } ma) _view.Layout(ma.area, ma.offset);
        else _view.Layout(new Vector2(vp.X * 0.62f, vp.Y * 0.80f), new Vector2(vp.X * 0.19f, vp.Y * 0.12f));
    }

    // Compare your progress to the time-aligned ghost: lines for time-attack modes
    // (race to 40 fastest), score otherwise. Positive = you're ahead.
    private void UpdateGhostPace()
    {
        if (_ghost is null || _ghostLabel is null) return;
        bool timeAttack = GameMode.IsTimeAttack(_modeId);
        long yours = timeAttack ? _game.Scoring.LinesCleared : _game.Scoring.Score;
        long theirs = timeAttack ? _ghost.Game.Scoring.LinesCleared : _ghost.Game.Scoring.Score;
        long delta = yours - theirs;
        _ghostLabel.Text = Loc.T("GHOST  {0}{1}", delta >= 0 ? "+" : "", delta.ToString("N0"));
        _ghostLabel.AddThemeColorOverride("font_color", delta >= 0 ? Palette.Accent : new Color(0.98f, 0.36f, 0.45f));
    }

    private void WireEngineEvents()
    {
        var audio = Bootstrap.Instance.Audio;

        // The InputProcessor credits inputs carried across the lock itself (via the
        // spawn counter), so we just start the finesse piece here. Also drop any
        // undrained drag steps so a fast flick's leftover motion never shoves the
        // freshly-spawned piece.
        _game.PieceSpawned += p =>
        {
            _sampler.ClearDragMove();
            _finesse.BeginPiece(p.Type, p.Origin.Col, p.State);
        };
        _game.PieceLocked += ev =>
        {
            if (ev.ClearedRows.Count > 0)
            {
                _view.FlashRows(ev.ClearedRows);
                _juice.OnLineClear(ev.ClearedRows, ev.Result, ev.Piece.Type);
                audio.PlayLineClear(ev.Result);
                if (ev.Result.PerfectClear) audio.PlaySfx("perfect_clear");
            }
            else
            {
                audio.PlaySfx("lock");
            }
            // Grade finesse on the FINAL resting piece (before the next piece spawns).
            _finesse.Finalize(ev.Piece.Origin.Col, ev.Piece.State, ev.Result.Spin != SpinType.None);
            if (_blind) _view.RevealBriefly();
        };
        _game.PieceRotated += r => { if (r.Success) { audio.PlaySfx("rotate"); _finesse.OnRotate(); } };
        _finesse.StreakMilestone += n => _juice.OnFinesseMilestone(n);
        _game.PieceMoved += _ => audio.PlaySfx("move");
        _game.HardDropped += dist => { if (dist > 0) { audio.PlaySfx("hard_drop"); _juice.OnHardDrop(dist); } };
        _game.HoldChanged += _ => audio.PlaySfx("hold");
        _game.LevelChanged += lvl => { audio.PlaySfx("level_up"); _juice.OnLevelUp(lvl); };
        _game.AttackPerformed += n => { if (n > 0) _juice.OnAttack(n); };
        _game.GarbageReceived += n => { if (n > 0) { audio.PlaySfx("garbage"); _juice.OnGarbage(n); } };
        // Game over gets punctuation: the stack dies bottom-to-top, then either a
        // second-chance offer (booster / rewarded ad) or the results.
        _game.GameOver += OnGameOver;
        _game.Completed += () => Finish(completed: true);
    }

    // ---- Second chance (revive) --------------------------------------------

    private bool _revived;
    private Control? _reviveOverlay;
    private Label? _reviveCountLabel;
    private double _reviveCountdown;

    private void OnGameOver()
    {
        _view.PlayDeathWave();
        if (CanOfferRevive())
        {
            // Let the death wave land before asking — the moment needs a beat.
            GetTree().CreateTimer(Motion.Reduced ? 0.1 : 0.8).Timeout += () =>
            {
                if (IsInstanceValid(this) && !_finished && _game.Status == GameStatus.GameOver)
                    ShowReviveOffer();
            };
        }
        else
        {
            Finish(completed: false);
        }
    }

    private bool CanOfferRevive()
    {
        if (_revived || _finished) return false;
        if (_dailyKey is not null) return false; // daily: one seed, one shot — no comebacks
        bool hasBooster = Bootstrap.Instance.Save.BoosterCount(Platform.StoreCatalog.BoosterSecondChance) > 0;
        bool hasAd = Bootstrap.Instance.Platform.SupportsAds;
        return hasBooster || hasAd;
    }

    private void ShowReviveOffer()
    {
        var save = Bootstrap.Instance.Save;
        int boosters = save.BoosterCount(Platform.StoreCatalog.BoosterSecondChance);

        var scrim = new ColorRect { Color = new Color(0, 0, 0, 0.60f) };
        UiTheme.ApplyTo(scrim);
        scrim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var card = new PanelContainer { ThemeTypeVariation = "Card" };
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(320, 0) };
        box.AddThemeConstantOverride("separation", 14);

        var title = new Label
        {
            Text = Loc.T("SECOND CHANCE?"),
            ThemeTypeVariation = "TitleLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 30);
        title.AddThemeColorOverride("font_color", Palette.AccentGold);
        box.AddChild(title);

        var blurb = new Label
        {
            Text = Loc.T("WIPE THE BOARD, KEEP YOUR RUN.\nREVIVED RUNS SET NO RECORDS."),
            ThemeTypeVariation = "DimLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        blurb.AddThemeFontSizeOverride("font_size", 14);
        box.AddChild(blurb);

        if (boosters > 0)
        {
            var use = MakeMenuButton(Loc.T("USE SECOND CHANCE ({0} LEFT)", boosters), AcceptReviveBooster, primary: true);
            box.AddChild(use);
        }
        if (Bootstrap.Instance.Platform.SupportsAds)
        {
            var ad = MakeMenuButton(Loc.T("WATCH AD TO REVIVE"), AcceptReviveAd, primary: boosters == 0);
            box.AddChild(ad);
        }
        _reviveCountLabel = new Label
        {
            ThemeTypeVariation = "DimLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var giveUp = MakeMenuButton(Loc.T("GIVE UP"), DeclineRevive, primary: false);
        box.AddChild(giveUp);
        box.AddChild(_reviveCountLabel);

        card.AddChild(box);
        center.AddChild(card);
        scrim.AddChild(center);
        _reviveOverlay = scrim;
        _reviveCountdown = 6.0;
        _uiHost.AddChild(scrim);
        Motion.PopIn(card);
    }

    private void AcceptReviveBooster()
    {
        if (!Bootstrap.Instance.Save.ConsumeBooster(Platform.StoreCatalog.BoosterSecondChance)) return;
        DoRevive();
    }

    private void AcceptReviveAd()
    {
        Bootstrap.Instance.Platform.ShowRewardedAd(ok =>
        {
            if (!IsInstanceValid(this) || _finished || _reviveOverlay is null) return;
            if (ok) DoRevive();
        });
    }

    private void DoRevive()
    {
        _reviveOverlay?.QueueFree();
        _reviveOverlay = null;
        if (!_game.Revive()) { Finish(completed: false); return; }
        _sampler.Reset(); // drop any gesture/edge queued while topped-out — the board is fresh now
        (_touchLayer as GestureBoardControls)?.CancelGesture(); // and forget any still-held finger
        _revived = true;
        _view.ResetDeathWave();
        _juice.OnRevive();
        Bootstrap.Instance.Audio.PlaySfx("level_up");
    }

    private void DeclineRevive()
    {
        _reviveOverlay?.QueueFree();
        _reviveOverlay = null;
        Finish(completed: false);
    }

    public override void _Process(double delta)
    {
        if (_finished) return;
        if (_game.Status == GameStatus.Running)
        {
            // Fixed-timestep loop: sample -> record -> process -> advance, in whole
            // 60Hz ticks. This makes play frame-rate independent AND deterministic,
            // so the recorded button stream reproduces the run exactly on replay.
            _tickAccum += delta;
            if (_tickAccum > 0.25) _tickAccum = 0.25; // avoid a spiral of death after a stall
            while (_tickAccum >= Sim.TickDt && _game.Status == GameStatus.Running)
            {
                _tickAccum -= Sim.TickDt;
                var buttons = _sampler.Sample();
                _recorder.Record(buttons);
                _proc.Step(buttons, _game);
                _game.Update(Sim.TickDt);
                _ghost?.StepOne(); // advance the ghost the same number of ticks (time-aligned)
            }
            UpdateGhostPace();
        }

        // Revive offer counts down to an auto-decline — no limbo screens.
        if (_reviveOverlay is not null)
        {
            _reviveCountdown -= delta;
            if (_reviveCountLabel is not null)
                _reviveCountLabel.Text = Loc.T("{0}", Mathf.CeilToInt((float)Mathf.Max(0, _reviveCountdown)));
            if (_reviveCountdown <= 0) DeclineRevive();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // F9 toggles the desktop mobile-layout preview live (shows/hides the touch pad).
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F9 })
        {
            MobilePreview.Enabled = !MobilePreview.Enabled;
            ShowTouchControls(TouchControls.ShouldShow());
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_finished) return;
        if (_game.Status == GameStatus.Running)
        {
            // Edge actions are latched into the sampler so they land on exactly one
            // fixed tick (never dropped or double-fired by the accumulator).
            if (@event.IsActionPressed("rotate_cw")) _sampler.LatchRotateCw();
            else if (@event.IsActionPressed("rotate_ccw")) _sampler.LatchRotateCcw();
            else if (@event.IsActionPressed("rotate_180")) _sampler.LatchRotate180();
            else if (@event.IsActionPressed("hard_drop")) _sampler.LatchHardDrop();
            else if (@event.IsActionPressed("hold_piece")) _sampler.LatchHold();
            else if (@event.IsActionPressed("pause_game")) TogglePause();
        }
        else if (@event.IsActionPressed("pause_game"))
            TogglePause();
    }

    private void Finish(bool completed)
    {
        if (_finished) return;
        _finished = true;
        Bootstrap.Instance.Audio.PlaySfx(completed ? "win" : "game_over");

        var save = Bootstrap.Instance.Save;
        bool isBest;
        if (_dailyKey is not null)
            isBest = save.SubmitDaily(_dailyKey, _game.Scoring.Score);
        else if (_modifiers.Length > 0 || _revived)
            isBest = false; // modified/revived runs are for fun — they don't set records
        else
            isBest = save.SubmitResult(_modeId, _game.Scoring.Score, _game.Elapsed, _game.Scoring.LinesCleared, completed);

        // Report to the platform leaderboard (Steam / mobile) fire-and-forget.
        // Revived runs stay OFF public leaderboards — a bought comeback must
        // never outrank a clean run.
        if (!_revived)
            Bootstrap.Instance.Platform.SubmitScore(_modeId, _game.Scoring.Score, _game.Elapsed);
        Bootstrap.Instance.Platform.ReportAchievements(_game.Stats, completed, _modeId);

        // Revived runs can't be reproduced from inputs alone (the board wipe isn't in
        // the stream), so they carry no replay.
        var replay = _revived ? null : _recorder.Build(_game);

        // Fold career stats, unlock achievements, and record a leaderboard entry.
        var unlocked = save.RecordRun(_modeId, _game.Stats, _game.Scoring.Score, _game.Elapsed,
            completed, _modifiers, _revived, _seed, replay);

        var results = new RunResults
        {
            Mode = _modeId,
            Completed = completed,
            Score = _game.Scoring.Score,
            Lines = _game.Scoring.LinesCleared,
            Level = _game.Scoring.Level,
            Time = _game.Elapsed,
            Stats = _game.Stats,
            GarbageSent = _game.TotalGarbageSent,
            Seed = _seed,
            Modifiers = _modifiers,
            Finesse = _finesse.Snapshot(),
            IsNewBest = isBest,
            Replay = replay,
            UnlockedAchievements = System.Linq.Enumerable.ToArray(unlocked),
        };
        // Completed runs cut to results quickly; game over holds long enough for
        // the death wave (~600ms) plus a breath of silence — the run needs an
        // ending, not a slam-cut to a menu. Reduced motion skips the wait.
        // The SceneTreeTimer outlives this node, so guard against firing after a
        // quit path already freed us.
        double hold = Motion.Reduced ? 0.4 : (completed ? 0.7 : 1.5);
        GetTree().CreateTimer(hold).Timeout += () =>
        {
            if (IsInstanceValid(this) && IsInsideTree()) RunFinished?.Invoke(results);
        };
    }

    // ---- Pause overlay ----------------------------------------------------
    private void TogglePause()
    {
        if (_finished) return;
        if (_game.Status == GameStatus.Running) PauseGame();
        else if (_game.Status == GameStatus.Paused)
        {
            _game.Resume();
            _pauseOverlay.Visible = false;
        }
    }

    /// <summary>Pause a live run and raise the overlay. Idempotent; never resumes.</summary>
    private void PauseGame()
    {
        // _game/_pauseOverlay aren't built until setup runs; a focus notification
        // could arrive before then (tree notifications route through _Notification too).
        if (_game is null || _finished || _game.Status != GameStatus.Running) return;
        _game.Pause();
        _pauseOverlay.Visible = true;
        Motion.PopIn(_pauseCard); // in with a pop; out instant — unpause must feel immediate
    }

    /// <summary>
    /// Auto-pause a live run when the app loses focus or is backgrounded — alt-tab
    /// or a minimize on desktop, a call / notification / home press on mobile — so
    /// no piece keeps dropping while the player is away. We deliberately never
    /// auto-RESUME: the player unpauses themselves when they come back.
    /// </summary>
    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut
            || what == NotificationWMWindowFocusOut
            || what == NotificationApplicationPaused)
            PauseGame();
    }

    private Control _pauseCard = null!;

    private void BuildPauseOverlay()
    {
        var scrim = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.72f),
            Visible = false,
        };
        UiTheme.ApplyTo(scrim); // overlays hang off a Node2D — the theme never propagates here
        scrim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _pauseOverlay = scrim;

        // CenterContainer keeps the card truly centered as its min size settles —
        // anchor presets computed before children exist pin the top-left instead.
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var card = new PanelContainer { ThemeTypeVariation = "Card" };
        center.AddChild(card);
        _pauseCard = card;

        var box = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) };
        box.AddThemeConstantOverride("separation", 18);

        var title = new Label
        {
            Text = Loc.T("PAUSED"),
            ThemeTypeVariation = "TitleLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 36);
        box.AddChild(title);

        var resume = MakeMenuButton(Loc.T("RESUME"), TogglePause, primary: true);
        var quit = MakeMenuButton(Loc.T("QUIT TO MENU"), () => QuitRequested?.Invoke(), primary: false);
        box.AddChild(resume);
        box.AddChild(quit);

        card.AddChild(box);
        scrim.AddChild(center);
        _uiHost.AddChild(_pauseOverlay);
    }

    private static Button MakeMenuButton(string text, Action onPressed, bool primary)
    {
        var b = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(260, 56),
            ThemeTypeVariation = primary ? "PrimaryButton" : "GhostButton",
        };
        Motion.BindButtonFeel(b);
        b.Pressed += () => onPressed();
        return b;
    }

    /// <summary>Show/hide the on-screen touch controls live (used by the F9 mobile-preview toggle).</summary>
    private void ShowTouchControls(bool on)
    {
        if (on && _touchLayer is null)
        {
            // Drag-to-fit gestures by default; the classic d-pad is a settings opt-out.
            _touchLayer = Bootstrap.Instance.Save.Settings.GestureControls
                ? BuildGestureControls()
                : BuildTouchControls();
            _uiHost.AddChild(_touchLayer);
        }
        else if (!on && _touchLayer is not null)
        {
            _touchLayer.QueueFree();
            _touchLayer = null;
        }
    }

    private GestureBoardControls BuildGestureControls()
    {
        var g = new GestureBoardControls(_view, _sampler,
            () => !_finished && _game.Status == GameStatus.Running);
        g.PauseRequested += TogglePause;
        return g;
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
