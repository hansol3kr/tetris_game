namespace Blockfall.Core;

/// <summary>
/// Converts a line-clear outcome into "garbage lines sent" using a modern
/// guideline-style attack table. In versus this is damage; in Blockfall's solo
/// modes it drives the attack readout and, in Survival, cancels incoming garbage.
///
/// Pure and deterministic — a single static function over <see cref="ClearResult"/>,
/// so every row of the table is pinned by unit tests.
/// </summary>
public static class AttackTable
{
    /// <summary>Garbage lines this clear would send (0 if it cleared no lines).</summary>
    public static int LinesSent(ClearResult r)
    {
        if (r.LinesCleared == 0) return 0; // spins with no line clear send nothing

        int lines = BaseAttack(r.LinesCleared, r.Spin);
        if (r.BackToBack && lines > 0) lines += 1;   // back-to-back bonus
        lines += ComboBonus(r.ComboCount);           // combo adds to the attack
        if (r.PerfectClear) lines += 10;             // all-clear is a huge swing
        return lines;
    }

    /// <summary>Base attack before B2B / combo / perfect-clear modifiers.</summary>
    private static int BaseAttack(int lines, SpinType spin) => spin switch
    {
        SpinType.Full => lines switch { 1 => 2, 2 => 4, 3 => 6, _ => 0 }, // T-spin S/D/T
        SpinType.Mini => lines switch { 1 => 0, 2 => 1, _ => 0 },         // mini S/D
        _ => lines switch { 1 => 0, 2 => 1, 3 => 2, 4 => 4, _ => 0 },     // single..tetris
    };

    /// <summary>
    /// Combo bonus lines. <paramref name="comboCount"/> is 0 for the first clear in a
    /// chain (matching <see cref="ClearResult.ComboCount"/>), so a 2-chain is 1, etc.
    /// </summary>
    private static int ComboBonus(int comboCount) => comboCount switch
    {
        <= 0 => 0,
        1 or 2 => 1,
        3 or 4 => 2,
        5 or 6 => 3,
        7 or 8 or 9 => 4,
        _ => 5, // 10+ chain
    };
}
