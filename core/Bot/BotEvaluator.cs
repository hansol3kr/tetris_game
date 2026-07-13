namespace Blockfall.Core;

/// <summary>
/// Scores a hypothetical board state for the AI. Uses the well-known "El-Tetris"
/// six-feature heuristic (landing height, eroded piece cells, row/column
/// transitions, holes, cumulative wells) with published weights that clear
/// millions of lines. Pure and deterministic, so the bot's judgement is unit-testable.
///
/// Higher score = better placement. Buggy stacks (holes, tall towers, deep wells)
/// score very negative; clearing lines with the piece scores positive.
/// </summary>
public static class BotEvaluator
{
    // Published El-Tetris weights.
    private const double WLandingHeight = -4.500158825082766;
    private const double WErodedCells   =  3.4181268101392694;
    private const double WRowTransitions = -3.2178882868487753;
    private const double WColTransitions = -9.348695305445199;
    private const double WHoles          = -7.899265427351652;
    private const double WWells          = -3.3855972247263626;

    /// <summary>
    /// Clone the board, drop <paramref name="landed"/> onto it, clear lines, and
    /// return the heuristic score of the resulting position. The input board is
    /// not modified.
    /// </summary>
    public static double ScorePlacement(Board board, Piece landed)
    {
        var scratch = board.Clone();

        Span<Vec2> pcells = stackalloc Vec2[4];
        landed.CellsInto(pcells);

        scratch.Lock(landed);

        // Which of the just-placed rows are now full? (eroded-cells feature)
        // Determine full rows BEFORE collapsing so we can count the piece's own
        // contribution to them.
        int rowsCleared = 0;
        int pieceCellsInClears = 0;
        for (int i = 0; i < 4; i++)
        {
            int r = pcells[i].Row;
            if (r < 0 || r >= scratch.TotalRows) continue;
            if (IsRowFull(scratch, r)) pieceCellsInClears++;
        }

        // Landing height: average height (from the floor) of the piece's cells.
        double sumHeight = 0;
        for (int i = 0; i < 4; i++)
            sumHeight += scratch.TotalRows - pcells[i].Row;
        double landingHeight = sumHeight / 4.0;

        var cleared = scratch.ClearFullRows();
        rowsCleared = cleared.Count;
        int eroded = rowsCleared * pieceCellsInClears;

        int holes = Holes(scratch);
        int rowTrans = RowTransitions(scratch);
        int colTrans = ColTransitions(scratch);
        int wells = CumulativeWells(scratch);

        return WLandingHeight * landingHeight
             + WErodedCells * eroded
             + WRowTransitions * rowTrans
             + WColTransitions * colTrans
             + WHoles * holes
             + WWells * wells;
    }

    /// <summary>
    /// The board that would result from dropping <paramref name="landed"/> and
    /// clearing lines — used by the 2-ply lookahead to score the next piece on top
    /// of this placement. Does not modify the input board.
    /// </summary>
    public static Board ResultBoard(Board board, Piece landed)
    {
        var scratch = board.Clone();
        scratch.Lock(landed);
        scratch.ClearFullRows();
        return scratch;
    }

    /// <summary>Best single-placement score for <paramref name="type"/> on <paramref name="board"/> (1-ply).</summary>
    public static double BestPlacementScore(Board board, PieceType type)
    {
        double best = double.NegativeInfinity;
        for (int s = 0; s < Tetromino.StateCount; s++)
        {
            var state = (RotationState)s;
            var cells = Tetromino.Cells(type, state);
            int minC = int.MaxValue, maxC = int.MinValue;
            foreach (var c in cells) { if (c.Col < minC) minC = c.Col; if (c.Col > maxC) maxC = c.Col; }
            for (int originCol = -minC; originCol <= board.Width - 1 - maxC; originCol++)
            {
                var p = new Piece(type, state, new Vec2(0, originCol));
                if (!board.CanPlace(p)) continue;
                var (landed, _) = board.HardDropTarget(p);
                double sc = ScorePlacement(board, landed);
                if (sc > best) best = sc;
            }
        }
        return best == double.NegativeInfinity ? 0.0 : best;
    }

    private static bool Filled(Board b, int r, int c) => b[r, c] != PieceType.Empty;

    private static bool IsRowFull(Board b, int row)
    {
        for (int c = 0; c < b.Width; c++)
            if (b[row, c] == PieceType.Empty) return false;
        return true;
    }

    private static int TopRow(Board b)
    {
        for (int r = 0; r < b.TotalRows; r++)
            for (int c = 0; c < b.Width; c++)
                if (Filled(b, r, c)) return r;
        return b.TotalRows; // empty board
    }

    // Transitions between filled/empty across each row (walls count as filled).
    // Fully-empty rows above the stack are skipped so buffer height doesn't skew it.
    private static int RowTransitions(Board b)
    {
        int top = TopRow(b);
        int trans = 0;
        for (int r = top; r < b.TotalRows; r++)
        {
            bool prev = true; // left wall
            for (int c = 0; c < b.Width; c++)
            {
                bool cur = Filled(b, r, c);
                if (cur != prev) trans++;
                prev = cur;
            }
            if (!prev) trans++; // right wall is filled
        }
        return trans;
    }

    // Vertical transitions per column (floor counts as filled, ceiling as empty).
    private static int ColTransitions(Board b)
    {
        int trans = 0;
        for (int c = 0; c < b.Width; c++)
        {
            bool prev = false; // open ceiling above the stack
            for (int r = 0; r < b.TotalRows; r++)
            {
                bool cur = Filled(b, r, c);
                if (cur != prev) trans++;
                prev = cur;
            }
            if (!prev) trans++; // floor is filled
        }
        return trans;
    }

    // Empty cells with at least one filled cell above them in the same column.
    private static int Holes(Board b)
    {
        int holes = 0;
        for (int c = 0; c < b.Width; c++)
        {
            bool seenFilled = false;
            for (int r = 0; r < b.TotalRows; r++)
            {
                if (Filled(b, r, c)) seenFilled = true;
                else if (seenFilled) holes++;
            }
        }
        return holes;
    }

    // Cumulative well depth: a run of empty cells walled on both sides adds 1+2+…+d.
    private static int CumulativeWells(Board b)
    {
        int wells = 0;
        for (int c = 0; c < b.Width; c++)
        {
            int depth = 0;
            for (int r = 0; r < b.TotalRows; r++)
            {
                bool isWell = !Filled(b, r, c)
                    && (c == 0 || Filled(b, r, c - 1))
                    && (c == b.Width - 1 || Filled(b, r, c + 1));
                if (isWell) { depth++; wells += depth; }
                else depth = 0;
            }
        }
        return wells;
    }
}
