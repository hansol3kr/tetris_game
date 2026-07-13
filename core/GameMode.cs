namespace Blockfall.Core;

public enum GameModeId
{
    Marathon, // clear lines, climb levels, endless-ish with a level cap
    Sprint,   // clear a fixed number of lines as fast as possible (time is score)
    Ultra,    // score as much as possible within a time limit
    Zen,      // relaxed, endless, no pressure
    Daily,    // 2-minute score attack on a seed shared by everyone that day
    Dig,      // clear a stack of pre-filled garbage as fast as possible
    Survival, // garbage rises over time; your clears cancel it — last as long as you can
    Master,   // 20G instant gravity + tight lock — pure execution
    Versus,   // 1v1 garbage battle against a CPU bot (or, later, a network peer)
}

public enum GameStatus
{
    Ready,
    Running,
    Paused,
    GameOver,   // topped out
    Completed,  // goal reached (Sprint lines / Ultra time / Marathon cap)
}

/// <summary>
/// Declarative description of a mode's rules. Adding a new mode is data, not code:
/// set the goal, gravity, and top-out behavior here.
/// </summary>
public sealed class GameMode
{
    public GameModeId Id { get; init; }
    public string Name { get; init; } = "Marathon";

    /// <summary>Sprint line goal (0 = not a line-goal mode).</summary>
    public int LineGoal { get; init; }

    /// <summary>Ultra time limit in seconds (0 = untimed).</summary>
    public double TimeLimit { get; init; }

    /// <summary>Marathon level cap that ends the run (0 = endless).</summary>
    public int LevelCap { get; init; }

    /// <summary>Starting level (affects gravity + score multiplier).</summary>
    public int StartLevel { get; init; } = 1;

    /// <summary>If false, topping out is ignored (used by fully relaxed modes).</summary>
    public bool CanTopOut { get; init; } = true;

    // ----- Garbage mechanics (Dig / Survival) ------------------------------
    /// <summary>Garbage rows pre-filled at start (Dig). 0 = none.</summary>
    public int InitialGarbage { get; init; }

    /// <summary>Complete the run once all garbage is cleared (Dig).</summary>
    public bool DigGoal { get; init; }

    /// <summary>Seconds between garbage rises (Survival). 0 = no rising garbage.</summary>
    public double GarbageRiseInterval { get; init; }

    /// <summary>How much the rise interval shortens after each rise (Survival ramp).</summary>
    public double GarbageRiseAccel { get; init; }

    /// <summary>Garbage lines queued per rise (Survival).</summary>
    public int GarbageRiseAmount { get; init; } = 1;

    public GameConfig Config { get; init; } = GameConfig.Default;

    /// <summary>Time-attack modes rank by lowest time (rather than highest score).</summary>
    public static bool IsTimeAttack(GameModeId id) => id is GameModeId.Sprint or GameModeId.Dig;

    /// <summary>
    /// Returns a copy of this mode with a different config (all other rules kept).
    /// Used to layer per-player handling settings onto a preset without mutating it.
    /// </summary>
    public GameMode WithConfig(GameConfig config) => new()
    {
        Id = Id,
        Name = Name,
        LineGoal = LineGoal,
        TimeLimit = TimeLimit,
        LevelCap = LevelCap,
        StartLevel = StartLevel,
        CanTopOut = CanTopOut,
        InitialGarbage = InitialGarbage,
        DigGoal = DigGoal,
        GarbageRiseInterval = GarbageRiseInterval,
        GarbageRiseAccel = GarbageRiseAccel,
        GarbageRiseAmount = GarbageRiseAmount,
        Config = config,
    };

    /// <summary>Evaluate whether the goal is met given current progress.</summary>
    public bool IsGoalReached(int linesCleared, int level, double elapsed)
    {
        if (LineGoal > 0 && linesCleared >= LineGoal) return true;
        if (TimeLimit > 0 && elapsed >= TimeLimit) return true;
        if (LevelCap > 0 && level > LevelCap) return true;
        return false;
    }

    // ----- Factory presets -------------------------------------------------
    public static GameMode Marathon => new()
    {
        Id = GameModeId.Marathon,
        Name = "Marathon",
        LevelCap = 15,
        StartLevel = 1,
        Config = GameConfig.Default,
    };

    public static GameMode Sprint40 => new()
    {
        Id = GameModeId.Sprint,
        Name = "Sprint 40",
        LineGoal = 40,
        StartLevel = 1,
        // Sprint uses a fixed, fast-but-fair gravity so the clock is the challenge.
        Config = new GameConfig { BaseGravity = 1.0, MaxGravityLevel = 0 },
    };

    public static GameMode Ultra => new()
    {
        Id = GameModeId.Ultra,
        Name = "Ultra 2:00",
        TimeLimit = 120.0,
        StartLevel = 1,
        Config = GameConfig.Default,
    };

    public static GameMode Zen => new()
    {
        Id = GameModeId.Zen,
        Name = "Zen",
        StartLevel = 1,
        CanTopOut = true, // board can still fill; there is simply no goal/clock
        Config = new GameConfig { BaseGravity = 1.2, MaxGravityLevel = 0 },
    };

    public static GameMode Daily => new()
    {
        Id = GameModeId.Daily,
        Name = "Daily Challenge",
        // Same shape as Ultra (bounded, score-based) so everyone's 2 minutes on the
        // shared daily seed are directly comparable. The seed is supplied per-day.
        TimeLimit = 120.0,
        StartLevel = 1,
        Config = GameConfig.Default,
    };

    public static GameMode DigRace => new()
    {
        Id = GameModeId.Dig,
        Name = "Dig Race",
        InitialGarbage = 10,       // ten messy rows to clear
        DigGoal = true,            // finished when the board is garbage-free
        StartLevel = 1,
        // Fixed, fair gravity — the clock is the challenge, like Sprint.
        Config = new GameConfig { BaseGravity = 1.0, MaxGravityLevel = 0 },
    };

    public static GameMode Survival => new()
    {
        Id = GameModeId.Survival,
        Name = "Survival",
        GarbageRiseInterval = 4.0, // a garbage line every 4s...
        GarbageRiseAccel = 0.05,   // ...accelerating relentlessly
        GarbageRiseAmount = 1,
        StartLevel = 1,
        Config = new GameConfig { BaseGravity = 1.0, MaxGravityLevel = 0 },
    };

    public static GameMode Master => new()
    {
        Id = GameModeId.Master,
        Name = "Master 20G",
        LevelCap = 0,              // endless; score is the reward
        StartLevel = 5,            // start fast
        // 20G: gravity is effectively instant, with a tight lock delay. All-spin on,
        // so immobile S/Z/L/J twists are rewarded — the whole point of the mode.
        Config = new GameConfig { MaxGravityLevel = 1, LockDelay = 0.3, MaxLockResets = 8, AllSpin = true },
    };

    public static GameMode Versus => new()
    {
        Id = GameModeId.Versus,
        Name = "Versus CPU",
        LevelCap = 0,   // endless — the match ends when someone tops out, not on a goal
        StartLevel = 1,
        CanTopOut = true,
        Config = GameConfig.Default,
    };

    public static GameMode ById(GameModeId id) => id switch
    {
        GameModeId.Marathon => Marathon,
        GameModeId.Sprint => Sprint40,
        GameModeId.Ultra => Ultra,
        GameModeId.Zen => Zen,
        GameModeId.Daily => Daily,
        GameModeId.Dig => DigRace,
        GameModeId.Survival => Survival,
        GameModeId.Master => Master,
        GameModeId.Versus => Versus,
        _ => Marathon,
    };
}
