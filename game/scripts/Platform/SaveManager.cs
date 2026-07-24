using Godot;
using System.Collections.Generic;
using System.Text.Json;
using Blockfall.Core;
using Blockfall.Core.Localization;

namespace Blockfall.Platform;

/// <summary>Player-tunable settings persisted across sessions.</summary>
public sealed class GameSettings
{
    public float SfxVolume { get; set; } = 0.9f;
    public float MusicVolume { get; set; } = 0.7f;
    public bool Muted { get; set; }
    public bool GhostEnabled { get; set; } = true;
    public double DasSeconds { get; set; } = 0.133;
    public double ArrSeconds { get; set; } = 0.02;
    public bool AdsRemoved { get; set; }

    /// <summary>Use the high-distinction (deuteranopia/protanopia-friendly) piece palette.</summary>
    public bool ColorblindMode { get; set; }

    /// <summary>Screen-shake / particle intensity, 0 = off … 1 = full.</summary>
    public float JuiceIntensity { get; set; } = 1.0f;

    /// <summary>Line-clear effect style — index into JuiceLayer.ClearFxNames.</summary>
    public int ClearFxStyle { get; set; }

    /// <summary>Show the finesse (input-economy) readout during play and on the results screen.</summary>
    public bool ShowFinesse { get; set; } = true;

    /// <summary>Reduced motion: no entrance slides/scale punches/background drift — short fades only.</summary>
    public bool ReducedMotion { get; set; }

    /// <summary>Neon bloom (HDR 2D). Off = the low-end-device fallback look.</summary>
    public bool GlowEnabled { get; set; } = true;

    /// <summary>Last mode launched from the menu — drives the hero PLAY card.</summary>
    public string LastPlayedMode { get; set; } = "";

    /// <summary>Has the player finished (or skipped) the how-to-play tutorial?</summary>
    public bool TutorialDone { get; set; }

    /// <summary>
    /// Touch control scheme. True = direct manipulation (drag the piece into place,
    /// tap to rotate, flick to drop); false = the classic on-screen d-pad buttons.
    /// Ignored on devices without a touchscreen (keyboard/gamepad always work).
    /// </summary>
    public bool GestureControls { get; set; } = true;

    /// <summary>UI language. Persisted as an int; append-only enum keeps it stable.</summary>
    public Language Language { get; set; } = Language.English;

    /// <summary>Custom keyboard bindings: input-action name → physical keycode.
    /// Actions absent from the map use their built-in default. Gamepad bindings
    /// are never remapped here.</summary>
    public Dictionary<string, long> KeyBindings { get; set; } = new();

    /// <summary>Accessibility text scale (1.0 = default). Multiplies UI font sizes
    /// only — the play area is unaffected, so the board never clips.</summary>
    public float TextScale { get; set; } = 1.0f;

    /// <summary>Desktop window mode. Ignored on mobile (always fullscreen there).</summary>
    public bool Fullscreen { get; set; }
}

/// <summary>Serializable save payload (best scores + settings + store state).</summary>
internal sealed class SaveData
{
    public Dictionary<string, double> Best { get; set; } = new();
    /// <summary>Best daily-challenge score keyed by "yyyy-MM-dd" (UTC).</summary>
    public Dictionary<string, double> Daily { get; set; } = new();
    public GameSettings Settings { get; set; } = new();

    /// <summary>Store: purchased item ids (themes, packs — not consumable counts).</summary>
    public List<string> OwnedItems { get; set; } = new();
    /// <summary>Store: the equipped cosmetic theme's item id.</summary>
    public string EquippedTheme { get; set; } = "theme_neon_flux";
    /// <summary>Store: the equipped Block Fit burst-FX artifact's item id.</summary>
    public string EquippedArtifact { get; set; } = "artifact_sparks";
    /// <summary>Store: consumable booster counts by booster id.</summary>
    public Dictionary<string, int> Boosters { get; set; } = new();

