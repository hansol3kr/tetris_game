namespace Blockfall.Core;

/// <summary>
/// A finesse mastery grade. <see cref="None"/> until enough pieces have been
/// scored to be meaningful; then F (rough) … S (near-perfect input economy).
/// </summary>
public enum FinesseRank { None, F, D, C, B, A, S }

/// <summary>The finesse verdict for a single placed piece.</summary>
public readonly struct FinesseResult
{
    /// <summary>False for spins / tucks / hold-abandoned pieces: no fault charged, streak untouched.</summary>
    public bool Scored { get; init; }
    /// <summary>Fewest keypresses that could have reached this (column, orientation) on an empty well.</summary>
    public int MinPresses { get; init; }
    /// <summary>Keypresses the player actually spent moving/rotating this piece.</summary>
    public int ActualPresses { get; init; }
    /// <summary>Wasted presses: <c>Max(0, ActualPresses - MinPresses)</c> (0 when unscored).</summary>
    public int Faults { get; init; }

    /// <summary>A scored placement with zero wasted presses.</summary>
    public bool Clean => Scored && Faults == 0;

    /// <summary>The neutral verdict for a piece that finesse does not grade.</summary>
    public static readonly FinesseResult Unscored = new() { Scored = false };
}

/// <summary>An immutable snapshot of a run's finesse performance (for the results screen).</summary>
public readonly struct FinesseSummary
{
    public int ScoredPieces { get; init; }
    public int CleanPieces { get; init; }
    public int TotalFaults { get; init; }
    public int LongestStreak { get; init; }
    public double Percent { get; init; }
    public FinesseRank Rank { get; init; }
}

/// <summary>
/// Precomputes the minimum number of keypresses ("finesse") needed to place each
/// piece type at any (column, orientation) on an EMPTY well, then answers O(1)
/// lookups. The model is terrain-independent by design: finesse asks "fewest
/// presses to reach a column + orientation at the top of the well, before the one
/// terminal drop", which does not depend on the stack below.
///
/// The search reuses the SAME <see cref="Board.CanPlace"/> and
/// <see cref="Tetromino.KickSequence"/> the live game uses, so its notion of a
/// legal move (including near-wall rotation kicks) can never drift from real play.
/// Pure and deterministic — no engine types leak in, no allocation on the hot path.
/// </summary>
public static class FinesseSolver
{
    private const int Width = Board.DefaultWidth; // 10
    private static readonly Board EmptyBoard = new();
    // A safe reference row deep in the empty field: small vertical kicks and the
    // 4-tall I box stay in bounds, so the only thing that ever blocks a candidate
    // is a COLUMN going out of range — exactly the terrain-independent behaviour we want.
    private static readonly int RefRow = EmptyBoard.VisibleTop;

    // Lazy per-type BFS distance map over (originCol, state). Index by (int)PieceType (I=1..L=7).
    private static readonly System.Collections.Generic.Dictionary<(int col, RotationState state), int>?[] _dist
        = new System.Collections.Generic.Dictionary<(int, RotationState), int>?[9];

    /// <summary>Smallest column offset among a piece's four box cells (for its leftmost occupied column).</summary>
    public static int MinColOffset(PieceType type, RotationState state)
    {
        int min = int.MaxValue;
        foreach (var c in Tetromino.Cells(type, state))
            if (c.Col < min) min = c.Col;
        return min;
    }

    /// <summary>Largest column offset among a piece's four box cells (for its rightmost occupied column).</summary>
    public static int MaxColOffset(PieceType type, RotationState state)
    {
        int max = int.MinValue;
        foreach (var c in Tetromino.Cells(type, state))
            if (c.Col > max) max = c.Col;
        return max;
    }

