using Godot;
using System;
using Blockfall.Core;
using Blockfall.Core.Localization;
using Blockfall.Core.Net;
using Blockfall.Gameplay;
using Blockfall.Theme;

namespace Blockfall.Net;

/// <summary>
/// Hosts one online 1v1 match. The LOCAL <see cref="Game"/> is fully
/// authoritative and runs from local input with zero added latency; the rival's
/// side is a mirror fed by their snapshots (board on every lock, piece pose at
/// ~15 Hz). Attacks travel as net-garbage messages into the local engine's
/// pending queue, so cancellation works exactly like offline versus. Both
/// players run the same seed for a fair piece race. There is NO pause online —
/// Esc asks to forfeit instead.
/// </summary>
public partial class NetVersusController : Node2D
{
    public event Action? QuitRequested;
    /// <summary>A rematch has been agreed; the router swaps in a fresh controller on the same peer.</summary>
    public event Action<ulong>? RematchStarted;

    private readonly INetChannel _net;
    private readonly bool _isHost;
    private readonly ulong _seed;
    private readonly string _rivalName;
    private readonly bool _ranked; // Quick Match duels move the ladder; direct-connect don't

    private Game _game = null!;
    private InputController _input = null!;
    private BoardView _view = null!;
    private JuiceLayer _juice = null!;
    private Control _uiHost = null!;
    private RemoteBoardView _remote = null!;

    private Label _youTag = null!, _rivalTag = null!;
    private ColorRect _incomingMeter = null!;
    private Control _overlay = null!;
    private Label _overlayStatus = null!;

    private int _rivalLines, _rivalSent;
    private bool _over;
    private bool _localRematchAsked, _remoteRematchAsked;
    private bool _handedOff; // peer ownership moved to the next controller (rematch)
    private double _poseTimer;
    private const double PoseInterval = 1.0 / 15.0;

    public NetVersusController(INetChannel net, bool isHost, ulong seed, string rivalName, bool ranked = false)
    {
        _net = net;
        _isHost = isHost;
        _seed = seed;
        _rivalName = rivalName;
        _ranked = ranked;
    }

    public override void _Ready()
    {
        // Same piece seed both sides (fair race); garbage-hole streams differ per side.
        _game = new Game(GameMode.Versus,
            new SevenBagGenerator(new XorShiftRandom(_seed)),
            new XorShiftRandom(_seed ^ (_isHost ? 0x1111_2222_3333_4444UL : 0x5555_6666_7777_8888UL)));
        _game.CommitPendingGarbageOnLock = true;
        _input = new InputController(_game.Config);

        _view = new BoardView();
        _view.Bind(_game);
        AddChild(_view);

        _juice = new JuiceLayer();
        AddChild(_juice);
        _juice.Configure(_view, Bootstrap.Instance.Save.Settings.JuiceIntensity);

        _remote = new RemoteBoardView();
        AddChild(_remote);

        // Viewport-sized host for anchor-based Controls (win/forfeit overlay, touch).
        // A Control under this Node2D gets a 0×0 rect, collapsing FullRect anchors —
        // the overlay scrim + card were invisible. Same fix as GameController._uiHost.
        _uiHost = new Control { Name = "UiHost", MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_uiHost);
        _uiHost.Position = Vector2.Zero;
        _uiHost.Size = GetViewport().GetVisibleRect().Size;

        BuildHud();
        BuildOverlay();
        WireGameEvents();
        WireNetEvents();

        LayoutBoards();
        GetViewport().SizeChanged += LayoutBoards;

        if (TouchControls.ShouldShow())
            _uiHost.AddChild(BuildTouchControls());

        _game.Start();
        Bootstrap.Instance.Audio.PlayMusic("game");
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= LayoutBoards;
        if (!_handedOff) _net.Close();
    }

    // ---- Layout -------------------------------------------------------------

