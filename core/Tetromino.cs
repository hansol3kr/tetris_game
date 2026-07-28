namespace Blockfall.Core;

/// <summary>
/// Static shape + wall-kick data for the Super Rotation System (SRS).
///
/// Each piece is defined by its four occupied cells in every rotation state,
/// expressed as (row, col) offsets inside the piece's bounding box
/// (3x3 for J L S T Z, 4x4 for I, 2x2 for O). Rotating a piece changes the
/// state index; the bounding-box origin only moves when a wall kick is applied.
///
/// The kick tables are stored in the canonical SRS (x-right, y-up) convention
/// so they can be diffed against public references. They are converted to board
/// space in <see cref="Piece"/> when tested.
/// </summary>
public static class Tetromino
{
    public const int StateCount = 4;

    // Occupied cells per piece per state. Index: [pieceIndex][state] -> 4 cells.
    // pieceIndex maps PieceType (I=1..L=7) minus 1.
    private static readonly Vec2[][][] Shapes = BuildShapes();

    // Bounding-box side length per piece (used for spawn centering + T-spin corners).
    private static readonly int[] BoxSize = { 4, 2, 3, 3, 3, 3, 3 }; // I,O,T,S,Z,J,L

    /// <summary>Returns the four occupied cells (box-relative) for a piece in a given state.</summary>
    public static Vec2[] Cells(PieceType type, RotationState state)
        => Shapes[PieceIndex(type)][(int)state];

    public static int BoundingBox(PieceType type) => BoxSize[PieceIndex(type)];

    private static int PieceIndex(PieceType type) => (int)type - 1;

    // ----- Spawn placement -------------------------------------------------
    // Pieces spawn horizontally centered, straddling the top of the visible
    // field so they visibly drop in. SpawnRow is the box's top row in board
    // coordinates (rows above VisibleTop are the hidden buffer).
    public static Vec2 SpawnOrigin(PieceType type, int bufferRows)
    {
        // Standard guideline: 3-wide pieces spawn at columns 3-5, I at 3-6, O at 4-5.
        // Board is 10 wide. Box top sits so the lowest spawn cells enter the field.
        int col = type == PieceType.O ? 4 : 3;
        // Place the box so its bottom-most occupied row lands at the first visible row.
        // For simplicity we spawn the box top two rows above the visible line.
        int row = bufferRows - 2;
        if (type == PieceType.I) row = bufferRows - 2; // I's cells are on box row 1
        return new Vec2(row, col);
    }

    // ----- Wall-kick tables (SRS, x-right/y-up) ----------------------------
    // Indexed by [fromState][toState]. Only adjacent (CW/CCW) and 180 entries
    // are populated. Empty transitions fall back to the no-op test.
    private static readonly Kick[][][] JlstzKicks = BuildJlstzKicks();
    private static readonly Kick[][][] IKicks = BuildIKicks();
    private static readonly Kick[][][] OKicks = BuildOKicks();
    private static readonly Kick[][][] Kicks180V1 = Build180KicksV1();
    private static readonly Kick[][][] Kicks180V2 = Build180KicksV2();

    /// <summary>
    /// The ordered kick tests for one rotation. <paramref name="rules"/> selects the
    /// 180° table generation — the only kick data that has ever changed — so a replay
    /// recorded before the fix still resolves its spins exactly as it did when played.
    /// CW/CCW kicks are canonical SRS and are identical in every rules version.
    /// </summary>
    public static Kick[] KickSequence(PieceType type, RotationState from, RotationState to,
        RulesVersion rules = SimRules.Current)
    {
        int f = (int)from, t = (int)to;
        bool is180 = (f + 2) % 4 == t;
        if (is180)
            return rules == RulesVersion.V1 ? Kicks180V1[f][t] : Kicks180V2[f][t];
        return type switch
        {
            PieceType.O => OKicks[f][t],
            PieceType.I => IKicks[f][t],
            _ => JlstzKicks[f][t],
        };
    }

