using Godot;
using System;
using Blockfall.Core;
using Blockfall.Core.Net;
using Blockfall.Core.Localization;
using Blockfall.Theme;

namespace Blockfall.Net;

/// <summary>
/// Host/join screen for the online duel. Serverless P2P: the host opens port
/// 7777 and shows their LAN addresses; the joiner types the host's IP (over the
/// internet the host needs that port forwarded). Handshake: both send HELLO on
/// connect; the host then picks the seed and sends START; both sides raise
/// <see cref="MatchReady"/> and the router swaps in the match controller, which
/// inherits the live connection.
/// </summary>
public partial class NetLobbyScreen : Control
{
    public event Action? BackRequested;
    public event Action<INetChannel, bool, ulong, string, bool>? MatchReady; // (channel, isHost, seed, rivalName, ranked)

    private NetPeer? _net;
    private RelayChannel? _relay;
    private bool _isHost;
    private bool _helloReceived;
    private ulong _seed;
    private string _rivalName = "RIVAL";
    private bool _started;

    private Label _status = null!;
    private LineEdit _address = null!;
    private LineEdit _serverUrl = null!;
    private Button _hostBtn = null!, _joinBtn = null!, _findBtn = null!;

    // Stored so they can be detached from the channel at handoff — otherwise these
    // lobby closures fire against freed UI nodes when the opponent later disconnects.
    private Action<string>? _onRelayFailed;
    private Action? _onRelayLost, _onRelayMatched;
    private Action? _onNetConnected, _onNetLost;

