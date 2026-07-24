using System;
using System.Collections.Generic;

namespace Blockfall.Core.BlockFit;

/// <summary>
/// Heuristic scorer + move picker for a Block Fit placement bot. Deterministic given the
/// same RNG. It prefers placements that clear lines, keep the board emptier (there is no
/// gravity, so open space is the resource), and avoid trapping fully-enclosed empty cells.
/// The Block Fit analogue of <see cref="BotEvaluator"/> for the falling game.
/// </summary>
public static class BlockFitEvaluator
{
    /// <summary>Pick a placement for the current tray. Returns false if nothing fits at all.
    /// Applies the difficulty's score jitter, and occasionally a deliberate blunder.</summary>
    public static bool BestPlacement(BlockFitGame g, Random rng, BotDifficulty diff,
        out int trayIndex, out int row, out int col)
    {
        trayIndex = -1; row = -1; col = -1;
        int n = BlockFitGame.Size;
        var occ = Snapshot(g, n);

        double best = double.NegativeInfinity;
        var candidates = new List<(int ti, int r, int c)>();
        for (int ti = 0; ti < 3; ti++)
        {
            var p = g.Tray[ti];
            if (p is null) continue;
            for (int r = 0; r <= n - p.Height; r++)
                for (int c = 0; c <= n - p.Width; c++)
                {
                    if (!Fits(occ, p, r, c, n)) continue;
                    candidates.Add((ti, r, c));
                    double s = Score(occ, p, r, c, n) + Jitter(rng, diff.ScoreNoise);
                    if (s > best) { best = s; trayIndex = ti; row = r; col = c; }
                }
        }
        if (trayIndex < 0) return false;

        // Deliberate blunder: sometimes take a random legal move instead of the best one.
        if (rng != null && candidates.Count > 1 && rng.NextDouble() < diff.BlunderChance)
        {
            var pick = candidates[rng.Next(candidates.Count)];
            trayIndex = pick.ti; row = pick.r; col = pick.c;
        }
        return true;
    }

    private static double Jitter(Random rng, double noise)
        => rng is null || noise <= 0 ? 0 : (rng.NextDouble() * 2 - 1) * noise;

    private static bool[] Snapshot(BlockFitGame g, int n)
    {
        var occ = new bool[n * n];
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
                occ[r * n + c] = g.At(r, c) != PieceType.Empty;
        return occ;
    }

    private static bool Fits(bool[] occ, BlockPiece p, int row, int col, int n)
    {
        foreach (var (dr, dc) in p.Cells)
        {
            int r = row + dr, c = col + dc;
            if (r < 0 || r >= n || c < 0 || c >= n) return false;
            if (occ[r * n + c]) return false;
        }
        return true;
    }

    /// <summary>Score a legal placement: clears are strongly rewarded, a fuller board is
    /// mildly penalised, and fully-enclosed empty cells (hard to ever fill) are heavily so.</summary>
    private static double Score(bool[] occ, BlockPiece p, int row, int col, int n)
    {
        var g = (bool[])occ.Clone();
        foreach (var (dr, dc) in p.Cells) g[(row + dr) * n + (col + dc)] = true;

        // Clear any full rows/columns the placement completed.
        int cleared = 0;
        Span<bool> clearRow = stackalloc bool[BlockFitGame.Size];
        Span<bool> clearCol = stackalloc bool[BlockFitGame.Size];
        for (int r = 0; r < n; r++)
        {
            bool full = true;
            for (int c = 0; c < n; c++) if (!g[r * n + c]) { full = false; break; }
            if (full) { clearRow[r] = true; cleared++; }
        }
        for (int c = 0; c < n; c++)
        {
            bool full = true;
            for (int r = 0; r < n; r++) if (!g[r * n + c]) { full = false; break; }
            if (full) { clearCol[c] = true; cleared++; }
        }
        for (int r = 0; r < n; r++) if (clearRow[r]) for (int c = 0; c < n; c++) g[r * n + c] = false;
        for (int c = 0; c < n; c++) if (clearCol[c]) for (int r = 0; r < n; r++) g[r * n + c] = false;

        int filled = 0, holes = 0;
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
            {
                if (g[r * n + c]) { filled++; continue; }
                bool up = r == 0 || g[(r - 1) * n + c];
                bool dn = r == n - 1 || g[(r + 1) * n + c];
                bool lf = c == 0 || g[r * n + (c - 1)];
                bool rt = c == n - 1 || g[r * n + (c + 1)];
                if (up && dn && lf && rt) holes++;   // trapped empty — very hard to use later
            }
        return cleared * 40.0 - filled * 0.5 - holes * 6.0;
    }
}

