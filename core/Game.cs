namespace Blockfall.Core;

/// <summary>
/// The engine-agnostic heart of Blockfall. Owns the board, the active piece,
/// gravity, lock delay, hold, scoring, and mode goals. The presentation layer
/// (Godot) calls the command methods on input and <see cref="Update"/> every
/// frame, then reads state + subscribes to events for rendering and audio.
///
/// No allocations on the hot path; no engine types; fully deterministic given
/// the same seed and the same sequence of commands + dt values.
/// </summary>
public sealed class Game
{
    public Board Board { get; }
    public Scoring Scoring { get; }
    public GameMode Mode { get; }
    public GameConfig Config => Mode.Config;
    public RunStats Stats { get; } = new();

    public Piece? Current { get; private set; }

    /// <summary>Monotonic count of pieces spawned; lets an external driver (the bot) detect a piece change.</summary>
    public int SpawnCount { get; private set; }

    public PieceType Hold { get; private set; } = PieceType.Empty;
    public bool CanHold { get; private set; } = true;
    public GameStatus Status { get; private set; } = GameStatus.Ready;
    public double Elapsed { get; private set; }

    /// <summary>Garbage lines sent this run (attack skill readout).</summary>
    public int TotalGarbageSent { get; private set; }

    /// <summary>Garbage queued to drop in (Survival/Versus) — drives the incoming meter.</summary>
    public int PendingGarbage => _pendingGarbage;

    /// <summary>
    /// When true, queued incoming garbage is committed to the board on each lock
    /// (the versus feel: an opponent's attack lands on your next piece unless you
    /// cancel it). Survival drives this via its rise timer instead. Off by default.
    /// </summary>
    public bool CommitPendingGarbageOnLock { get; set; }

    private readonly IPieceGenerator _generator;
    private readonly IRandomSource _garbageRng;

    // Garbage / attack bookkeeping (Dig / Survival).
    private int _pendingGarbage;
    private double _riseTimer;
    private double _riseInterval;
    private const double MinRiseInterval = 0.8;
    private const int GarbageCommitCap = 6;

    // Lock-delay / gravity bookkeeping.
    private double _gravityAccum;
    private double _lockTimer;
    private int _lockResets;
    private int _lowestRow;
    private bool _softDropping;

    // T-spin context: was the last successful action a rotation, and via which kick?
    private bool _lastActionWasRotation;
    private int _lastKickIndex;
    private bool _lastRotationWas180;

    // ----- Events (presentation layer subscribes) --------------------------
    public event Action<Piece>? PieceSpawned;
    public event Action<MoveKind>? PieceMoved;
    public event Action<RotationEvent>? PieceRotated;
    public event Action<LockEvent>? PieceLocked;
    public event Action<int>? LevelChanged;
    public event Action<PieceType>? HoldChanged;
    public event Action? GameOver;
    public event Action? Completed;
    public event Action<int>? HardDropped; // distance fallen
    public event Action<int>? GarbageReceived; // garbage lines just added to the board
    public event Action<int>? AttackPerformed; // garbage lines this clear would send (gross)
    public event Action<int>? GarbageSentToOpponent; // NET garbage after cancelling own incoming (versus)

    public Game(GameMode mode, IPieceGenerator generator, IRandomSource? garbageRng = null)
    {
        Mode = mode;
        _generator = generator;
        // Independent, deterministic stream for garbage holes (kept separate from the
        // 7-bag stream so garbage doesn't perturb piece order).
        _garbageRng = garbageRng ?? new XorShiftRandom(0x5DEECE66DUL);
        Board = new Board();
        Scoring = new Scoring(mode.StartLevel);
    }

    /// <summary>
    /// Convenience constructor: builds a seeded 7-bag game for a mode. An optional
    /// <paramref name="configOverride"/> replaces the mode's config (used to apply
    /// per-player handling settings such as DAS/ARR/ghost).
    /// </summary>
    public static Game Create(GameModeId modeId, ulong seed, GameConfig? configOverride = null)
    {
        var mode = GameMode.ById(modeId);
        if (configOverride is not null) mode = mode.WithConfig(configOverride);
        var rng = new XorShiftRandom(seed);
        var gen = new SevenBagGenerator(rng);
        var garbageRng = new XorShiftRandom(seed ^ 0x9E3779B97F4A7C15UL);
        return new Game(mode, gen, garbageRng);
    }

