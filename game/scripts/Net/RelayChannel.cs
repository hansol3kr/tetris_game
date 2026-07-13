using Godot;
using System;
using Blockfall.Core.Net;

namespace Blockfall.Net;

/// <summary>
/// An <see cref="INetChannel"/> that reaches the opponent through the matchmaking
/// server: a WebSocket to the server which pairs us and RELAYS binary game frames
/// to the other player. Works across any NAT with no port forwarding. Control
/// messages (queue / matched / opponent_left) are JSON text frames; game data are
/// binary <see cref="NetMessage"/> frames the server forwards verbatim.
/// </summary>
public sealed class RelayChannel : INetChannel
{
    private readonly WebSocketPeer _ws = new();
    private readonly string _url;
    private readonly string _playerName;
    private bool _queued, _matched, _closedFired;

    public bool IsServer { get; private set; }
    public ulong Seed { get; private set; }
    public string Rival { get; private set; } = "RIVAL";

    public bool IsConnected => _matched && _ws.GetReadyState() == WebSocketPeer.State.Open;

    public event Action<NetMessage>? MessageReceived;
    public event Action? PeerConnected;
    public event Action? PeerDisconnected;

    // Relay-specific (the lobby listens for these):
    public event Action? MatchFound;        // paired; Seed/IsServer/Rival now set
    public event Action<string>? Failed;    // couldn't connect / dropped before a match

    public RelayChannel(string url, string playerName)
    {
        _url = url;
        _playerName = string.IsNullOrWhiteSpace(playerName) ? "PLAYER" : playerName;
    }

    public Error Connect() => _ws.ConnectToUrl(_url);

    public void Poll()
    {
        _ws.Poll();
        var state = _ws.GetReadyState();
        if (state == WebSocketPeer.State.Open)
        {
            if (!_queued)
            {
                _queued = true;
                var d = new Godot.Collections.Dictionary { { "type", "queue" }, { "name", _playerName } };
                _ws.SendText(Json.Stringify(d));
            }
            while (_ws.GetAvailablePacketCount() > 0)
            {
                var pkt = _ws.GetPacket();
                if (_ws.WasStringPacket())
                {
                    bool wasMatched = _matched;
                    HandleControl(System.Text.Encoding.UTF8.GetString(pkt));
                    // 'matched' hands this channel to the match controller, which only
                    // subscribes to MessageReceived after a deferred scene swap. Stop
                    // draining now so any binary frames queued behind 'matched' stay in
                    // the peer buffer for the controller's first Poll (it reuses this same
                    // channel) instead of being dispatched to an empty invocation list.
                    if (!wasMatched && _matched) break;
                }
                else if (NetMessage.TryDecode(pkt, out var msg))
                    MessageReceived?.Invoke(msg);
            }
        }
        else if (state == WebSocketPeer.State.Closed && !_closedFired)
        {
            _closedFired = true;
            if (_matched) PeerDisconnected?.Invoke();
            else Failed?.Invoke("LOST CONNECTION TO MATCHMAKER");
        }
    }

    private void HandleControl(string jsonStr)
    {
        var parsed = Json.ParseString(jsonStr);
        if (parsed.VariantType != Variant.Type.Dictionary) return;
        var d = parsed.AsGodotDictionary();
        string type = d.ContainsKey("type") ? d["type"].AsString() : "";
        switch (type)
        {
            case "matched":
                IsServer = d.ContainsKey("isHost") && d["isHost"].AsBool();
                Seed = d.ContainsKey("seed") && ulong.TryParse(d["seed"].AsString(), out var s) ? s : 0;
                Rival = d.ContainsKey("rival") ? d["rival"].AsString() : "RIVAL";
                _matched = true;
                PeerConnected?.Invoke();
                MatchFound?.Invoke();
                break;
            case "opponent_left":
                PeerDisconnected?.Invoke();
                break;
            // "queued" — just keep waiting.
        }
    }

    public void Send(NetMessage message)
    {
        if (_matched && _ws.GetReadyState() == WebSocketPeer.State.Open)
            _ws.Send(message.Encode());
    }

    public void Close() => _ws.Close();
}
