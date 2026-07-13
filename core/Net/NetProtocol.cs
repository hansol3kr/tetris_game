using System;
using System.IO;
using System.Text;

namespace Blockfall.Core.Net;

/// <summary>Wire message kinds for the P2P versus protocol.</summary>
public enum NetMsgType : byte
{
    Hello = 1,          // name + protocol version (both directions on connect)
    Start = 2,          // host → client: piece seed; the match begins (also rematch start)
    Board = 3,          // sender's full board snapshot (sent on every lock — authoritative)
    Active = 4,         // sender's falling piece pose (~15 Hz, display only)
    Attack = 5,         // NET garbage lines for the receiver to queue
    Stats = 6,          // sender's lines cleared + total sent (scoreboard)
    GameOver = 7,       // sender topped out — receiver wins
    RematchRequest = 8, // either side asks; host answers with Start(new seed)
}

/// <summary>
/// The P2P versus wire protocol, engine-free so it unit-tests headlessly.
/// Design: each peer runs its OWN authoritative <see cref="Game"/> (zero input
/// latency, no lockstep); the opponent view is a mirrored snapshot — the full
/// board on every lock (10×40 cells at 4 bits ≈ 200 bytes, trivial at 1-2
/// locks/sec) plus a low-rate falling-piece pose. Mirroring instead of command
/// replay makes desync structurally impossible. All messages ride a reliable
/// ordered channel.
/// </summary>
public sealed class NetMessage
{
    public const int ProtocolVersion = 1;

    public NetMsgType Type { get; init; }

    // Hello
    public string Name { get; init; } = "";
    public int Version { get; init; }

    // Start
    public ulong Seed { get; init; }

    // Board
    public byte BoardWidth { get; init; }
    public byte BoardRows { get; init; }
    public PieceType[] Cells { get; init; } = Array.Empty<PieceType>();

    // Active piece pose (display only). Visible=false means "no falling piece".
    public bool PieceVisible { get; init; }
    public PieceType Piece { get; init; }
    public short Row { get; init; }
    public sbyte Col { get; init; }
    public RotationState Rot { get; init; }

    // Attack
    public int Lines { get; init; }

    // Stats
    public int LinesTotal { get; init; }
    public int Sent { get; init; }

    // ---- Constructors ------------------------------------------------------

    public static NetMessage Hello(string name)
        => new() { Type = NetMsgType.Hello, Name = name, Version = ProtocolVersion };

    public static NetMessage Start(ulong seed)
        => new() { Type = NetMsgType.Start, Seed = seed };

    public static NetMessage BoardSnapshot(Board board)
        => new()
        {
            Type = NetMsgType.Board,
            BoardWidth = (byte)board.Width,
            BoardRows = (byte)board.TotalRows,
            Cells = board.Snapshot(),
        };

    public static NetMessage ActivePiece(Piece? piece)
        => piece is null
            ? new NetMessage { Type = NetMsgType.Active, PieceVisible = false }
            : new NetMessage
            {
                Type = NetMsgType.Active,
                PieceVisible = true,
                Piece = piece.Type,
                Row = (short)piece.Origin.Row,
                Col = (sbyte)piece.Origin.Col,
                Rot = piece.State,
            };

    public static NetMessage Attack(int lines) => new() { Type = NetMsgType.Attack, Lines = lines };

    public static NetMessage Stats(int linesTotal, int sent)
        => new() { Type = NetMsgType.Stats, LinesTotal = linesTotal, Sent = sent };

    public static NetMessage GameOver() => new() { Type = NetMsgType.GameOver };

    public static NetMessage RematchRequest() => new() { Type = NetMsgType.RematchRequest };

    // ---- Encoding ------------------------------------------------------------