    private void LayoutBoards()
    {
        var vp = GetViewport().GetVisibleRect().Size;
        if (GodotObject.IsInstanceValid(_uiHost)) { _uiHost.Position = Vector2.Zero; _uiHost.Size = vp; }
        float top = vp.Y * 0.16f;
        float h = vp.Y * 0.70f;
        float w = vp.X * 0.45f;

        _view.Layout(new Vector2(w, h), new Vector2(vp.X * 0.03f, top));
        _remote.Layout(new Vector2(w, h), new Vector2(vp.X * 0.52f, top));

        _youTag.Position = new Vector2(_view.BoardOrigin.X, _view.BoardOrigin.Y - 44);
        _rivalTag.Position = new Vector2(_remote.BoardOrigin.X, _remote.BoardOrigin.Y - 44);
        UpdateMeter();
    }

    private void BuildHud()
    {
        _youTag = Tag(Loc.T("YOU"), Palette.Accent);
        _rivalTag = Tag(_rivalName, Palette.AccentViolet);
        AddChild(_youTag);
        AddChild(_rivalTag);

        _incomingMeter = new ColorRect { Color = new Color(Palette.AccentRed.R, Palette.AccentRed.G, Palette.AccentRed.B, 0.85f) };
        AddChild(_incomingMeter);
    }

    private static Label Tag(string text, Color color)
    {
        var l = new Label { Text = text };
        l.AddThemeFontOverride("font", Fonts.UiBold);
        l.AddThemeFontSizeOverride("font_size", 21);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }

    private void UpdateMeter()
    {
        int rows = _game.Board.VisibleRows;
        float cell = _view.CellSize;
        float bottom = _view.BoardOrigin.Y + cell * rows;
        float height = Mathf.Min(_game.PendingGarbage, rows) * cell;
        _incomingMeter.Position = new Vector2(_view.BoardOrigin.X + cell * _view.Columns + 3f, bottom - height);
        _incomingMeter.Size = new Vector2(10f, height);
        _incomingMeter.Visible = _game.PendingGarbage > 0;
    }

    // ---- Wiring ----------------------------------------------------------------

    private void WireGameEvents()
    {
        var audio = Bootstrap.Instance.Audio;

        _game.PieceLocked += ev =>
        {
            if (ev.ClearedRows.Count > 0)
            {
                _view.FlashRows(ev.ClearedRows);
                _juice.OnLineClear(ev.ClearedRows, ev.Result, ev.Piece.Type);
                audio.PlayLineClear(ev.Result);
            }
            else audio.PlaySfx("lock");

            // The lock is the authoritative sync point: mirror board + scoreboard.
            _net.Send(NetMessage.BoardSnapshot(_game.Board));
            _net.Send(NetMessage.Stats(_game.Scoring.LinesCleared, _game.TotalGarbageSent));
        };
        _game.HardDropped += dist => { if (dist > 0) { audio.PlaySfx("hard_drop"); _juice.OnHardDrop(dist); } };
        _game.PieceRotated += r => { if (r.Success) audio.PlaySfx("rotate"); };
        _game.AttackPerformed += n => { if (n > 0) _juice.OnAttack(n); };
        _game.GarbageReceived += n => { if (n > 0) { audio.PlaySfx("garbage"); _juice.OnGarbage(n); } };
        _game.GarbageSentToOpponent += n => { if (n > 0) _net.Send(NetMessage.Attack(n)); };
        _game.GameOver += () =>
        {
            _net.Send(NetMessage.BoardSnapshot(_game.Board)); // rival sees the final stack
            _net.Send(NetMessage.GameOver());
            Resolve(won: false);
        };
    }

    private void WireNetEvents()
    {
        _net.MessageReceived += OnNetMessage;
        _net.PeerDisconnected += OnPeerLost;
    }

