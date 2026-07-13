namespace Blockfall.Core;

/// <summary>Difficulty knobs for the CPU: how fast it acts, how often it blunders, whether it uses hold.</summary>
public sealed class BotDifficulty
{
    public string Name { get; init; } = "Normal";
    /// <summary>Seconds between the bot's individual inputs (lower = faster/stronger).</summary>
    public double ThinkInterval { get; init; } = 0.10;
    /// <summary>Chance per piece to deliberately pick a sub-optimal placement (0 = perfect).</summary>
    public double BlunderChance { get; init; }
    /// <summary>Random score jitter to vary otherwise-identical play (0 = pure heuristic).</summary>
    public double ScoreNoise { get; init; }
    /// <summary>May the bot use its hold slot when planning?</summary>
    public bool UseHold { get; init; } = true;

    /// <summary>Weigh the best next-piece placement too (2-ply search) — much stronger stacking.</summary>
    public bool Lookahead { get; init; }

    public static BotDifficulty Easy => new()
        { Name = "Easy", ThinkInterval = 0.20, BlunderChance = 0.30, ScoreNoise = 30, UseHold = false };
    public static BotDifficulty Normal => new()
        { Name = "Normal", ThinkInterval = 0.11, BlunderChance = 0.10, ScoreNoise = 12, UseHold = true };
    public static BotDifficulty Hard => new()
        { Name = "Hard", ThinkInterval = 0.055, BlunderChance = 0.02, ScoreNoise = 4, UseHold = true };
    public static BotDifficulty Master => new()
        { Name = "Master", ThinkInterval = 0.025, BlunderChance = 0.0, ScoreNoise = 0, UseHold = true };
    public static BotDifficulty Grandmaster => new()
        { Name = "Grandmaster", ThinkInterval = 0.015, BlunderChance = 0.0, ScoreNoise = 0, UseHold = true, Lookahead = true };

    public static BotDifficulty[] All => new[] { Easy, Normal, Hard, Master, Grandmaster };
}

/// <summary>
/// A heuristic AI that plays a <see cref="Game"/> by issuing the same commands a
/// human would. Each "think tick" it takes one corrective action toward a target
/// placement chosen by <see cref="BotEvaluator"/> — reading the live piece each
/// tick, so it self-corrects for wall kicks. Deterministic given its RNG seed,
/// which makes bot-vs-bot matches fully unit-testable.
/// </summary>
public sealed class BotPlayer
{
    private readonly BotDifficulty _diff;
    private readonly IRandomSource _rng;

    private double _timer;
    private bool _hasTarget;
    private bool _needHold;
    private bool _rotateSettled;
    private RotationState _targetState;
    private int _targetCol;
    private int _planSpawnId;

    public BotDifficulty Difficulty => _diff;

    public BotPlayer(BotDifficulty difficulty, IRandomSource? rng = null)
    {
        _diff = difficulty;
        _rng = rng ?? new XorShiftRandom(0xB07);
    }

    public void Reset()
    {
        _hasTarget = false;
        _needHold = false;
        _rotateSettled = false;
        _planSpawnId = 0;
        _timer = 0;
    }

    /// <summary>Advance the bot; it acts at most once per <see cref="BotDifficulty.ThinkInterval"/>.</summary>
    public void Update(double dt, Game game)
    {
        if (game.Status != GameStatus.Running || game.Current is null) return;

        _timer += dt;
        if (_timer < _diff.ThinkInterval) return;
        _timer = 0;

        // If the piece we planned for was locked by gravity/lock-delay (not our own
        // hard-drop), a different piece is now live — discard the stale plan.
        if (_hasTarget && game.SpawnCount != _planSpawnId) _hasTarget = false;

        if (!_hasTarget) PlanTarget(game);
        Act(game);
    }

    // ---- Planning ---------------------------------------------------------
    private readonly struct Placement
    {
        public readonly double Score;
        public readonly RotationState State;
        public readonly int Col;
        public readonly bool UseHold;
        public Placement(double score, RotationState state, int col, bool useHold)
        { Score = score; State = state; Col = col; UseHold = useHold; }
    }

