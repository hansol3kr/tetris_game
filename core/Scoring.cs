namespace Blockfall.Core;

/// <summary>How a piece was spun into place, used for T-spin scoring.</summary>
public enum SpinType
{
    None,
    Mini,   // mini T-spin (fewer points)
    Full,   // proper T-spin
}

/// <summary>The outcome of locking a single piece; drives scoring, effects, and stats.</summary>
public readonly struct ClearResult
{
    public int LinesCleared { get; init; }
    public SpinType Spin { get; init; }
    public bool BackToBack { get; init; }   // this clear continued a B2B chain
    public int ComboCount { get; init; }    // 0 for the first clear in a chain
    public bool PerfectClear { get; init; } // board fully emptied
    public int ScoreGained { get; init; }
    public int LevelAfter { get; init; }

    /// <summary>A "difficult" clear (Tetris or any spin line clear) that sustains B2B.</summary>
    public bool IsDifficult => LinesCleared == 4 || (Spin != SpinType.None && LinesCleared > 0);
}

/// <summary>
/// Stateful scorer implementing guideline scoring: line clears, T-spins,
/// back-to-back (x1.5), combos, soft/hard drop, and perfect-clear bonuses.
/// </summary>
public sealed class Scoring
{
    public long Score { get; private set; }
    public int LinesCleared { get; private set; }
    public int Level { get; private set; } = 1;
    public int ComboCounter { get; private set; } = -1; // -1 means "no active combo"
    public bool BackToBackActive { get; private set; }

    private readonly int _startLevel;
    private readonly double _scoreMultiplier;

    public Scoring(int startLevel = 1, double scoreMultiplier = 1.0)
    {
        _startLevel = Math.Max(1, startLevel);
        Level = _startLevel;
        _scoreMultiplier = scoreMultiplier;
    }

    public void AddSoftDrop(int cells) => Score += cells; // 1 pt/cell
    public void AddHardDrop(int cells) => Score += cells * 2; // 2 pt/cell

    /// <summary>
    /// Applies a lock outcome to the running score. <paramref name="lines"/> is the
    /// number of lines cleared, <paramref name="spin"/> the detected spin, and
    /// <paramref name="perfectClear"/> whether the board is now empty.
    /// </summary>
    public ClearResult ApplyLock(int lines, SpinType spin, bool perfectClear)
    {
        // Combo tracking: increments on each consecutive line-clearing lock, resets otherwise.
        if (lines > 0) ComboCounter++;
        else ComboCounter = -1;

        int basePoints = BaseScore(lines, spin);
        bool difficult = lines == 4 || (spin != SpinType.None && lines > 0);

        // Back-to-back applies to difficult clears that follow another difficult clear.
        bool b2bThisClear = false;
        if (lines > 0)
        {
            if (difficult)
            {
                if (BackToBackActive) b2bThisClear = true;
                BackToBackActive = true;
            }
            else
            {
                BackToBackActive = false; // a normal (non-difficult) line clear breaks the chain
            }
        }
        // Note: a spin with 0 lines does NOT break B2B (guideline behavior).

        double points = basePoints;
        if (b2bThisClear) points *= 1.5;

        // Combo bonus: 50 * comboCount * level (comboCount is 0 on the first clear).
        int combo = ComboCounter;
        if (combo > 0) points += 50.0 * combo * Level;

        // Perfect clear (all-clear) bonuses stack on top.
        if (perfectClear && lines > 0)
            points += PerfectClearBonus(lines, b2bThisClear) ;

        // Descent charm multiplier scales the whole lock gain exactly once.
        // Guarded so legacy modes (multiplier 1.0) keep bit-identical scores.
        if (_scoreMultiplier != 1.0) points *= _scoreMultiplier;

        int gained = (int)Math.Round(points);
        Score += gained;

        // Level & line progression (10 lines per level in Marathon-style modes).
        if (lines > 0)
        {
            LinesCleared += lines;
            Level = _startLevel + LinesCleared / 10;
        }

        return new ClearResult
        {
            LinesCleared = lines,
            Spin = spin,
            BackToBack = b2bThisClear,
            ComboCount = combo < 0 ? 0 : combo,
            PerfectClear = perfectClear && lines > 0,
            ScoreGained = gained,
            LevelAfter = Level,
        };
    }

    private int BaseScore(int lines, SpinType spin)
    {
        int lvl = Level;
        if (spin == SpinType.Full)
        {
            return lines switch
            {
                0 => 400 * lvl,
                1 => 800 * lvl,
                2 => 1200 * lvl,
                3 => 1600 * lvl,
                _ => 1600 * lvl,
            };
        }
        if (spin == SpinType.Mini)
        {
            return lines switch
            {
                0 => 100 * lvl,
                1 => 200 * lvl,
                2 => 400 * lvl, // rare (mini double via I/kick edge cases)
                _ => 400 * lvl,
            };
        }
        return lines switch
        {
            0 => 0,
            1 => 100 * lvl,
            2 => 300 * lvl,
            3 => 500 * lvl,
            4 => 800 * lvl, // "quad" / Tetris
            _ => 800 * lvl,
        };
    }

    private int PerfectClearBonus(int lines, bool b2b)
    {
        // Guideline perfect-clear values (per level), with a B2B quad PC bonus.
        int lvl = Level;
        int baseBonus = lines switch
        {
            1 => 800 * lvl,
            2 => 1200 * lvl,
            3 => 1800 * lvl,
            4 => b2b ? 3200 * lvl : 2000 * lvl,
            _ => 2000 * lvl,
        };
        return baseBonus;
    }

    public void Reset()
    {
        Score = 0;
        LinesCleared = 0;
        Level = _startLevel;
        ComboCounter = -1;
        BackToBackActive = false;
    }

    /// <summary>Break the combo/B2B chain only (revive) — score/lines/level stay.</summary>
    public void ResetChain()
    {
        ComboCounter = -1;
        BackToBackActive = false;
    }
}