    public void Start()
    {
        Status = GameStatus.Running;
        Elapsed = 0;
        _riseInterval = Mode.GarbageRiseInterval;
        if (Mode.InitialGarbage > 0) PrefillGarbage(Mode.InitialGarbage);
        SpawnNext();
    }

    /// <summary>Fill the bottom of the board with garbage rows (Dig), each holed randomly.</summary>
    private void PrefillGarbage(int rows)
    {
        for (int i = 0; i < rows; i++)
            Board.InsertGarbageLines(1, _garbageRng.Next(Board.Width));
    }

    public void Pause() { if (Status == GameStatus.Running) Status = GameStatus.Paused; }
    public void Resume() { if (Status == GameStatus.Paused) Status = GameStatus.Running; }

    /// <summary>
    /// Second-chance revive (the store's consumable booster): valid only after a
    /// game over. Wipes the stack and any pending garbage, resets the combo/B2B
    /// chain, and resumes play — score, lines, level, and the clock are kept.
    /// A piece that was blocked at spawn respawns as the same type (it was never
    /// played); a piece that died locking is replaced by the next from the bag.
    /// Returns false unless the game is actually over.
    /// </summary>
    public bool Revive()
    {
        if (Status != GameStatus.GameOver) return false;
        Board.Clear();
        _pendingGarbage = 0;
        Scoring.ResetChain();
        Status = GameStatus.Running;
        if (_diedAtSpawn && Current is not null) SpawnPiece(Current.Type);
        else SpawnNext();
        return true;
    }

    private bool _diedAtSpawn;

    // ======================================================================
    //  Commands (called from input)
    // ======================================================================
    public bool MoveLeft() => TryShift(0, -1, MoveKind.Left);
    public bool MoveRight() => TryShift(0, 1, MoveKind.Right);

    private bool TryShift(int dRow, int dCol, MoveKind kind)
    {
        if (Status != GameStatus.Running || Current is null) return false;
        var moved = Current.Moved(dRow, dCol);
        if (!Board.CanPlace(moved)) return false;
        Current = moved;
        _lastActionWasRotation = false;
        OnPieceChanged();
        PieceMoved?.Invoke(kind);
        return true;
    }

    /// <summary>Engage/disengage soft drop (accelerated gravity).</summary>
    public void SetSoftDrop(bool on) => _softDropping = on;

    /// <summary>
    /// Enqueue incoming garbage from an opponent (versus). It sits in the incoming
    /// queue and drops in on the next lock unless cancelled by your own attack.
    /// </summary>
    public void ReceiveGarbage(int lines)
    {
        if (lines > 0) _pendingGarbage += lines;
    }

    /// <summary>Discrete one-cell soft drop (for tap controls); scores 1 point on success.</summary>
    public bool SoftDropStep()
    {
        if (Status != GameStatus.Running || Current is null) return false;
        var moved = Current.Moved(1, 0);
        if (!Board.CanPlace(moved)) return false;
        Current = moved;
        Scoring.AddSoftDrop(1);
        _lastActionWasRotation = false;
        OnDownwardProgress();
        PieceMoved?.Invoke(MoveKind.SoftStep);
        return true;
    }

    public bool RotateCw() => TryRotate(Piece.Cw(Current?.State ?? RotationState.Spawn));
    public bool RotateCcw() => TryRotate(Piece.Ccw(Current?.State ?? RotationState.Spawn));
    public bool Rotate180() => TryRotate(Piece.Flip(Current?.State ?? RotationState.Spawn));

    private bool TryRotate(RotationState target)
    {
        if (Status != GameStatus.Running || Current is null) return false;
        var piece = Current;
        var kicks = Tetromino.KickSequence(piece.Type, piece.State, target);
        for (int i = 0; i < kicks.Length; i++)
        {
            var k = kicks[i];
            // Convert SRS (x-right/y-up) to board space: row -= y, col += x.
            var candidate = new Piece(piece.Type, target, piece.Origin.Offset(-k.Y, k.X));
            if (Board.CanPlace(candidate))
            {
                Current = candidate;
                _lastActionWasRotation = true;
                _lastKickIndex = i;
                _lastRotationWas180 = (((int)piece.State + 2) & 3) == (int)target;
                OnPieceChanged();
                PieceRotated?.Invoke(new RotationEvent { Success = true, KickIndex = i, NewState = target });
                return true;
            }
        }
        PieceRotated?.Invoke(new RotationEvent { Success = false, KickIndex = -1, NewState = piece.State });
        return false;
    }

