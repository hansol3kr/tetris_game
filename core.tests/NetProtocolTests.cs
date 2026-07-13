using System;
using Blockfall.Core;
using Blockfall.Core.Net;
using Xunit;

namespace Blockfall.Core.Tests;

public class NetProtocolTests
{
    private static NetMessage RoundTrip(NetMessage m)
    {
        var bytes = m.Encode();
        Assert.True(NetMessage.TryDecode(bytes, out var decoded));
        Assert.Equal(m.Type, decoded.Type);
        return decoded;
    }

    [Fact]
    public void Hello_RoundTrips()
    {
        var d = RoundTrip(NetMessage.Hello("철웅"));
        Assert.Equal("철웅", d.Name);
        Assert.Equal(NetMessage.ProtocolVersion, d.Version);
    }

    [Fact]
    public void Start_RoundTrips()
    {
        var d = RoundTrip(NetMessage.Start(0xDEADBEEF_CAFEBABEUL));
        Assert.Equal(0xDEADBEEF_CAFEBABEUL, d.Seed);
    }

    [Fact]
    public void Board_RoundTrips_ExactCells()
    {
        var board = new Board();
        // Scatter every piece type, including odd/even indexes (nibble packing).
        board[39, 0] = PieceType.I;
        board[39, 1] = PieceType.O;
        board[39, 2] = PieceType.T;
        board[38, 9] = PieceType.Garbage;
        board[20, 4] = PieceType.Z;
        board[0, 0] = PieceType.L;

        var d = RoundTrip(NetMessage.BoardSnapshot(board));
        Assert.Equal(board.Width, d.BoardWidth);
        Assert.Equal(board.TotalRows, d.BoardRows);
        var original = board.Snapshot();
        Assert.Equal(original.Length, d.Cells.Length);
        for (int i = 0; i < original.Length; i++)
            Assert.Equal(original[i], d.Cells[i]);
    }

    [Fact]
    public void Board_FromLiveGame_RoundTrips()
    {
        var g = Game.Create(GameModeId.Marathon, 42);
        g.Start();
        for (int i = 0; i < 12; i++) g.HardDrop();

        var d = RoundTrip(NetMessage.BoardSnapshot(g.Board));
        var original = g.Board.Snapshot();
        for (int i = 0; i < original.Length; i++)
            Assert.Equal(original[i], d.Cells[i]);
    }

    [Fact]
    public void ActivePiece_RoundTrips()
    {
        var g = Game.Create(GameModeId.Marathon, 42);
        g.Start();
        g.RotateCw();
        var d = RoundTrip(NetMessage.ActivePiece(g.Current));
        Assert.True(d.PieceVisible);
        Assert.Equal(g.Current!.Type, d.Piece);
        Assert.Equal(g.Current.Origin.Row, d.Row);
        Assert.Equal(g.Current.Origin.Col, d.Col);
        Assert.Equal(g.Current.State, d.Rot);
    }

    [Fact]
    public void ActivePiece_Null_RoundTripsAsHidden()
    {
        var d = RoundTrip(NetMessage.ActivePiece(null));
        Assert.False(d.PieceVisible);
    }

    [Fact]
    public void Attack_Stats_GameOver_Rematch_RoundTrip()
    {
        Assert.Equal(4, RoundTrip(NetMessage.Attack(4)).Lines);

        var s = RoundTrip(NetMessage.Stats(37, 12));
        Assert.Equal(37, s.LinesTotal);
        Assert.Equal(12, s.Sent);

        RoundTrip(NetMessage.GameOver());
        RoundTrip(NetMessage.RematchRequest());
    }

    [Fact]
    public void Decode_Garbage_IsRejectedNotThrown()
    {
        Assert.False(NetMessage.TryDecode(Array.Empty<byte>(), out _));
        Assert.False(NetMessage.TryDecode(new byte[] { 250, 1, 2 }, out _));      // unknown type
        Assert.False(NetMessage.TryDecode(new byte[] { (byte)NetMsgType.Start, 1 }, out _)); // truncated
        Assert.False(NetMessage.TryDecode(new byte[] { (byte)NetMsgType.Board, 200, 200 }, out _)); // absurd size
    }
}
