using Godot;
using System;
using Blockfall.Core.Net;

namespace Blockfall.Net;

/// <summary>
/// Thin ENet wrapper for the 1v1 P2P match: one host, one client, one reliable
/// ordered channel carrying <see cref="NetMessage"/>s. Not a Node — whoever owns
/// the connection (lobby, then the match controller) must call <see cref="Poll"/>
/// every frame. Ownership is handed from the lobby to the controller on match
/// start, and the controller closes it on quit.
/// </summary>
public sealed class NetPeer : INetChannel
{
    public const int DefaultPort = 7777;

    private ENetConnection? _host;
    private ENetPacketPeer? _peer;

    public bool IsServer { get; private set; }
    public bool IsConnected => _peer is not null
        && _peer.GetState() == ENetPacketPeer.PeerState.Connected;

    public event Action? PeerConnected;
    public event Action? PeerDisconnected;
    public event Action<NetMessage>? MessageReceived;

    /// <summary>Open a listening host on the port. One opponent max.</summary>
    public Error Host(int port = DefaultPort)
    {
        _host = new ENetConnection();
        IsServer = true;
        return _host.CreateHostBound("*", port, maxPeers: 1);
    }

    /// <summary>Connect out to a host.</summary>
    public Error Join(string address, int port = DefaultPort)
    {
        _host = new ENetConnection();
        IsServer = false;
        var err = _host.CreateHost(maxPeers: 1);
        if (err != Error.Ok) return err;
        _peer = _host.ConnectToHost(address, port);
        return _peer is null ? Error.CantConnect : Error.Ok;
    }

    /// <summary>Pump ENet events. Call once per frame from the owning screen.</summary>
    public void Poll()
    {
        if (_host is null) return;
        while (true)
        {
            var ev = _host.Service(0);
            var type = (ENetConnection.EventType)(int)ev[0];
            switch (type)
            {
                case ENetConnection.EventType.Connect:
                    _peer = ev[1].As<ENetPacketPeer>();
                    PeerConnected?.Invoke();
                    break;

                case ENetConnection.EventType.Disconnect:
                    _peer = null;
                    PeerDisconnected?.Invoke();
                    break;

                case ENetConnection.EventType.Receive:
                {
                    var peer = ev[1].As<ENetPacketPeer>();
                    var bytes = peer.GetPacket();
                    if (NetMessage.TryDecode(bytes, out var msg))
                        MessageReceived?.Invoke(msg);
                    break; // malformed packets are dropped, never crash
                }

                default:
                    return; // None / Error: nothing more this frame
            }
        }
    }

    public void Send(NetMessage message)
        => _peer?.Send(0, message.Encode(), (int)ENetPacketPeer.FlagReliable);

    /// <summary>Tear the connection down (quit / lobby cancel).</summary>
    public void Close()
    {
        _peer?.PeerDisconnectNow();
        _peer = null;
        _host?.Destroy();
        _host = null;
    }

    /// <summary>LAN addresses worth showing the host (for the opponent to type in).</summary>
    public static string[] LocalAddresses()
    {
        var list = new System.Collections.Generic.List<string>();
        foreach (string addr in IP.GetLocalAddresses())
        {
            // IPv4, skip loopback — the addresses a friend can actually reach.
            if (!addr.Contains(':') && addr != "127.0.0.1")
                list.Add(addr);
        }
        return list.ToArray();
    }
}