    /// <summary>Progression: career totals folded from every run.</summary>
    public LifetimeStats Lifetime { get; set; } = new();
    /// <summary>Progression: unlocked achievement ids.</summary>
    public List<string> Achievements { get; set; } = new();
    /// <summary>Progression: per-mode local leaderboards (mode name -> top entries).</summary>
    public Dictionary<string, List<LeaderboardEntry>> Leaderboards { get; set; } = new();

    /// <summary>Online: competitive ladder rating for ranked (Quick Match) duels.</summary>
    public RankRating Rank { get; set; } = new();
}

/// <summary>
/// Local persistence via Godot's user:// sandbox. Stores best score/time per
/// mode and player settings. Writes are debounced through <see cref="Flush"/>,
/// which is also called when the app backgrounds (see Bootstrap). Cloud sync
/// (Steam Cloud / mobile) layers on top of this in the platform implementations.
/// </summary>
public partial class SaveManager : Node
{
    private const string Path = "user://blockfall_save.json";
    private SaveData _data = new();
    private bool _dirty;

    public GameSettings Settings => _data.Settings;

    /// <summary>True when no save file existed at load — used to pick first-run,
    /// platform-specific setting defaults (e.g. glow off on mobile) that the player
    /// can still override afterwards.</summary>
    public bool FreshInstall { get; private set; }

    public override void _Ready() => Load();

    public double? GetBest(GameModeId mode)
        => _data.Best.TryGetValue(mode.ToString(), out var v) ? v : null;

    /// <summary>Best score in the free-placement (Block Fit) mode — persisted in the
    /// same Best table under a reserved key, so no schema change is needed.</summary>
    public double BlockFitBest
    {
        get => _data.Best.TryGetValue("__blockfit", out var v) ? v : 0;
        set { _data.Best["__blockfit"] = value; _dirty = true; Flush(); }
    }

    /// <summary>
    /// Records a run and returns true if it beat the stored best. For Sprint the
    /// best is the LOWEST time; for score modes it's the HIGHEST score.
    /// </summary>
    public bool SubmitResult(GameModeId mode, long score, double time, int lines, bool completed)
    {
        // Time-attack modes (Sprint, Dig) rank by lowest time and only count when the
        // goal was actually reached; score modes rank by highest score.
        bool timeAttack = GameMode.IsTimeAttack(mode);
        double metric = timeAttack ? time : score;
        string key = mode.ToString();

        bool isBest;
        if (!_data.Best.TryGetValue(key, out var existing))
            isBest = timeAttack ? completed : score > 0;
        else
            isBest = timeAttack ? (completed && metric < existing) : metric > existing;

        if (isBest)
        {
            _data.Best[key] = metric;
            _dirty = true;
            Flush();
        }
        return isBest;
    }

    /// <summary>Best score recorded for a given daily-challenge date key ("yyyy-MM-dd").</summary>
    public double? GetDailyBest(string dateKey)
        => _data.Daily.TryGetValue(dateKey, out var v) ? v : null;

    /// <summary>Records a daily-challenge score; returns true if it beat today's stored best.</summary>
    public bool SubmitDaily(string dateKey, long score)
    {
        bool isBest = !_data.Daily.TryGetValue(dateKey, out var existing) ? score > 0 : score > existing;
        if (isBest)
        {
            _data.Daily[dateKey] = score;
            // Keep the map small: retain only the most recent handful of days.
            PruneDaily(keep: 14);
            _dirty = true;
            Flush();
        }
        return isBest;
    }

    private void PruneDaily(int keep)
    {
        if (_data.Daily.Count <= keep) return;
        var keys = new List<string>(_data.Daily.Keys);
        keys.Sort(System.StringComparer.Ordinal); // "yyyy-MM-dd" sorts chronologically
        for (int i = 0; i < keys.Count - keep; i++) _data.Daily.Remove(keys[i]);
    }

    public void SetSettings(GameSettings s)
    {
        _data.Settings = s;
        _dirty = true;
        Flush();
    }

    /// <summary>True once the player has completed or skipped the tutorial.</summary>
    public bool TutorialDone => _data.Settings.TutorialDone;