    public bool HardDrop()
    {
        if (Status != GameStatus.Running || Current is null) return false;
        var (landed, distance) = Board.HardDropTarget(Current);
        Current = landed;
        if (distance > 0)
        {
            Scoring.AddHardDrop(distance);
            _lastActionWasRotation = false; // moving down by drop is not a spin
        }
        HardDropped?.Invoke(distance);
        LockPiece();
        return true;
    }

    public bool HoldPiece()
    {
        if (!Config.HoldEnabled || Status != GameStatus.Running || Current is null || !CanHold)
            return false;

        var incoming = Hold;
        Hold = Current.Type;
        CanHold = false;
        if (incoming == PieceType.Empty)
            SpawnNext(consumeFromGenerator: true);
        else
            SpawnPiece(incoming);
        HoldChanged?.Invoke(Hold);
        return true;
    }

    // ======================================================================
    //  Per-frame update
    // ======================================================================
    public void Update(double dt)
    {
        if (Status != GameStatus.Running || Current is null) return;

        Elapsed += dt;

        // Time-limited modes (Ultra) finish on the clock.
        if (Mode.TimeLimit > 0 && Elapsed >= Mode.TimeLimit)
        {
            Finish(GameStatus.Completed);
            return;
        }

        // Survival: garbage wells up over time and the interval keeps shrinking.
        if (Mode.GarbageRiseInterval > 0)
        {
            _riseTimer += dt;
            while (_riseTimer >= _riseInterval)
            {
                _riseTimer -= _riseInterval;
                _pendingGarbage += Mode.GarbageRiseAmount;
                _riseInterval = Math.Max(MinRiseInterval, _riseInterval - Mode.GarbageRiseAccel);
            }
        }

        // Gravity.
        double secondsPerCell = Config.GravityForLevel(Scoring.Level);
        double rate = 1.0 / secondsPerCell;
        if (_softDropping) rate *= Config.SoftDropFactor;
        _gravityAccum += dt * rate;

        while (_gravityAccum >= 1.0)
        {
            _gravityAccum -= 1.0;
            var down = Current.Moved(1, 0);
            if (Board.CanPlace(down))
            {
                Current = down;
                if (_softDropping) Scoring.AddSoftDrop(1);
                _lastActionWasRotation = false;
                OnDownwardProgress();
            }
            else
            {
                _gravityAccum = 0;
                break;
            }
        }

        // Lock delay while landed.
        if (Board.IsLanded(Current))
        {
            _lockTimer += dt;
            if (_lockTimer >= Config.LockDelay || _lockResets > Config.MaxLockResets)
                LockPiece();
        }
        else
        {
            _lockTimer = 0;
        }
    }

    // ======================================================================
    //  Internal helpers
    // ======================================================================
    private void OnPieceChanged()
    {
        if (Current is null) return;
        if (Current.Origin.Row > _lowestRow)
        {
            OnDownwardProgress();
        }
        else if (Board.IsLanded(Current))
        {
            // Move/rotate reset while resting: refresh the timer, bounded by MaxLockResets.
            _lockTimer = 0;
            _lockResets++;
        }
    }

    private void OnDownwardProgress()
    {
        if (Current is null) return;
        _lowestRow = Current.Origin.Row;
        _lockTimer = 0;
        _lockResets = 0;
    }

    private void SpawnNext(bool consumeFromGenerator = true)
    {
        var type = _generator.Next();
        SpawnPiece(type);
    }

    private void SpawnPiece(PieceType type)
    {
        SpawnCount++;
        _diedAtSpawn = false;
        var origin = Tetromino.SpawnOrigin(type, Board.BufferRows);
        var piece = new Piece(type, RotationState.Spawn, origin);

        // Block-out: if the spawn location is already occupied, it's game over.
        if (!Board.CanPlace(piece))
        {
            if (Mode.CanTopOut)
            {
                Current = piece;
                _diedAtSpawn = true; // Revive respawns this exact piece
                Finish(GameStatus.GameOver);
                return;
            }
            // For non-topping modes, nudge upward once; if still blocked, stop.
            var lifted = piece.Moved(-1, 0);
            piece = Board.CanPlace(lifted) ? lifted : piece;
        }

        Current = piece;
        _gravityAccum = 0;
        _lockTimer = 0;
        _lockResets = 0;
        _lowestRow = piece.Origin.Row;
        _lastActionWasRotation = false;
        _lastKickIndex = 0;
        _lastRotationWas180 = false;
        PieceSpawned?.Invoke(piece);
    }