    /// <summary>
    /// Collapses rotation states that are indistinguishable once placed, so a wasted
    /// rotation of a symmetric piece is correctly a fault. O has one true orientation;
    /// I/S/Z have two (flat ≡ Two, vertical ≡ Left); T/J/L keep all four.
    /// </summary>
    public static RotationState Canonical(PieceType type, RotationState state) => type switch
    {
        PieceType.O => RotationState.Spawn,
        PieceType.I or PieceType.S or PieceType.Z => state switch
        {
            RotationState.Two => RotationState.Spawn, // flat orientations coincide
            RotationState.Left => RotationState.Right, // vertical orientations coincide
            _ => state,
        },
        _ => state,
    };

    /// <summary>
    /// Fewest keypresses to reach the placement identified by (type, orientation,
    /// leftmost occupied column) from spawn. Takes the min over every internal state
    /// in the same canonical class, so symmetric pieces automatically pick the cheaper
    /// rotation direction. Returns -1 only if the placement is unreachable (never for a
    /// genuinely in-bounds placement — the caller treats that as "unscored").
    /// </summary>
    public static int MinPresses(PieceType type, RotationState targetState, int targetLeftCol)
    {
        var canon = Canonical(type, targetState);
        int best = int.MaxValue;
        foreach (var kv in Dist(type))
        {
            var (col, st) = kv.Key;
            if (Canonical(type, st) == canon && col + MinColOffset(type, st) == targetLeftCol)
                if (kv.Value < best) best = kv.Value;
        }
        return best == int.MaxValue ? -1 : best;
    }

    private static System.Collections.Generic.Dictionary<(int col, RotationState state), int> Dist(PieceType type)
    {
        int idx = (int)type;
        return _dist[idx] ??= BuildBfs(type);
    }

    private static System.Collections.Generic.Dictionary<(int col, RotationState state), int> BuildBfs(PieceType type)
    {
        var dist = new System.Collections.Generic.Dictionary<(int col, RotationState state), int>();
        var queue = new System.Collections.Generic.Queue<(int col, RotationState state)>();
        int spawnCol = Tetromino.SpawnOrigin(type, EmptyBoard.BufferRows).Col;
        var start = (spawnCol, RotationState.Spawn);
        dist[start] = 0;
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            int d = dist[node];
            foreach (var next in Neighbors(type, node))
                if (!dist.ContainsKey(next))
                {
                    dist[next] = d + 1;
                    queue.Enqueue(next);
                }
        }
        return dist;
    }

    // Each edge costs one keypress: a single tap, a DAS-to-wall, or a rotation.
    private static System.Collections.Generic.IEnumerable<(int col, RotationState state)> Neighbors(
        PieceType type, (int col, RotationState state) node)
    {
        var (col, state) = node;

        // Single taps (blocked at the wall — CanPlace guards the edge).
        if (CanBeAt(type, state, col - 1)) yield return (col - 1, state);
        if (CanBeAt(type, state, col + 1)) yield return (col + 1, state);

        // DAS to either wall — always legal on an empty board (occupied cols land in [0, 9]).
        yield return (-MinColOffset(type, state), state);
        yield return (Width - 1 - MaxColOffset(type, state), state);

        // Rotations (CW / CCW / 180), each honouring the real SRS kick sequence.
        if (TryRotate(type, state, col, Piece.Cw(state), out int cwCol)) yield return (cwCol, Piece.Cw(state));
        if (TryRotate(type, state, col, Piece.Ccw(state), out int ccwCol)) yield return (ccwCol, Piece.Ccw(state));
        if (TryRotate(type, state, col, Piece.Flip(state), out int flipCol)) yield return (flipCol, Piece.Flip(state));
    }

    private static bool CanBeAt(PieceType type, RotationState state, int col)
        => EmptyBoard.CanPlace(new Piece(type, state, new Vec2(RefRow, col)));

    // Mirrors Game.TryRotate: try each kick in order, take the first that fits.
    private static bool TryRotate(PieceType type, RotationState from, int col, RotationState target, out int newCol)
    {
        var origin = new Vec2(RefRow, col);
        foreach (var k in Tetromino.KickSequence(type, from, target))
        {
            // SRS (x-right/y-up) -> board space: row -= y, col += x.
            var candidate = new Piece(type, target, origin.Offset(-k.Y, k.X));
            if (EmptyBoard.CanPlace(candidate)) { newCol = candidate.Origin.Col; return true; }
        }
        newCol = 0;
        return false;
    }
}