    private void OnNetMessage(NetMessage msg)
    {
        switch (msg.Type)
        {
            case NetMsgType.Board:
                _remote.ApplySnapshot(msg);
                break;
            case NetMsgType.Active:
                _remote.ApplyActive(msg);
                break;
            case NetMsgType.Attack:
                if (!_over && msg.Lines > 0) _game.ReceiveGarbage(msg.Lines);
                break;
            case NetMsgType.Stats:
                _rivalLines = msg.LinesTotal;
                _rivalSent = msg.Sent;
                break;
            case NetMsgType.GameOver:
                Resolve(won: true);
                break;
            case NetMsgType.RematchRequest:
                _remoteRematchAsked = true;
                TryStartRematch();
                UpdateOverlayStatus();
                break;
            case NetMsgType.Start:
                // Host agreed — new seed, fresh match on the same connection.
                BeginRematch(msg.Seed);
                break;
        }
    }

    private void OnPeerLost()
    {
        if (_over && _overlay.Visible) { UpdateOverlayStatus(Loc.T("OPPONENT LEFT")); return; }
        _over = true;
        ShowOverlay(Loc.T("OPPONENT LEFT"), Palette.TextSecondary, showRematch: false);
    }

    // ---- Frame loop --------------------------------------------------------------

    public override void _Process(double delta)
    {
        _net.Poll();

        if (!_over && _game.Status == GameStatus.Running)
        {
            _input.Update(delta, _game);
            _game.Update(delta);

            _poseTimer += delta;
            if (_poseTimer >= PoseInterval)
            {
                _poseTimer = 0;
                _net.Send(NetMessage.ActivePiece(_game.Current));
            }
        }

        _youTag.Text = Loc.T("YOU   {0}L · SENT {1}", _game.Scoring.LinesCleared, _game.TotalGarbageSent);
        _rivalTag.Text = Loc.T("{0}   {1}L · SENT {2}", _rivalName, _rivalLines, _rivalSent);
        UpdateMeter();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_over) return;
        if (@event.IsActionPressed("pause_game")) { ConfirmForfeit(); return; }
        if (_game.Status == GameStatus.Running)
            _input.HandleEvent(@event, _game, ConfirmForfeit);
    }

    // ---- Match end / rematch ---------------------------------------------------

    private void Resolve(bool won)
    {
        if (_over) return;
        _over = true;
        Bootstrap.Instance.Audio.PlaySfx(won ? "win" : "game_over");
        if (!won) _view.PlayDeathWave();
        ShowOverlay(won ? Loc.T("YOU WIN!") : Loc.T("DEFEATED"),
                    won ? Palette.Accent : Palette.AccentRed, showRematch: true);

        // Ranked (Quick Match) results move the ladder. We only score a clean game
        // over here — a mid-match disconnect (OnPeerLost) is NOT counted, so a flaky
        // connection can't be farmed for rating.
        if (_ranked)
        {
            int delta = Bootstrap.Instance.Save.RecordRankedResult(won);
            int rating = Bootstrap.Instance.Save.Rank.Rating;
            string tier = Loc.T(RankSystem.TierName(RankSystem.TierOf(rating)));
            string sign = delta >= 0 ? "+" : "";
            _overlayStatus.Text = Loc.T("RANK {0}  ({1}{2}) · {3}", rating, sign, delta, tier);
        }
    }

    private void RequestRematch()
    {
        _localRematchAsked = true;
        _net.Send(NetMessage.RematchRequest());
        TryStartRematch();
        UpdateOverlayStatus();
    }

    /// <summary>Host starts the rematch once BOTH sides have asked; client waits for Start.</summary>
    private void TryStartRematch()
    {
        if (!_isHost || !_localRematchAsked || !_remoteRematchAsked) return;
        ulong seed = (ulong)Time.GetTicksUsec() ^ ((ulong)GD.Randi() << 32);
        _net.Send(NetMessage.Start(seed));
        BeginRematch(seed);
    }

    private void BeginRematch(ulong seed)
    {
        if (_handedOff) return;
        _handedOff = true; // the next controller inherits the live connection
        _net.MessageReceived -= OnNetMessage;
        _net.PeerDisconnected -= OnPeerLost;
        RematchStarted?.Invoke(seed);
    }

    private void ConfirmForfeit()
    {
        if (_over) return;
        // No pause online (the rival keeps playing) — leaving forfeits.
        ShowOverlay(Loc.T("LEAVE THE MATCH?"), Palette.TextPrimary, showRematch: false, forfeit: true);
    }

    // ---- Overlay -------------------------------------------------------------------

    private void BuildOverlay()
    {
        var scrim = new ColorRect { Color = new Color(0, 0, 0, 0.72f), Visible = false };
        UiTheme.ApplyTo(scrim);
        scrim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _overlay = scrim;
        _uiHost.AddChild(_overlay);
    }

    private void ShowOverlay(string text, Color color, bool showRematch, bool forfeit = false)
    {
        foreach (var c in _overlay.GetChildren()) ((Node)c).QueueFree();

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var card = new PanelContainer { ThemeTypeVariation = "Card" };
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(320, 0) };
        box.AddThemeConstantOverride("separation", 14);

        var title = new Label
        {
            Text = text,
            ThemeTypeVariation = "TitleLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 34);
        title.AddThemeColorOverride("font_color", color);
        box.AddChild(title);

        _overlayStatus = new Label
        {
            ThemeTypeVariation = "DimLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _overlayStatus.AddThemeFontSizeOverride("font_size", 14);
        box.AddChild(_overlayStatus);

        if (forfeit)
        {
            box.AddChild(OverlayButton(Loc.T("KEEP PLAYING"), primary: true, () => _overlay.Visible = false));
            box.AddChild(OverlayButton(Loc.T("FORFEIT"), primary: false, () => QuitRequested?.Invoke()));
        }
        else
        {
            if (showRematch)
                box.AddChild(OverlayButton(Loc.T("REMATCH"), primary: true, RequestRematch));
            box.AddChild(OverlayButton(Loc.T("MENU"), primary: !showRematch, () => QuitRequested?.Invoke()));
        }

        card.AddChild(box);
        center.AddChild(card);
        _overlay.AddChild(center);
        _overlay.Visible = true;
        Motion.PopIn(card);
    }

    private void UpdateOverlayStatus(string? forced = null)
    {
        if (_overlayStatus is null || !IsInstanceValid(_overlayStatus)) return;
        _overlayStatus.Text = forced ?? (_localRematchAsked, _remoteRematchAsked) switch
        {
            (true, false) => _isHost ? Loc.T("WAITING FOR OPPONENT…") : Loc.T("WAITING FOR HOST…"),
            (false, true) => Loc.T("{0} WANTS A REMATCH", _rivalName),
            (true, true) => Loc.T("STARTING…"),
            _ => "",
        };
    }

    private static Button OverlayButton(string text, bool primary, Action onPressed)
    {
        var b = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(260, 52),
            ThemeTypeVariation = primary ? "PrimaryButton" : "GhostButton",
        };
        Motion.BindButtonFeel(b);
        b.Pressed += () => onPressed();
        return b;
    }

    private TouchControls BuildTouchControls()
    {
        var tc = new TouchControls();
        tc.LeftPressed += () => _input.PressLeft(_game);
        tc.LeftReleased += () => _input.ReleaseHorizontal(-1);
        tc.RightPressed += () => _input.PressRight(_game);
        tc.RightReleased += () => _input.ReleaseHorizontal(1);
        tc.SoftPressed += () => _input.PressSoftDrop(_game);
        tc.SoftReleased += () => _input.ReleaseSoftDrop(_game);
        tc.HardDrop += () => _game.HardDrop();
        tc.RotateCw += () => _game.RotateCw();
        tc.RotateCcw += () => _game.RotateCcw();
        tc.Hold += () => _game.HoldPiece();
        return tc;
    }
}