    // =======================================================================
    //  Shape construction
    // =======================================================================
    private static Vec2[][][] BuildShapes()
    {
        // Helper to turn a list of (r,c) pairs into Vec2[].
        static Vec2[] C(params (int r, int c)[] pts)
        {
            var arr = new Vec2[pts.Length];
            for (int i = 0; i < pts.Length; i++) arr[i] = new Vec2(pts[i].r, pts[i].c);
            return arr;
        }

        // Order in this array MUST match PieceType (I=1 -> index 0, ... L=7 -> index 6).
        var shapes = new Vec2[7][][];

        // I (4x4 box)
        shapes[PieceIndex(PieceType.I)] = new[]
        {
            C((1,0),(1,1),(1,2),(1,3)), // 0
            C((0,2),(1,2),(2,2),(3,2)), // R
            C((2,0),(2,1),(2,2),(2,3)), // 2
            C((0,1),(1,1),(2,1),(3,1)), // L
        };

        // O (2x2 box) - identical in all states
        shapes[PieceIndex(PieceType.O)] = new[]
        {
            C((0,0),(0,1),(1,0),(1,1)),
            C((0,0),(0,1),(1,0),(1,1)),
            C((0,0),(0,1),(1,0),(1,1)),
            C((0,0),(0,1),(1,0),(1,1)),
        };

        // T (3x3 box)
        shapes[PieceIndex(PieceType.T)] = new[]
        {
            C((0,1),(1,0),(1,1),(1,2)), // 0
            C((0,1),(1,1),(1,2),(2,1)), // R
            C((1,0),(1,1),(1,2),(2,1)), // 2
            C((0,1),(1,0),(1,1),(2,1)), // L
        };

        // S (3x3 box)
        shapes[PieceIndex(PieceType.S)] = new[]
        {
            C((0,1),(0,2),(1,0),(1,1)), // 0
            C((0,1),(1,1),(1,2),(2,2)), // R
            C((1,1),(1,2),(2,0),(2,1)), // 2
            C((0,0),(1,0),(1,1),(2,1)), // L
        };

        // Z (3x3 box)
        shapes[PieceIndex(PieceType.Z)] = new[]
        {
            C((0,0),(0,1),(1,1),(1,2)), // 0
            C((0,2),(1,1),(1,2),(2,1)), // R
            C((1,0),(1,1),(2,1),(2,2)), // 2
            C((0,1),(1,0),(1,1),(2,0)), // L
        };

        // J (3x3 box)
        shapes[PieceIndex(PieceType.J)] = new[]
        {
            C((0,0),(1,0),(1,1),(1,2)), // 0
            C((0,1),(0,2),(1,1),(2,1)), // R
            C((1,0),(1,1),(1,2),(2,2)), // 2
            C((0,1),(1,1),(2,0),(2,1)), // L
        };

        // L (3x3 box)
        shapes[PieceIndex(PieceType.L)] = new[]
        {
            C((0,2),(1,0),(1,1),(1,2)), // 0
            C((0,1),(1,1),(2,1),(2,2)), // R
            C((1,0),(1,1),(1,2),(2,0)), // 2
            C((0,0),(0,1),(1,1),(2,1)), // L
        };

        return shapes;
    }

    // =======================================================================
    //  Kick tables
    // =======================================================================
    private static Kick[][][] NewKickGrid()
    {
        var g = new Kick[4][][];
        for (int i = 0; i < 4; i++)
        {
            g[i] = new Kick[4][];
            for (int j = 0; j < 4; j++) g[i][j] = new[] { new Kick(0, 0) };
        }
        return g;
    }

    private static Kick[] K(params (int x, int y)[] pts)
    {
        var arr = new Kick[pts.Length];
        for (int i = 0; i < pts.Length; i++) arr[i] = new Kick(pts[i].x, pts[i].y);
        return arr;
    }

