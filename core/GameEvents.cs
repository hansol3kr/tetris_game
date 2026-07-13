namespace Blockfall.Core;

/// <summary>Fired when a piece locks; carries everything the presentation layer needs for juice.</summary>
public readonly struct LockEvent
{
    public Piece Piece { get; init; }
    public IReadOnlyList<int> ClearedRows { get; init; }
    public ClearResult Result { get; init; }
}

/// <summary>Result of a rotation attempt, including which kick made it fit (for T-spin + effects).</summary>
public readonly struct RotationEvent
{
    public bool Success { get; init; }
    public int KickIndex { get; init; }  // 0 = rotated in place, >0 = wall-kicked
    public RotationState NewState { get; init; }
}

public enum MoveKind { Left, Right, SoftStep }

/// <summary>Cumulative per-run statistics for the results screen and leaderboards.</summary>
public sealed class RunStats
{
    public int PiecesPlaced { get; internal set; }
    public int Singles { get; internal set; }
    public int Doubles { get; internal set; }
    public int Triples { get; internal set; }
    public int Quads { get; internal set; }      // 4-line clears (Tetrises)
    public int TSpins { get; internal set; }
    public int TSpinMinis { get; internal set; }
    public int PerfectClears { get; internal set; }
    public int MaxCombo { get; internal set; }
    public int MaxBackToBack { get; internal set; }
    public int TotalLines { get; internal set; }
    public double FinishTime { get; internal set; }

    private int _currentB2B;

    internal void Record(ClearResult r)
    {
        switch (r.LinesCleared)
        {
            case 1: Singles++; break;
            case 2: Doubles++; break;
            case 3: Triples++; break;
            case 4: Quads++; break;
        }
        if (r.Spin == SpinType.Full && r.LinesCleared > 0) TSpins++;
        if (r.Spin == SpinType.Mini && r.LinesCleared > 0) TSpinMinis++;
        if (r.PerfectClear) PerfectClears++;
        if (r.ComboCount > MaxCombo) MaxCombo = r.ComboCount;
        TotalLines += r.LinesCleared;

        if (r.BackToBack) { _currentB2B++; if (_currentB2B > MaxBackToBack) MaxBackToBack = _currentB2B; }
        else if (r.LinesCleared > 0 && !r.IsDifficult) _currentB2B = 0;
    }

    /// <summary>Pieces per second — the key speed metric for Sprint leaderboards.</summary>
    public double PiecesPerSecond => FinishTime > 0 ? PiecesPlaced / FinishTime : 0;
}
