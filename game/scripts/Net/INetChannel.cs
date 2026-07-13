using System;
using Blockfall.Core.Net;

namespace Blockfall.Net;

/// <summary>
/// A 1v1 message channel the versus controller talks through, independent of
/// transport. Two implementations back it: <see cref="NetPeer"/> (ENet P2P,
/// direct connect by IP) and <see cref="RelayChannel"/> (WebSocket via the
/// matchmaking/relay server). Same game code drives both.
/// </summary>
public interface INetChannel
{
    bool IsConnected { get; }
    bool IsServer { get; }

    event Action<NetMessage>? MessageReceived;
    event Action? PeerConnected;
    event Action? PeerDisconnected;

    void Poll();
    void Send(NetMessage message);
    void Close();
}