/// <summary>
/// A Block Fit CPU opponent. On a difficulty-paced interval it picks a placement via
/// <see cref="BlockFitEvaluator"/> and commits it in one shot (no gravity/rotation
/// sub-steps, unlike the falling <c>BotPlayer</c>). <see cref="Update"/> returns the lines
/// it cleared that tick so the match can convert them into an attack.
/// </summary>
public sealed class BlockFitBot
{
    private readonly BotDifficulty _diff;
    private readonly Random _rng;
    private readonly double _interval;
    private double _timer;

    public BlockFitBot(BotDifficulty diff, int seed)
    {
        _diff = diff;
        _rng = new Random(seed);
        // For the falling bot, ThinkInterval is per-input and a piece takes several inputs.
        // A Block Fit placement equals a whole piece, so stretch it into a per-placement pace
        // (≈0.95s Easy … ≈0.40s Grandmaster).
        _interval = 0.35 + diff.ThinkInterval * 3.0;
    }

    public int Update(double dt, BlockFitGame game)
    {
        if (game.GameOver) return 0;
        _timer += dt;
        if (_timer < _interval) return 0;
        _timer = 0;
        if (!BlockFitEvaluator.BestPlacement(game, _rng, _diff, out int ti, out int r, out int c))
            return 0;
        game.TryPlace(ti, r, c);
        return game.LastClearedRows + game.LastClearedCols;
    }
}

/// <summary>
/// A live Block Fit duel: two independent <see cref="BlockFitGame"/> boards — the player and
/// a <see cref="BlockFitBot"/>. Clearing lines sends garbage blockers to the opponent; the
/// first side that can no longer place any tray piece loses. Like <see cref="VersusMatch"/>
/// this is a LIVE match (the player drives their board directly), so it carries no
/// replay/determinism contract and both boards use the game RNG freely.
/// </summary>
public sealed class BlockFitVersus
{
    public BlockFitGame PlayerGame { get; }
    public BlockFitGame BotGame { get; }
    private readonly BlockFitBot _bot;
    private readonly int _attackPerLine;

    public VersusSide? Winner { get; private set; }
    public bool IsOver => Winner.HasValue;
    public event Action<VersusSide>? MatchEnded;

    /// <summary>Garbage cells the most recent attack sent to each side (drives meter feedback).</summary>
    public int PlayerLastHit { get; private set; }
    public int BotLastHit { get; private set; }

    /// <summary>Fires with the garbage count when the player is hit (the bot cleared).</summary>
    public event Action<int>? PlayerHit;
    /// <summary>Fires with the garbage count when the bot is hit (the player cleared).</summary>
    public event Action<int>? BotHit;

    public BlockFitVersus(BotDifficulty difficulty, int seed, int attackPerLine = 2)
    {
        PlayerGame = new BlockFitGame(seed == 0 ? 1 : seed);
        BotGame = new BlockFitGame(seed == 0 ? 7 : unchecked(seed + 12345));
        _bot = new BlockFitBot(difficulty, unchecked(seed + 999));
        _attackPerLine = attackPerLine;
    }

    /// <summary>Test seam: inject both boards (rig the player's for a deterministic clear).</summary>
    public BlockFitVersus(BlockFitGame player, BlockFitGame bot, BotDifficulty difficulty, int attackPerLine = 2)
    {
        PlayerGame = player;
        BotGame = bot;
        _bot = new BlockFitBot(difficulty, 1);
        _attackPerLine = attackPerLine;
    }

    /// <summary>Commit a player placement; garbage flows to the bot on a clear. Returns lines cleared.</summary>
    public int PlayerPlace(int trayIndex, int row, int col)
    {
        if (IsOver || !PlayerGame.TryPlace(trayIndex, row, col)) return 0;
        int lines = PlayerGame.LastClearedRows + PlayerGame.LastClearedCols;
        if (lines > 0) { BotLastHit = _attackPerLine * lines; BotGame.AddGarbage(BotLastHit); BotHit?.Invoke(BotLastHit); }
        CheckEnd();
        return lines;
    }

    /// <summary>Commit a player tray merge (no attack — merging clears nothing).</summary>
    public bool PlayerMerge(int srcIndex, int dstIndex)
    {
        if (IsOver) return false;
        bool ok = PlayerGame.TryMerge(srcIndex, dstIndex);
        if (ok) CheckEnd();
        return ok;
    }

    /// <summary>Advance the bot; garbage flows to the player when the bot clears.</summary>
    public void Update(double dt)
    {
        if (IsOver) return;
        int botCleared = _bot.Update(dt, BotGame);
        if (botCleared > 0) { PlayerLastHit = _attackPerLine * botCleared; PlayerGame.AddGarbage(PlayerLastHit); PlayerHit?.Invoke(PlayerLastHit); }
        CheckEnd();
    }

    private void CheckEnd()
    {
        if (IsOver) return;
        if (PlayerGame.GameOver) End(VersusSide.Bot);
        else if (BotGame.GameOver) End(VersusSide.Player);
    }

    private void End(VersusSide winner)
    {
        Winner = winner;
        MatchEnded?.Invoke(winner);
    }
}