/// <summary>
/// Tracks input economy ("finesse") across a run. It is fed SEMANTIC player actions
/// from the presentation layer — a tap, a DAS-to-wall, a rotation, a soft drop —
/// because tap-vs-hold can only be distinguished where the input timing lives
/// (the InputController), not inside the deterministic engine. On each lock it grades
/// the piece against <see cref="FinesseSolver"/> and maintains faults, clean streaks,
/// a percentage, and a letter rank.
///
/// Pure core: no Godot, no wall-clock, no RNG — fully unit-testable.
/// </summary>
public sealed class FinesseTracker
{
    // ----- Live read-model (HUD + results) ---------------------------------
    public int CurrentPieceFaults { get; private set; }
    public int TotalFaults { get; private set; }
    /// <summary>Pieces that were graded — the denominator of <see cref="FinessePercent"/>.</summary>
    public int ScoredPieces { get; private set; }
    public int CleanPieces { get; private set; }
    /// <summary>Pieces finesse deliberately skipped (spins, tucks, hold-abandoned).</summary>
    public int UnscoredPieces { get; private set; }
    public int CleanStreak { get; private set; }
    public int BestCleanStreak { get; private set; }
    public double FinessePercent => ScoredPieces == 0 ? 100.0 : 100.0 * CleanPieces / ScoredPieces;
    public bool LastWasClean { get; private set; }
    public int LastFaults { get; private set; }

    /// <summary>Letter grade, unrated until enough pieces are scored to be meaningful.</summary>
    public FinesseRank Rank
    {
        get
        {
            if (ScoredPieces < RankMinPieces) return FinesseRank.None;
            double p = FinessePercent;
            if (p >= 98) return FinesseRank.S;
            if (p >= 94) return FinesseRank.A;
            if (p >= 88) return FinesseRank.B;
            if (p >= 78) return FinesseRank.C;
            if (p >= 65) return FinesseRank.D;
            return FinesseRank.F;
        }
    }

    private const int RankMinPieces = 10;

    // ----- Events (presentation subscribes) --------------------------------
    /// <summary>Fires once per SCORED piece; argument is that piece's fault count (0 = clean).</summary>
    public event Action<int>? PieceScored;
    /// <summary>Fires when the clean streak reaches 10, 25, 50, 100, then every +50.</summary>
    public event Action<int>? StreakMilestone;

    // ----- Per-piece state -------------------------------------------------
    private PieceType _type;
    private int _actual;
    private bool _tracking;
    private bool _softDropped;
    private bool _movedAfterSoftDrop;
    private bool _provisionalTap;
    private int _provisionalDir;

    /// <summary>
    /// Start tracking a freshly spawned piece. If a previous piece is still in flight
    /// (i.e. it was swapped out by Hold and never locked), it is silently discarded:
    /// neither graded nor counted, and the streak is untouched.
    /// </summary>
    public void BeginPiece(PieceType type, int spawnCol, RotationState spawnState)
    {
        _type = type;
        _actual = 0;
        _tracking = true;
        _softDropped = false;
        _movedAfterSoftDrop = false;
        _provisionalTap = false;
        _provisionalDir = 0;
    }