    public byte[] Encode()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8);
        w.Write((byte)Type);
        switch (Type)
        {
            case NetMsgType.Hello:
                w.Write(Version);
                var nameBytes = Encoding.UTF8.GetBytes(Name);
                w.Write((byte)Math.Min(nameBytes.Length, 32));
                w.Write(nameBytes, 0, Math.Min(nameBytes.Length, 32));
                break;

            case NetMsgType.Start:
                w.Write(Seed);
                break;

            case NetMsgType.Board:
                w.Write(BoardWidth);
                w.Write(BoardRows);
                // 4 bits per cell (PieceType 0..8), two cells per byte.
                for (int i = 0; i < Cells.Length; i += 2)
                {
                    int lo = (int)Cells[i] & 0xF;
                    int hi = i + 1 < Cells.Length ? (int)Cells[i + 1] & 0xF : 0;
                    w.Write((byte)(lo | (hi << 4)));
                }
                break;

            case NetMsgType.Active:
                w.Write(PieceVisible);
                if (PieceVisible)
                {
                    w.Write((byte)Piece);
                    w.Write(Row);
                    w.Write(Col);
                    w.Write((byte)Rot);
                }
                break;

            case NetMsgType.Attack:
                w.Write(Lines);
                break;

            case NetMsgType.Stats:
                w.Write(LinesTotal);
                w.Write(Sent);
                break;

            case NetMsgType.GameOver:
            case NetMsgType.RematchRequest:
                break;
        }
        return ms.ToArray();
    }

    /// <summary>Decode one message; false on anything malformed (never throws).</summary>
    public static bool TryDecode(byte[] data, out NetMessage message)
    {
        message = new NetMessage();
        try
        {
            using var ms = new MemoryStream(data);
            using var r = new BinaryReader(ms, Encoding.UTF8);
            var type = (NetMsgType)r.ReadByte();
            switch (type)
            {
                case NetMsgType.Hello:
                {
                    int version = r.ReadInt32();
                    int len = r.ReadByte();
                    var name = Encoding.UTF8.GetString(r.ReadBytes(len));
                    message = new NetMessage { Type = type, Version = version, Name = name };
                    return true;
                }
                case NetMsgType.Start:
                    message = new NetMessage { Type = type, Seed = r.ReadUInt64() };
                    return true;

                case NetMsgType.Board:
                {
                    byte width = r.ReadByte();
                    byte rows = r.ReadByte();
                    int count = width * rows;
                    if (count <= 0 || count > 4096) return false;
                    var cells = new PieceType[count];
                    for (int i = 0; i < count; i += 2)
                    {
                        byte b = r.ReadByte();
                        cells[i] = (PieceType)(b & 0xF);
                        if (i + 1 < count) cells[i + 1] = (PieceType)((b >> 4) & 0xF);
                    }
                    message = new NetMessage { Type = type, BoardWidth = width, BoardRows = rows, Cells = cells };
                    return true;
                }
                case NetMsgType.Active:
                {
                    bool visible = r.ReadBoolean();
                    if (!visible)
                    {
                        message = new NetMessage { Type = type, PieceVisible = false };
                        return true;
                    }
                    message = new NetMessage
                    {
                        Type = type,
                        PieceVisible = true,
                        Piece = (PieceType)r.ReadByte(),
                        Row = r.ReadInt16(),
                        Col = r.ReadSByte(),
                        Rot = (RotationState)r.ReadByte(),
                    };
                    return true;
                }
                case NetMsgType.Attack:
                    message = new NetMessage { Type = type, Lines = r.ReadInt32() };
                    return true;

                case NetMsgType.Stats:
                    message = new NetMessage { Type = type, LinesTotal = r.ReadInt32(), Sent = r.ReadInt32() };
                    return true;

                case NetMsgType.GameOver:
                case NetMsgType.RematchRequest:
                    message = new NetMessage { Type = type };
                    return true;

                default:
                    return false;
            }
        }
        catch (Exception e) when (e is IOException or EndOfStreamException or ArgumentException)
        {
            return false;
        }
    }
}