    private void PlanTarget(Game game)
    {
        _planSpawnId = game.SpawnCount;
        var candidates = new List<Placement>(96);

        // For 2-ply lookahead we score the current placement PLUS the best placement
        // of the next queued piece on the resulting board.
        var preview = game.Preview();
        PieceType? next = preview.Count > 0 ? preview[0] : null;

        Collect(candidates, game.Board, game.Current!.Type, useHold: false, next);

        if (_diff.UseHold && game.CanHold)
        {
            var holdType = game.Hold == PieceType.Empty
                ? (preview.Count > 0 ? preview[0] : game.Current!.Type)
                : game.Hold;
            // The queued "next" differs when holding an empty slot; keep hold candidates 1-ply.
            Collect(candidates, game.Board, holdType, useHold: true, nextType: null);
        }

        if (candidates.Count == 0)
        {
            // Nowhere to go (board is choked) — just drop and accept the outcome.
            _hasTarget = true; _needHold = false; _rotateSettled = true;
            _targetState = game.Current!.State; _targetCol = game.Current!.Origin.Col;
            return;
        }

        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));

        int idx = 0;
        if (_diff.BlunderChance > 0 && candidates.Count > 1 && NextDouble() < _diff.BlunderChance)
            idx = 1 + _rng.Next(candidates.Count - 1); // deliberately imperfect

        var chosen = candidates[idx];
        _hasTarget = true;
        _needHold = chosen.UseHold;
        _rotateSettled = false;
        _targetState = chosen.State;
        _targetCol = chosen.Col;
    }

    private void Collect(List<Placement> into, Board board, PieceType type, bool useHold, PieceType? nextType)
    {
        bool lookahead = _diff.Lookahead && nextType is not null;
        for (int s = 0; s < Tetromino.StateCount; s++)
        {
            var state = (RotationState)s;
            var cells = Tetromino.Cells(type, state);
            int minC = int.MaxValue, maxC = int.MinValue;
            foreach (var c in cells) { if (c.Col < minC) minC = c.Col; if (c.Col > maxC) maxC = c.Col; }

            for (int originCol = -minC; originCol <= board.Width - 1 - maxC; originCol++)
            {
                var p = new Piece(type, state, new Vec2(0, originCol));
                if (!board.CanPlace(p)) continue; // can't even enter this column from the top
                var (landed, _) = board.HardDropTarget(p);
                double score = BotEvaluator.ScorePlacement(board, landed);
                if (lookahead)
                {
                    var resultBoard = BotEvaluator.ResultBoard(board, landed);
                    score += BotEvaluator.BestPlacementScore(resultBoard, nextType!.Value);
                }
                if (_diff.ScoreNoise > 0) score += (NextDouble() - 0.5) * _diff.ScoreNoise;
                into.Add(new Placement(score, state, originCol, useHold));
            }
        }
    }

    // ---- Execution (one action per tick, re-reading live state) -----------
    private void Act(Game game)
    {
        if (_needHold)
        {
            game.HoldPiece();
            _needHold = false;
            _planSpawnId = game.SpawnCount; // the swapped-in piece is the one the plan targets
            return; // keep the same target
        }

        var cur = game.Current!;

        if (!_rotateSettled && cur.State != _targetState)
        {
            int diff = (((int)_targetState - (int)cur.State) & 3);
            bool ok = diff switch
            {
                1 => game.RotateCw(),
                2 => game.Rotate180(),
                3 => game.RotateCcw(),
                _ => true,
            };
            // If the rotation couldn't apply (blocked) or we've reached the state, stop rotating.
            if (!ok || game.Current!.State == _targetState) _rotateSettled = true;
            return;
        }
        _rotateSettled = true;

        int col = game.Current!.Origin.Col;
        if (col != _targetCol)
        {
            bool moved = col < _targetCol ? game.MoveRight() : game.MoveLeft();
            if (!moved) DropAndReset(game); // wall/stack blocks the path — commit here
            return;
        }

        DropAndReset(game);
    }

    private void DropAndReset(Game game)
    {
        game.HardDrop();
        _hasTarget = false;
        _rotateSettled = false;
    }

    private double NextDouble() => _rng.Next(1_000_001) / 1_000_000.0;
}