    public void MarkTutorialDone()
    {
        if (_data.Settings.TutorialDone) return;
        _data.Settings.TutorialDone = true;
        _dirty = true;
        Flush();
    }

    // ---- Progression --------------------------------------------------------

    public LifetimeStats Lifetime => _data.Lifetime;
    public bool HasAchievement(string id) => _data.Achievements.Contains(id);
    public IReadOnlyList<string> UnlockedAchievements => _data.Achievements;

    public IReadOnlyList<LeaderboardEntry> GetLeaderboard(GameModeId mode)
        => _data.Leaderboards.TryGetValue(mode.ToString(), out var l) ? l : new List<LeaderboardEntry>();

    /// <summary>
    /// Fold a finished run into career stats, unlock newly-earned achievements, and
    /// record a leaderboard entry (unmodified, non-revived, non-daily runs — a top
    /// entry also auto-saves its replay). Returns the achievements unlocked by THIS
    /// run, for the results toast.
    /// </summary>
    public IReadOnlyList<string> RecordRun(GameModeId mode, RunStats stats, long score, double time,
        bool completed, GameModifier[] modifiers, bool revived, ulong seed, ReplayData? replay,
        int depth = 0)
    {
        _data.Lifetime.Fold(mode, stats, score, time, completed);

        var ctx = new AchievementContext
        {
            Lifetime = _data.Lifetime, Mode = mode, Run = stats,
            Score = score, Time = time, Completed = completed,
        };
        var fresh = AchievementEngine.Evaluate(_data.Achievements, ctx);
        foreach (var id in fresh) _data.Achievements.Add(id);

        // Leaderboards: only clean, non-revived runs on ranked modes (Daily has its own).
        // Time-attack boards additionally require completion (see LeaderboardLogic.Qualifies).
        bool eligible = modifiers.Length == 0 && !revived && mode != GameModeId.Daily;
        if (LeaderboardLogic.Qualifies(GameMode.IsTimeAttack(mode), score, completed, eligible))
        {
            string key = mode.ToString();
            if (!_data.Leaderboards.TryGetValue(key, out var board))
                _data.Leaderboards[key] = board = new List<LeaderboardEntry>();
            long now = (long)Time.GetUnixTimeFromSystem();
            var entry = new LeaderboardEntry(score, time, stats.TotalLines, seed, now) { Depth = depth };
            int rank = LeaderboardLogic.Insert(board, entry, GameMode.IsTimeAttack(mode));
            if (rank >= 0 && replay is not null)
                entry.ReplayPath = ReplayStore.Save(replay); // watchable record run
        }

        _dirty = true;
        Flush();
        return fresh;
    }

    // ---- Store state --------------------------------------------------------

    /// <summary>
    /// The default theme and all free skins (themes with no store product id) are
    /// always owned — those are the Settings › APPEARANCE picks; everything else
    /// is bought.
    /// </summary>
    public bool OwnsItem(string itemId)
    {
        if (itemId == StoreCatalog.DefaultThemeId || _data.OwnedItems.Contains(itemId)) return true;
        var item = StoreCatalog.ById(itemId);
        // Free cosmetics (themes AND burst-FX artifacts with no store product) are always owned.
        return item is { Kind: StoreItemKind.Theme or StoreItemKind.Artifact }
            && string.IsNullOrEmpty(item.ProductId);
    }

    public void GrantItem(string itemId)
    {
        if (_data.OwnedItems.Contains(itemId)) return;
        _data.OwnedItems.Add(itemId);
        _dirty = true;
        Flush();
    }

    public string EquippedThemeId => _data.EquippedTheme;

    public void EquipTheme(string itemId)
    {
        if (_data.EquippedTheme == itemId) return;
        _data.EquippedTheme = itemId;
        _dirty = true;
        Flush();
    }

    public string EquippedArtifactId => _data.EquippedArtifact;

    public void EquipArtifact(string itemId)
    {
        if (_data.EquippedArtifact == itemId) return;
        _data.EquippedArtifact = itemId;
        _dirty = true;
        Flush();
    }