    public override void _Ready()
    {
        UiTheme.ApplyTo(this);
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(scroll);

        var outer = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        scroll.AddChild(outer);
        outer.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });

        var col = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            CustomMinimumSize = new Vector2(460, 0),
        };
        col.AddThemeConstantOverride("separation", 12);
        outer.AddChild(col);
        outer.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });

        var titleRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        titleRow.AddThemeConstantOverride("separation", 12);
        titleRow.AddChild(new Theme.Icon(IconKind.Swords, Palette.AccentViolet, 30) { SizeFlagsVertical = SizeFlags.ShrinkCenter });
        var title = new Label { Text = Loc.T("ONLINE MATCH"), ThemeTypeVariation = "TitleLabel" };
        title.AddThemeFontSizeOverride("font_size", 34);
        title.AddThemeColorOverride("font_color", Palette.AccentViolet);
        titleRow.AddChild(title);
        col.AddChild(titleRow);

        var hint = new Label
        {
            Text = Loc.T("SERVERLESS 1V1 — SAME WIFI, OR THE HOST FORWARDS PORT 7777"),
            ThemeTypeVariation = "SectionLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        col.AddChild(hint);

        // ---- Ladder standing (Quick Match is ranked) -------------------------
        var rank = Bootstrap.Instance.Save.Rank;
        string tier = Loc.T(RankSystem.TierName(RankSystem.TierOf(rank.Rating)));
        var rankLabel = new Label
        {
            Text = rank.GamesPlayed == 0
                ? Loc.T("YOUR RANK: UNRANKED")
                : Loc.T("YOUR RANK: {0} · {1}  ({2}W {3}L)", rank.Rating, tier, rank.Wins, rank.Losses),
            ThemeTypeVariation = "DimLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        col.AddChild(rankLabel);

        // ---- HOST card -------------------------------------------------------
        var hostCard = new PanelContainer { ThemeTypeVariation = "Card" };
        var hostBox = new VBoxContainer();
        hostBox.AddThemeConstantOverride("separation", 8);
        hostBox.AddChild(SectionLabel(Loc.T("HOST A MATCH")));
        string addresses = string.Join("   ", NetPeer.LocalAddresses());
        var ipLabel = new Label
        {
            Text = addresses.Length > 0 ? Loc.T("YOUR ADDRESS: {0}", addresses) : Loc.T("YOUR ADDRESS: (NO LAN ADDRESS FOUND)"),
            ThemeTypeVariation = "DimLabel",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        ipLabel.AddThemeFontSizeOverride("font_size", 14);
        hostBox.AddChild(ipLabel);
        _hostBtn = ActionButton(Loc.T("OPEN ROOM"), "PrimaryButton");
        _hostBtn.Pressed += StartHosting;
        hostBox.AddChild(_hostBtn);
        hostCard.AddChild(hostBox);
        col.AddChild(hostCard);

        // ---- JOIN card --------------------------------------------------------
        var joinCard = new PanelContainer { ThemeTypeVariation = "Card" };
        var joinBox = new VBoxContainer();
        joinBox.AddThemeConstantOverride("separation", 8);
        joinBox.AddChild(SectionLabel(Loc.T("JOIN A MATCH")));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        _address = new LineEdit
        {
            PlaceholderText = Loc.T("HOST IP  (E.G. 192.168.0.10)"),
            CustomMinimumSize = new Vector2(0, 46),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _joinBtn = ActionButton(Loc.T("JOIN"), "PrimaryButton");
        _joinBtn.CustomMinimumSize = new Vector2(110, 46);
        _joinBtn.Pressed += StartJoining;
        row.AddChild(_address);
        row.AddChild(_joinBtn);
        joinBox.AddChild(row);
        joinCard.AddChild(joinBox);
        col.AddChild(joinCard);

        // ---- QUICK MATCH card (online matchmaking) ---------------------------
        var qmCard = new PanelContainer { ThemeTypeVariation = "Card" };
        var qmBox = new VBoxContainer();
        qmBox.AddThemeConstantOverride("separation", 8);
        qmBox.AddChild(SectionLabel(Loc.T("QUICK MATCH  —  FIND ANY OPPONENT ONLINE")));
        _serverUrl = new LineEdit
        {
            PlaceholderText = Loc.T("matchmaker url (ws://host:8080)"),
            Text = "ws://127.0.0.1:8080",
            CustomMinimumSize = new Vector2(0, 46),
        };
        qmBox.AddChild(_serverUrl);
        _findBtn = ActionButton(Loc.T("FIND MATCH"), "PrimaryButton");
        _findBtn.Pressed += StartQuickMatch;
        qmBox.AddChild(_findBtn);
        qmCard.AddChild(qmBox);
        col.AddChild(qmCard);

        _status = new Label
        {
            ThemeTypeVariation = "DimLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        col.AddChild(_status);

        var back = ActionButton(Loc.T("BACK"), "GhostButton");
        back.Pressed += () => { Cleanup(); BackRequested?.Invoke(); };
        col.AddChild(back);
    }

    public override void _ExitTree()
    {
        if (!_started) Cleanup();
    }

    // ---- Connection flow --------------------------------------------------------

    private void StartHosting()
    {
        Cleanup();
        _net = new NetPeer();
        _isHost = true;
        var err = _net.Host();
        if (err != Error.Ok)
        {
            _status.Text = Loc.T("COULD NOT OPEN PORT {0} ({1})", NetPeer.DefaultPort, err);
            _net = null;
            return;
        }
        WireNet();
        _status.Text = Loc.T("ROOM OPEN ON PORT {0} — WAITING FOR AN OPPONENT…", NetPeer.DefaultPort);
        SetBusy(true);
    }

    private void StartJoining()
    {
        string text = _address.Text.Trim();
        if (text.Length == 0) { _status.Text = Loc.T("ENTER THE HOST'S IP FIRST"); return; }

        string host = text;
        int port = NetPeer.DefaultPort;
        int colon = text.LastIndexOf(':');
        if (colon > 0 && int.TryParse(text[(colon + 1)..], out var p)) { host = text[..colon]; port = p; }

        Cleanup();
        _net = new NetPeer();
        _isHost = false;
        var err = _net.Join(host, port);
        if (err != Error.Ok)
        {
            _status.Text = Loc.T("COULD NOT CONNECT ({0})", err);
            _net = null;
            return;
        }
        WireNet();
        _status.Text = Loc.T("CONNECTING TO {0}:{1}…", host, port);
        SetBusy(true);
    }

    private void StartQuickMatch()
    {
        Cleanup();
        string url = _serverUrl.Text.Trim();
        if (url.Length == 0) { _status.Text = Loc.T("ENTER THE MATCHMAKER URL FIRST"); return; }

        string name = System.Environment.UserName is { Length: > 0 } u ? u.ToUpperInvariant() : "PLAYER";
        _relay = new RelayChannel(url, name);
        _onRelayFailed = reason => { _status.Text = reason; SetBusy(false); };
        _onRelayLost = () => { _status.Text = Loc.T("OPPONENT LEFT"); SetBusy(false); };
        _onRelayMatched = () =>
        {
            _status.Text = Loc.T("MATCH FOUND — VS {0}", _relay!.Rival);
            Launch(_relay!, _relay!.IsServer, _relay!.Seed, _relay!.Rival, ranked: true);
        };
        _relay.Failed += _onRelayFailed;
        _relay.PeerDisconnected += _onRelayLost;
        _relay.MatchFound += _onRelayMatched;

        var err = _relay.Connect();
        if (err != Error.Ok) { _status.Text = Loc.T("COULD NOT CONNECT ({0})", err); _relay = null; return; }
        _status.Text = Loc.T("SEARCHING FOR AN OPPONENT…");
        SetBusy(true);
    }

    private void WireNet()
    {
        if (_net is null) return;
        _onNetConnected = () =>
        {
            _status.Text = Loc.T("CONNECTED — SHAKING HANDS…");
            _net.Send(NetMessage.Hello(System.Environment.UserName is { Length: > 0 } u ? u.ToUpperInvariant() : "PLAYER"));
        };
        _onNetLost = () =>
        {
            _status.Text = Loc.T("CONNECTION LOST");
            SetBusy(false);
        };
        _net.PeerConnected += _onNetConnected;
        _net.PeerDisconnected += _onNetLost;
        _net.MessageReceived += OnMessage;
    }

    private void OnMessage(NetMessage msg)
    {
        switch (msg.Type)
        {
            case NetMsgType.Hello:
                if (msg.Version != NetMessage.ProtocolVersion)
                {
                    _status.Text = Loc.T("VERSION MISMATCH — BOTH PLAYERS NEED THE SAME BUILD");
                    Cleanup();
                    SetBusy(false);
                    return;
                }
                _rivalName = msg.Name.Length > 0 ? msg.Name : "RIVAL";
                _helloReceived = true;
                if (_isHost)
                {
                    // Host owns the seed. Send it, then start locally.
                    _seed = (ulong)Time.GetTicksUsec() ^ ((ulong)GD.Randi() << 32);
                    _net!.Send(NetMessage.Start(_seed));
                    Launch(_net!, _isHost, _seed, _rivalName, ranked: false);
                }
                break;

            case NetMsgType.Start:
                if (!_isHost && _helloReceived)
                {
                    _seed = msg.Seed;
                    Launch(_net!, _isHost, _seed, _rivalName, ranked: false);
                }
                break;
        }
    }

    private void Launch(INetChannel channel, bool isHost, ulong seed, string rival, bool ranked)
    {
        if (_started) return;
        _started = true; // the match controller now owns the connection

        // Detach ALL lobby handlers from the live channel so only the controller
        // listens after the swap — otherwise these closures fire on freed UI nodes.
        if (_relay is not null)
        {
            if (_onRelayFailed is not null) _relay.Failed -= _onRelayFailed;
            if (_onRelayLost is not null) _relay.PeerDisconnected -= _onRelayLost;
            if (_onRelayMatched is not null) _relay.MatchFound -= _onRelayMatched;
        }
        if (_net is not null)
        {
            if (_onNetConnected is not null) _net.PeerConnected -= _onNetConnected;
            if (_onNetLost is not null) _net.PeerDisconnected -= _onNetLost;
            _net.MessageReceived -= OnMessage;
        }
        MatchReady?.Invoke(channel, isHost, seed, rival, ranked);
    }

    public override void _Process(double delta)
    {
        _net?.Poll();
        _relay?.Poll();
    }

    private void SetBusy(bool busy)
    {
        _hostBtn.Disabled = busy;
        _joinBtn.Disabled = busy;
        _findBtn.Disabled = busy;
    }

    private void Cleanup()
    {
        _net?.Close();
        _net = null;
        _relay?.Close();
        _relay = null;
        _helloReceived = false;
    }

    // ---- Small builders ------------------------------------------------------------

    private static Label SectionLabel(string text) => new() { Text = text, ThemeTypeVariation = "SectionLabel" };

    private static Button ActionButton(string text, string variation)
    {
        var b = new Button
        {
            Text = text,
            ThemeTypeVariation = variation,
            CustomMinimumSize = new Vector2(0, 50),
        };
        Motion.BindButtonFeel(b);
        return b;
    }
}
