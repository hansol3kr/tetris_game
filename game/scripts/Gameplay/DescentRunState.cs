using System.Collections.Generic;
using Blockfall.Core;

namespace Blockfall.Gameplay;

/// <summary>Per-stage outcome kept for the run results screen (depth bars).</summary>
public readonly struct StageOutcome
{
    public int Stratum { get; init; }
    public StageKind Kind { get; init; }
    public long Score { get; init; }
    public int Lines { get; init; }
    public double Time { get; init; }
    public bool Completed { get; init; }
}

/// <summary>
/// Carries one Descent run between screens (stage → draft → stage → … → results).
/// Wraps the pure-core <see cref="DescentRun"/> ledger with presentation-side
/// bookkeeping: per-stage outcomes for the results screen and the aggregated
/// <see cref="RunStats"/> for the single RecordRun fired at the END of the run —
/// a stage is not a run, so stages never touch records individually.
/// </summary>
public sealed class DescentRunState
{
    public DescentRun Run { get; }
    public List<StageOutcome> Outcomes { get; } = new();
    /// <summary>All stages folded into one run summary (career fold + achievements).</summary>
    public RunStats TotalStats { get; } = new();

    public DescentRunState(ulong seed) => Run = new DescentRun(seed);

    /// <summary>
    /// Record a finished stage and advance the core ledger: a cleared stage banks
    /// its score (plus the depth bonus); a failed stage ends the run with the bank
    /// intact. Call exactly once per stage, before any draft.
    /// </summary>
    public void RecordStage(RunResults results)
    {
        Outcomes.Add(new StageOutcome
        {
            Stratum = Run.CurrentStage.Stratum,
            Kind = Run.CurrentStage.Kind,
            Score = results.Score,
            Lines = results.Lines,
            Time = results.Time,
            Completed = results.Completed,
        });
        TotalStats.Accumulate(results.Stats);
        if (results.Completed) Run.CompleteStage(results.Score);
        else Run.FailStage();
    }

    /// <summary>
    /// Record a mid-stage quit: the abandoned stratum shows as a failed row with
    /// its score forfeit (mirroring how a top-out run lists its fatal stratum),
    /// then the run ends with the bank intact.
    /// </summary>
    public void RecordAbandon()
    {
        if (Run.Finished) return;
        Outcomes.Add(new StageOutcome
        {
            Stratum = Run.CurrentStage.Stratum,
            Kind = Run.CurrentStage.Kind,
            Score = 0,
            Lines = 0,
            Time = 0,
            Completed = false,
        });
        Run.FailStage();
    }
}