    public int BoosterCount(string boosterId)
        => _data.Boosters.TryGetValue(boosterId, out var n) ? n : 0;

    public void AddBoosters(string boosterId, int count)
    {
        _data.Boosters[boosterId] = BoosterCount(boosterId) + count;
        _dirty = true;
        Flush();
    }

    /// <summary>Spend one booster; false (and no change) if none are owned.</summary>
    public bool ConsumeBooster(string boosterId)
    {
        int n = BoosterCount(boosterId);
        if (n <= 0) return false;
        _data.Boosters[boosterId] = n - 1;
        _dirty = true;
        Flush();
        return true;
    }

    // ---- Ranked ladder ------------------------------------------------------

    /// <summary>The player's competitive ladder rating (ranked online duels).</summary>
    public RankRating Rank => _data.Rank;

    /// <summary>
    /// Fold a finished ranked match into the ladder and return the rating delta.
    /// Opponent rating is not yet exchanged over the wire, so each ranked match is
    /// scored as even-odds (opponent = our own rating); the tested <see cref="RankSystem"/>
    /// picks up real opponent ratings unchanged once the matchmaker carries them.
    /// </summary>
    public int RecordRankedResult(bool won)
    {
        int delta = RankSystem.Apply(_data.Rank, _data.Rank.Rating, won);
        _dirty = true;
        Flush();
        return delta;
    }

    // ---- Cloud sync ---------------------------------------------------------

    /// <summary>Serialize the whole save for upload to a platform cloud.</summary>
    public string ExportJson()
        => JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });

    /// <summary>
    /// Merge a cloud save into the local one (used when they diverge — the player
    /// played offline on two devices). Conflict resolution is the tested, engine-
    /// agnostic <see cref="SaveMerge"/>: keep every best, union collections, and take
    /// the max of career counters. Safe to call with malformed input (logs + ignores).
    /// </summary>
    public void MergeCloudJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        SaveData cloud;
        try { cloud = JsonSerializer.Deserialize<SaveData>(json) ?? new SaveData(); }
        catch (System.Exception e) { GD.PushWarning($"Cloud merge skipped (bad payload): {e.Message}"); return; }

        SaveMerge.MergeBest(_data.Best, cloud.Best, IsTimeAttackKey);
        SaveMerge.MergeMax(_data.Daily, cloud.Daily);
        SaveMerge.MergeUnion(_data.OwnedItems, cloud.OwnedItems);
        SaveMerge.MergeUnion(_data.Achievements, cloud.Achievements);
        SaveMerge.MergeCounts(_data.Boosters, cloud.Boosters);
        SaveMerge.MergeLifetime(_data.Lifetime, cloud.Lifetime);
        SaveMerge.MergeLeaderboards(_data.Leaderboards, cloud.Leaderboards, IsTimeAttackKey);
        _data.Rank = SaveMerge.MergeRank(_data.Rank, cloud.Rank);

        _dirty = true;
        Flush();
    }

    // A Best/Leaderboard key is a mode name; time-attack modes rank by lowest time.
    private static bool IsTimeAttackKey(string key)
        => System.Enum.TryParse<GameModeId>(key, out var mode) && GameMode.IsTimeAttack(mode);

    public void Flush()
    {
        if (!_dirty) return;
        try
        {
            var json = ExportJson();
            using var f = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
            if (f != null) { f.StoreString(json); _dirty = false; }
        }
        catch (System.Exception e)
        {
            GD.PushWarning($"Save failed: {e.Message}");
        }
    }

    private void Load()
    {
        if (!FileAccess.FileExists(Path)) { FreshInstall = true; return; }
        try
        {
            using var f = FileAccess.Open(Path, FileAccess.ModeFlags.Read);
            var json = f?.GetAsText();
            if (!string.IsNullOrEmpty(json))
                _data = JsonSerializer.Deserialize<SaveData>(json!) ?? new SaveData();
        }
        catch (System.Exception e)
        {
            GD.PushWarning($"Save load failed, using defaults: {e.Message}");
            _data = new SaveData();
        }
    }
}