    /// <summary>
    /// Grade the piece now that it has locked at (finalCol, finalState). Spins and
    /// tucks (a move after a soft drop) are returned <see cref="FinesseResult.Unscored"/>.
    /// </summary>
    public FinesseResult Finalize(int finalCol, RotationState finalState, bool wasSpin)
    {
        if (!_tracking) return FinesseResult.Unscored;
        _tracking = false;
        _provisionalTap = false;

        // Spins and tucks legitimately need extra inputs — don't grade them.
        int min = wasSpin ? -1 : FinesseSolver.MinPresses(
            _type, finalState, finalCol + FinesseSolver.MinColOffset(_type, finalState));
        bool unscored = wasSpin || (_softDropped && _movedAfterSoftDrop) || min < 0;
        if (unscored)
        {
            UnscoredPieces++;
            CurrentPieceFaults = 0;
            return FinesseResult.Unscored;
        }

        int faults = System.Math.Max(0, _actual - min);
        ScoredPieces++;
        TotalFaults += faults;
        CurrentPieceFaults = faults;
        LastFaults = faults;

        if (faults == 0)
        {
            CleanPieces++;
            CleanStreak++;
            if (CleanStreak > BestCleanStreak) BestCleanStreak = CleanStreak;
            LastWasClean = true;
            if (IsMilestone(CleanStreak)) StreakMilestone?.Invoke(CleanStreak);
        }
        else
        {
            CleanStreak = 0;
            LastWasClean = false;
        }

        PieceScored?.Invoke(faults);
        return new FinesseResult { Scored = true, MinPresses = min, ActualPresses = _actual, Faults = faults };
    }

    // ----- Semantic input (fed from the InputController) -------------------

    /// <summary>A single tap in <paramref name="dir"/> (-1 left, +1 right).</summary>
    public void OnTapMove(int dir)
    {
        if (!_tracking) return;
        if (_softDropped) _movedAfterSoftDrop = true;
        _actual++;
        _provisionalTap = true;
        _provisionalDir = dir;
    }

    /// <summary>
    /// One DAS-to-wall engagement in <paramref name="dir"/>. If the immediately
    /// preceding tap was the same direction, this is the SAME physical hold, so it is
    /// absorbed (one hold = one input); otherwise it is a distinct press.
    /// </summary>
    public void OnDasMove(int dir)
    {
        if (!_tracking) return;
        if (_softDropped) _movedAfterSoftDrop = true;
        if (_provisionalTap && _provisionalDir == dir)
        {
            // Absorbed: the initiating tap already booked this input.
        }
        else
        {
            _actual++;
        }
        _provisionalTap = false;
    }

    /// <summary>A successful rotation (any direction, including 180).</summary>
    public void OnRotate()
    {
        if (!_tracking) return;
        if (_softDropped) _movedAfterSoftDrop = true;
        _actual++;
        // Do NOT clear the tap->DAS absorb latch: a rotation is a separate key that does
        // not end a still-held horizontal direction, so a DAS that charges afterwards must
        // still be absorbed by the initiating tap (one physical hold = one input).
    }

    /// <summary>Soft drop engaged. Neutral for reaching a column, but arms the tuck rule.</summary>
    public void OnSoftDrop()
    {
        if (!_tracking) return;
        _softDropped = true;
        _provisionalTap = false;
    }

    /// <summary>Explicit hold hook. Auto-discard in <see cref="BeginPiece"/> already covers Hold.</summary>
    public void OnHold()
    {
        _provisionalTap = false;
    }

    /// <summary>Reset all counters for a new run.</summary>
    public void Reset()
    {
        CurrentPieceFaults = TotalFaults = ScoredPieces = CleanPieces = UnscoredPieces = 0;
        CleanStreak = BestCleanStreak = LastFaults = 0;
        LastWasClean = false;
        _tracking = _softDropped = _movedAfterSoftDrop = _provisionalTap = false;
        _actual = _provisionalDir = 0;
    }

    /// <summary>Immutable snapshot for the results screen / leaderboards.</summary>
    public FinesseSummary Snapshot() => new()
    {
        ScoredPieces = ScoredPieces,
        CleanPieces = CleanPieces,
        TotalFaults = TotalFaults,
        LongestStreak = BestCleanStreak,
        Percent = FinessePercent,
        Rank = Rank,
    };

    private static bool IsMilestone(int streak)
        => streak == 10 || streak == 25 || (streak >= 50 && streak % 50 == 0);
}
