using Godot;
using Blockfall.Core;
using Blockfall.Core.Net;
using Blockfall.Theme;

namespace Blockfall.Net;

/// <summary>
/// Renders the OPPONENT's board from network snapshots — no <see cref="Game"/>
/// behind it, just the last received cell array plus the low-rate falling-piece
/// pose. Drawn in the same visual language as the local board (baked cell
/// texture, glass panel) so the duel reads as one scene. Redraws only when a
/// snapshot arrives, not per frame.
/// </summary>
public partial class RemoteBoardView : Node2D
{
    private PieceType[] _cells = System.Array.Empty<PieceType>();
    private int _cols = Board.DefaultWidth;
    private int _rows = Board.DefaultVisibleRows + Board.DefaultBufferRows;
    private int VisibleRows => Board.DefaultVisibleRows;
    private int VisibleTop => _rows - VisibleRows;

    private bool _pieceVisible;
    private PieceType _pieceType;
    private int _pieceRow, _pieceCol;
    private RotationState _pieceRot;

    private float _cell = 24f;
    private Vector2 _origin;
    private Texture2D _cellTex = null!;
    private StyleBoxTexture _panelSb = null!;
    private int _bakedPx = -1;

    public Vector2 BoardOrigin => _origin;
    public float CellSize => _cell;
    public int Columns => _cols;

    public void Layout(Vector2 areaSize, Vector2 areaOffset)
    {
        float cw = areaSize.X / _cols;
        float ch = areaSize.Y / VisibleRows;
        _cell = Mathf.Floor(Mathf.Min(cw, ch));
        float w = _cell * _cols, h = _cell * VisibleRows;
        _origin = areaOffset + new Vector2((areaSize.X - w) / 2f, (areaSize.Y - h) / 2f);

        int px = Mathf.Clamp((int)_cell, 8, 128);
        if (px != _bakedPx)
        {
            _bakedPx = px;
            _cellTex = TextureFactory.Cell(px);
            _panelSb = TextureFactory.GlassStyle(12,
                new Color(0.045f, 0.055f, 0.105f, 0.85f), new Color(0.022f, 0.028f, 0.065f, 0.90f),
                Palette.GlassBorder, 1.2f, 0.10f, 0, 0);
        }
        QueueRedraw();
    }

    public void ApplySnapshot(NetMessage msg)
    {
        _cells = msg.Cells;
        _cols = msg.BoardWidth;
        _rows = msg.BoardRows;
        QueueRedraw();
    }

    public void ApplyActive(NetMessage msg)
    {
        _pieceVisible = msg.PieceVisible;
        _pieceType = msg.Piece;
        _pieceRow = msg.Row;
        _pieceCol = msg.Col;
        _pieceRot = msg.Rot;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_panelSb is null) return;

        float w = _cell * _cols, h = _cell * VisibleRows;
        _panelSb.Draw(GetCanvasItem(), new Rect2(_origin - new Vector2(8, 8), new Vector2(w + 16, h + 16)));

        for (int c = 1; c < _cols; c++)
            DrawLine(_origin + new Vector2(c * _cell, 0), _origin + new Vector2(c * _cell, h), Palette.GridLine, 1f);
        for (int r = 1; r < VisibleRows; r++)
            DrawLine(_origin + new Vector2(0, r * _cell), _origin + new Vector2(w, r * _cell), Palette.GridLine, 1f);

        if (_cells.Length == _cols * _rows)
        {
            for (int row = VisibleTop; row < _rows; row++)
                for (int col = 0; col < _cols; col++)
                {
                    var type = _cells[row * _cols + col];
                    if (type != PieceType.Empty)
                        DrawCell(row, col, Palette.ForPiece(type), 1f);
                }
        }

        if (_pieceVisible)
        {
            var color = Palette.ForPiece(_pieceType);
            foreach (var c in Tetromino.Cells(_pieceType, _pieceRot))
            {
                int row = _pieceRow + c.Row, col = _pieceCol + c.Col;
                if (row >= VisibleTop && row < _rows && col >= 0 && col < _cols)
                    DrawCell(row, col, color, 0.95f);
            }
        }
    }

    private void DrawCell(int row, int col, Color color, float alpha)
    {
        float gap = Mathf.Max(1f, _cell * 0.05f);
        var pos = _origin + new Vector2(col * _cell, (row - VisibleTop) * _cell);
        DrawTextureRect(_cellTex,
            new Rect2(pos + new Vector2(gap, gap), new Vector2(_cell - gap * 2, _cell - gap * 2)),
            false, new Color(color.R, color.G, color.B, alpha));
    }
}
