using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Blockfall.Core;

/// <summary>
/// Career totals accumulated across every run. Pure data + a single fold method,
/// so it serializes trivially and is unit-testable. Feeds the profile screen and
/// the achievement engine.
/// </summary>
public sealed class LifetimeStats
{
    public int GamesPlayed { get; set; }
    public long TotalScore { get; set; }
    public long TotalLines { get; set; }
    public long TotalPieces { get; set; }
    public double TotalPlaytime { get; set; } // seconds

    public int Singles { get; set; }
    public int Doubles { get; set; }
    public int Triples { get; set; }

    /// <summary>Four-line clears. Named for what the UI has always called them ("QUADS") rather
    /// than for the trademarked term — this property name is a KEY IN THE SAVE FILE, and CLAUDE.md
    /// §8-1 bans the mark from code and metadata alike. See <see cref="LegacyQuads"/> for how the
    /// counts already on players' devices survive the rename.</summary>
    public int Quads { get; set; }

    /// <summary>
    /// Read-only migration shim for saves written before <see cref="Quads"/> was renamed.
    ///
    /// <para>System.Text.Json binds by property name, so without this every existing player's
    /// four-line-clear total would silently reset to zero — and the "Quad Damage" achievement with
    /// it. The setter folds the old key in; the getter is never serialised
    /// (<see cref="JsonIgnoreCondition.WhenWritingDefault"/> plus a constant 0), so the old name is
    /// read once and never written back. Deleting this is safe only once no device can still hold a
    /// pre-rename save, which in practice means never.</para>
    /// </summary>
    [JsonPropertyName("Tetrises")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int LegacyQuads
    {
        get => 0;
        set => Quads = Math.Max(Quads, value);
    }

    public int TSpins { get; set; }
    public int TSpinMinis { get; set; }
    public int PerfectClears { get; set; }

    public int BestCombo { get; set; }
    public int BestBackToBack { get; set; }
    public double BestPps { get; set; }

    public Dictionary<string, int> ModeGames { get; set; } = new();
    public Dictionary<string, int> ModeCompletions { get; set; } = new();

    /// <summary>Accumulate one finished run into the career totals.</summary>
    public void Fold(GameModeId mode, RunStats run, long score, double time, bool completed)
    {
        GamesPlayed++;
        TotalScore += score;
        TotalLines += run.TotalLines;
        TotalPieces += run.PiecesPlaced;
        TotalPlaytime += time;

        Singles += run.Singles;
        Doubles += run.Doubles;
        Triples += run.Triples;
        Quads += run.Quads;
        TSpins += run.TSpins;
        TSpinMinis += run.TSpinMinis;
        PerfectClears += run.PerfectClears;

        if (run.MaxCombo > BestCombo) BestCombo = run.MaxCombo;
        if (run.MaxBackToBack > BestBackToBack) BestBackToBack = run.MaxBackToBack;
        if (run.PiecesPerSecond > BestPps) BestPps = run.PiecesPerSecond;

        string key = mode.ToString();
        ModeGames[key] = ModeGames.TryGetValue(key, out var g) ? g + 1 : 1;
        if (completed)
            ModeCompletions[key] = ModeCompletions.TryGetValue(key, out var c) ? c + 1 : 1;
    }

    public int GamesInMode(GameModeId mode) => ModeGames.TryGetValue(mode.ToString(), out var g) ? g : 0;
    public int CompletionsInMode(GameModeId mode) => ModeCompletions.TryGetValue(mode.ToString(), out var c) ? c : 0;
}
