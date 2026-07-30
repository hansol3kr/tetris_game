using System;
using System.Collections.Generic;

namespace Blockfall.Core;

/// <summary>Everything an achievement predicate can look at: career totals + the run that just finished.</summary>
public readonly struct AchievementContext
{
    public LifetimeStats Lifetime { get; init; }
    public GameModeId Mode { get; init; }
    public RunStats Run { get; init; }
    public long Score { get; init; }
    public double Time { get; init; }
    public bool Completed { get; init; }
}

public sealed class AchievementDef
{
    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public Func<AchievementContext, bool> Check { get; }

    public AchievementDef(string id, string name, string description, Func<AchievementContext, bool> check)
    {
        Id = id;
        Name = name;
        Description = description;
        Check = check;
    }
}

/// <summary>The fixed set of achievements. Adding one is a single entry here.</summary>
public static class AchievementCatalog
{
    /// <summary>
    /// Ids that were RENAMED after shipping, old → new. An unlocked achievement is persisted as its
    /// id string, so renaming one without this map would silently re-lock it on every device that
    /// had earned it.
    ///
    /// <para>"first_tetris" was renamed because the id is written into the save file and CLAUDE.md
    /// §8-1 bans the mark from code and metadata alike — the achievement's player-facing name was
    /// always "Quad Damage", so nothing the player ever saw changes.</para>
    ///
    /// <para>Append-only, and entries are permanent: an old id can be on a device forever, so a row
    /// here can never be deleted, only added to.</para>
    /// </summary>
    private static readonly Dictionary<string, string> RenamedIds = new()
    {
        ["first_tetris"] = "first_quad",
    };

    /// <summary>
    /// Translate a persisted achievement id to its current name, leaving unknown ids untouched.
    /// Call this when LOADING a save (and when merging a cloud one), never when writing.
    /// <para>Unknown ids pass through rather than being dropped: an id from a NEWER build that a
    /// downgraded client does not recognise is still that player's unlock, and discarding it would
    /// lose it permanently on the next write.</para>
    /// </summary>
    public static string MigrateId(string id) => RenamedIds.TryGetValue(id, out var now) ? now : id;

    /// <summary>Rewrite a persisted unlock list in place: migrate every id, drop duplicates that
    /// the migration collapses, and preserve order. Returns true if anything changed, so a caller
    /// can mark the save dirty only when it needs re-writing.</summary>
    public static bool MigrateUnlocked(List<string> unlocked)
    {
        bool changed = false;
        var seen = new HashSet<string>();
        var outp = new List<string>(unlocked.Count);
        foreach (var id in unlocked)
        {
            var now = MigrateId(id);
            if (now != id) changed = true;
            if (seen.Add(now)) outp.Add(now); else changed = true;
        }
        if (!changed) return false;
        unlocked.Clear();
        unlocked.AddRange(outp);
        return true;
    }

    public static readonly IReadOnlyList<AchievementDef> All = new List<AchievementDef>
    {
        new("first_line",    "First Steps",     "Clear your first line.",            c => c.Lifetime.TotalLines >= 1),
        new("first_quad",    "Quad Damage",     "Clear four lines at once.",         c => c.Lifetime.Quads >= 1),
        new("first_tspin",   "Spin Doctor",     "Score your first T-spin.",          c => c.Lifetime.TSpins >= 1),
        new("perfect",       "Spotless",        "Land a Perfect Clear.",             c => c.Lifetime.PerfectClears >= 1),
        new("combo_5",       "Chain Reaction",  "Reach a 5+ combo.",                 c => c.Lifetime.BestCombo >= 5),
        new("combo_10",      "Unbroken",        "Reach a 10+ combo.",                c => c.Lifetime.BestCombo >= 10),
        new("b2b_5",         "Back to Back to…","Keep a back-to-back chain of 5.",   c => c.Lifetime.BestBackToBack >= 5),
        new("marathon",      "The Long Haul",   "Complete Marathon.",                c => c is { Mode: GameModeId.Marathon, Completed: true }),
        new("sprint_sub2",   "Sprinter",        "Finish Sprint 40 under 2:00.",      c => c is { Mode: GameModeId.Sprint, Completed: true } && c.Time < 120),
        new("sprint_sub1",   "Blur",            "Finish Sprint 40 under 1:00.",      c => c is { Mode: GameModeId.Sprint, Completed: true } && c.Time < 60),
        new("ultra_10k",     "High Roller",     "Score 10,000+ in Ultra.",           c => c.Mode == GameModeId.Ultra && c.Score >= 10000),
        new("survivor",      "Survivor",        "Last 2:00 in Survival.",            c => c.Mode == GameModeId.Survival && c.Time >= 120),
        new("dig",           "Excavator",       "Complete Dig Race.",                c => c is { Mode: GameModeId.Dig, Completed: true }),
        new("speed_2pps",    "Fast Hands",      "Average 2+ pieces per second.",     c => c.Run.PiecesPerSecond >= 2.0),
        new("lines_1000",    "Line Cook",       "Clear 1,000 lines total.",          c => c.Lifetime.TotalLines >= 1000),
        new("tspin_50",      "Twist King",      "Score 50 T-spins total.",           c => c.Lifetime.TSpins >= 50),
        new("centurion",     "Centurion",       "Play 100 games.",                   c => c.Lifetime.GamesPlayed >= 100),
    };

    public static AchievementDef? ById(string id)
    {
        foreach (var a in All) if (a.Id == id) return a;
        return null;
    }
}

public static class AchievementEngine
{
    /// <summary>
    /// Returns the ids of achievements newly satisfied by this context that are
    /// not already in <paramref name="unlocked"/>. The caller persists them.
    /// </summary>
    public static IReadOnlyList<string> Evaluate(ICollection<string> unlocked, AchievementContext ctx)
    {
        var fresh = new List<string>();
        foreach (var def in AchievementCatalog.All)
        {
            if (unlocked.Contains(def.Id)) continue;
            bool ok;
            try { ok = def.Check(ctx); }
            catch { ok = false; } // a bad predicate must never break a run's results
            if (ok) fresh.Add(def.Id);
        }
        return fresh;
    }
}