    private static Kick[][][] BuildJlstzKicks()
    {
        var g = NewKickGrid();
        // 0->R
        g[0][1] = K((0,0),(-1,0),(-1,1),(0,-2),(-1,-2));
        // R->0
        g[1][0] = K((0,0),(1,0),(1,-1),(0,2),(1,2));
        // R->2
        g[1][2] = K((0,0),(1,0),(1,-1),(0,2),(1,2));
        // 2->R
        g[2][1] = K((0,0),(-1,0),(-1,1),(0,-2),(-1,-2));
        // 2->L
        g[2][3] = K((0,0),(1,0),(1,1),(0,-2),(1,-2));
        // L->2
        g[3][2] = K((0,0),(-1,0),(-1,-1),(0,2),(-1,2));
        // L->0
        g[3][0] = K((0,0),(-1,0),(-1,-1),(0,2),(-1,2));
        // 0->L
        g[0][3] = K((0,0),(1,0),(1,1),(0,-2),(1,-2));
        return g;
    }

    private static Kick[][][] BuildIKicks()
    {
        var g = NewKickGrid();
        g[0][1] = K((0,0),(-2,0),(1,0),(-2,-1),(1,2));
        g[1][0] = K((0,0),(2,0),(-1,0),(2,1),(-1,-2));
        g[1][2] = K((0,0),(-1,0),(2,0),(-1,2),(2,-1));
        g[2][1] = K((0,0),(1,0),(-2,0),(1,-2),(-2,1));
        g[2][3] = K((0,0),(2,0),(-1,0),(2,1),(-1,-2));
        g[3][2] = K((0,0),(-2,0),(1,0),(-2,-1),(1,2));
        g[3][0] = K((0,0),(1,0),(-2,0),(1,-2),(-2,1));
        g[0][3] = K((0,0),(-1,0),(2,0),(-1,2),(2,-1));
        return g;
    }

    private static Kick[][][] BuildOKicks()
    {
        // O never kicks.
        return NewKickGrid();
    }

    /// <summary>
    /// LEGACY (<see cref="RulesVersion.V1"/>) 180 kick set — frozen, never edit.
    ///
    /// Kept only so pre-v1.5 replays re-simulate bit-identically. Its flaw: the R&lt;-&gt;L
    /// entries test x = ±2 with no diagonal candidate, so on a ragged stack roughly
    /// 0.7% of 180 spins succeeded TWO columns away from where the player aimed — a
    /// once-in-143 unpredictable teleport, which is worse for a stacker than an
    /// honest failed rotation.
    /// </summary>
    private static Kick[][][] Build180KicksV1()
    {
        var g = NewKickGrid();
        var flat = K((0,0),(0,1),(0,-1),(0,2),(0,-2),(1,0));
        var upright = K((0,0),(1,0),(-1,0),(2,0),(-2,0),(0,1));
        g[0][2] = flat;
        g[2][0] = flat;
        g[1][3] = upright;
        g[3][1] = upright;
        return g;
    }

    /// <summary>
    /// Current (<see cref="RulesVersion.V2"/>) 180 kick set: the community-standard
    /// table modern clients share, in SRS (x-right / y-up) form.
    ///
    /// Design intent — PREDICTABILITY over permissiveness. Lateral displacement is
    /// capped at ONE column in every entry, and each table tests diagonals before
    /// giving up, so a 180 either lands where the player pictured it or visibly fails.
    /// The mirrored bias (0-&gt;2 kicks up, 2-&gt;0 kicks down, R-&gt;L kicks right, L-&gt;R
    /// kicks left) is what keeps the two directions from resolving to different cells
    /// out of the same pocket. This is marginally STRICTER than the legacy table
    /// (~98.5% vs ~99.0% success on a random stack); that half a percent buys the
    /// removal of every two-column surprise.
    /// </summary>
    private static Kick[][][] Build180KicksV2()
    {
        var g = NewKickGrid();
        // spawn <-> 2: the flip swaps which row the piece's nub occupies, so the
        // escape is vertical first, then a single-column diagonal.
        g[0][2] = K((0,0),(0,1),(1,1),(-1,1),(1,0),(-1,0));
        g[2][0] = K((0,0),(0,-1),(-1,-1),(1,-1),(-1,0),(1,0));
        // R <-> L: the nub crosses the spine, so the escape is one column toward the
        // direction of travel, optionally lifted (never two columns).
        g[1][3] = K((0,0),(1,0),(1,2),(1,1),(0,2),(0,1));
        g[3][1] = K((0,0),(-1,0),(-1,2),(-1,1),(0,2),(0,1));
        return g;
    }
}
