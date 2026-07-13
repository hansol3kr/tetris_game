namespace Blockfall.Core;

/// <summary>
/// Anti-cheat substrate for ranked leaderboards: re-simulates a <see cref="ReplayData"/>
/// from its seed + input stream and confirms the result it claims. A submitted score
/// is only trustworthy if its replay actually reproduces it — you cannot forge a high
/// score without a legal sequence of inputs that yields it. Pure and deterministic, so
/// the same check runs identically on the client and on a validation server.
/// </summary>
public static class ReplayValidator
{
    public readonly struct Result
    {
        public bool Valid { get; init; }
        public long ActualScore { get; init; }
        public int ActualLines { get; init; }
        public double ActualTime { get; init; }
        public string Reason { get; init; }

        public static Result Fail(string reason) => new() { Valid = false, Reason = reason };
    }

    /// <summary>Re-simulate and confirm the replay's own claimed score/lines are genuine.</summary>
    public static Result Validate(ReplayData data)
    {
        if (data.Version != ReplayData.CurrentVersion)
            return Result.Fail($"unsupported replay version {data.Version}");
        if (data.Inputs.Length == 0)
            return Result.Fail("empty replay");

        Game g;
        try
        {
            g = new ReplayPlayer(data).PlayToEnd();
        }
        catch (System.Exception e)
        {
            return Result.Fail("replay failed to simulate: " + e.Message);
        }

        long actualScore = g.Scoring.Score;
        int actualLines = g.Scoring.LinesCleared;
        double actualTime = g.Elapsed;

        bool ok = actualScore == data.FinalScore && actualLines == data.FinalLines;
        return new Result
        {
            Valid = ok,
            ActualScore = actualScore,
            ActualLines = actualLines,
            ActualTime = actualTime,
            Reason = ok ? "ok" : $"claimed {data.FinalScore}/{data.FinalLines}, re-sim gave {actualScore}/{actualLines}",
        };
    }

    /// <summary>True only if the replay is authentic AND reproduces the externally-claimed score.</summary>
    public static bool IsAuthentic(ReplayData data, long claimedScore)
    {
        var r = Validate(data);
        return r.Valid && r.ActualScore == claimedScore;
    }
}