    private void LockPiece()
    {
        if (Current is null) return;
        var piece = Current;

        // Classify a potential T-spin BEFORE mutating the board.
        var spin = SpinDetector.Detect(Board, piece, _lastActionWasRotation, _lastKickIndex,
            _lastRotationWas180, allSpin: Config.AllSpin);

        Board.Lock(piece);
        var cleared = Board.ClearFullRows();
        bool perfect = cleared.Count > 0 && Board.IsEmpty();

        int levelBefore = Scoring.Level;
        var result = Scoring.ApplyLock(cleared.Count, spin, perfect);

        Stats.PiecesPlaced++;
        Stats.Record(result);

        PieceLocked?.Invoke(new LockEvent { Piece = piece, ClearedRows = cleared, Result = result });

        if (Scoring.Level != levelBefore) LevelChanged?.Invoke(Scoring.Level);

        // Attack / garbage exchange (Dig completion; Survival commits incoming garbage).
        if (ApplyGarbageMechanics(result)) return;

        // Lock-out: a piece that locks entirely above the visible field ends the run.
        if (Mode.CanTopOut && AllCellsAboveField(piece))
        {
            Finish(GameStatus.GameOver);
            return;
        }

        // Mode goal check (Sprint lines / Marathon level cap).
        if (Mode.IsGoalReached(Scoring.LinesCleared, Scoring.Level, Elapsed))
        {
            Finish(GameStatus.Completed);
            return;
        }

        CanHold = true;
        SpawnNext();
    }

    /// <summary>
    /// Runs the attack/garbage exchange after a lock. Returns true if the run ended
    /// (Dig cleared, or a Survival garbage push topped the player out).
    /// </summary>
    private bool ApplyGarbageMechanics(ClearResult result)
    {
        // Attack readout + cancellation of pending garbage.
        int attack = AttackTable.LinesSent(result);
        if (attack > 0)
        {
            TotalGarbageSent += attack;
            AttackPerformed?.Invoke(attack);
            // Cancel your own incoming first; only the surplus reaches the opponent.
            int cancel = Math.Min(_pendingGarbage, attack);
            _pendingGarbage -= cancel;
            int outgoing = attack - cancel;
            if (outgoing > 0) GarbageSentToOpponent?.Invoke(outgoing);
        }

        // Dig: finished the instant the board is garbage-free.
        if (Mode.DigGoal && Board.GarbageRowCount() == 0)
        {
            Finish(GameStatus.Completed);
            return true;
        }

        // Survival & Versus: drop whatever incoming garbage remains (capped per piece).
        if ((Mode.GarbageRiseInterval > 0 || CommitPendingGarbageOnLock) && _pendingGarbage > 0)
        {
            int toAdd = Math.Min(_pendingGarbage, GarbageCommitCap);
            _pendingGarbage -= toAdd;
            bool overflowed = Board.InsertGarbageLines(toAdd, _garbageRng.Next(Board.Width));
            GarbageReceived?.Invoke(toAdd);
            if (overflowed && Mode.CanTopOut)
            {
                Finish(GameStatus.GameOver);
                return true;
            }
        }
        return false;
    }

    private bool AllCellsAboveField(Piece piece)
    {
        foreach (var c in piece.Cells())
            if (c.Row >= Board.VisibleTop) return false;
        return true;
    }

    private void Finish(GameStatus status)
    {
        Status = status;
        Stats.FinishTime = Elapsed;
        if (status == GameStatus.GameOver) GameOver?.Invoke();
        else if (status == GameStatus.Completed) Completed?.Invoke();
    }

    // ----- Read helpers for the renderer -----------------------------------
    public IReadOnlyList<PieceType> Preview() => _generator.Preview(Config.PreviewCount);

    /// <summary>Where the current piece would land if hard-dropped (for the ghost).</summary>
    public Piece? GhostPiece()
    {
        if (Current is null || !Config.GhostEnabled) return null;
        return Board.HardDropTarget(Current).landed;
    }
}
