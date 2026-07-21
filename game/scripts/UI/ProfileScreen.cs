using Godot;
using System;
using Blockfall.Core;
using Blockfall.Core.Localization;
using Blockfall.Platform;
using Blockfall.Theme;

namespace Blockfall.UI;

/// <summary>
/// The player's progression hub: career STATS, ACHIEVEMENTS (locked/unlocked),
/// and per-mode RANKS (local leaderboards, with a WATCH button for entries whose
/// replay was auto-saved). All data comes from <see cref="SaveManager"/>.
/// </summary>
public partial class ProfileScreen : Control
{
    public event Action? BackRequested;
    public event Action<ReplayData>? WatchReplay;

    private static readonly GameModeId[] RankModes =
    {
        GameModeId.Marathon, GameModeId.Sprint, GameModeId.Ultra, GameModeId.Zen,
        GameModeId.Dig, GameModeId.Survival, GameModeId.Master, GameModeId.Descent,
    };

    private OptionButton _modePick = null!;
    private VBoxContainer _rankList = null!;

    public override void _Ready()
    {
        UiTheme.ApplyTo(this);
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var col = new VBoxContainer();
        col.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        col.AddThemeConstantOverride("separation", 10);
        col.OffsetLeft = 20; col.OffsetRight = -20; col.OffsetTop = 24; col.OffsetBottom = -20;
        AddChild(col);

        var title = new Label { Text = Loc.T("PROFILE"), ThemeTypeVariation = "TitleLabel", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 36);
        title.AddThemeColorOverride("font_color", Palette.Accent);
        col.AddChild(title);

        var tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        col.AddChild(tabs);
        tabs.AddChild(BuildStatsTab());
        tabs.AddChild(BuildAchievementsTab());
        tabs.AddChild(BuildRanksTab());

        var back = new Button { Text = Loc.T("BACK"), ThemeTypeVariation = "GhostButton", CustomMinimumSize = new Vector2(0, 48) };
        Motion.BindButtonFeel(back);
        back.Pressed += () => BackRequested?.Invoke();
        col.AddChild(back);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("pause_game")) { BackRequested?.Invoke(); GetViewport().SetInputAsHandled(); }
    }

    // ---- STATS ------------------------------------------------------------
    private Control BuildStatsTab()
    {
        var s = Bootstrap.Instance.Save.Lifetime;
        var scroll = new ScrollContainer { Name = "STATS", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var grid = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(grid);

        AddStat(grid, Loc.T("GAMES PLAYED"), s.GamesPlayed.ToString("N0"));
        AddStat(grid, Loc.T("TOTAL SCORE"), s.TotalScore.ToString("N0"));
        AddStat(grid, Loc.T("LINES CLEARED"), s.TotalLines.ToString("N0"));
        AddStat(grid, Loc.T("PIECES PLACED"), s.TotalPieces.ToString("N0"));
        AddStat(grid, Loc.T("TIME PLAYED"), FormatPlaytime(s.TotalPlaytime));
        AddStat(grid, Loc.T("TETRISES"), s.Tetrises.ToString("N0"));
        AddStat(grid, Loc.T("T-SPINS"), s.TSpins.ToString("N0"));
        AddStat(grid, Loc.T("PERFECT CLEARS"), s.PerfectClears.ToString("N0"));
        AddStat(grid, Loc.T("BEST COMBO"), s.BestCombo.ToString());
        AddStat(grid, Loc.T("BEST BACK-TO-BACK"), s.BestBackToBack.ToString());
        AddStat(grid, Loc.T("BEST PIECES/SEC"), s.BestPps.ToString("0.00"));
        return scroll;
    }

    private static void AddStat(Container parent, string label, string value)
    {
        var row = new HBoxContainer();
        var l = new Label { Text = label, ThemeTypeVariation = "DimLabel", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        l.AddThemeFontSizeOverride("font_size", 16);
        var v = new Label { Text = value, HorizontalAlignment = HorizontalAlignment.Right };
        v.AddThemeFontSizeOverride("font_size", 16);
        row.AddChild(l);
        row.AddChild(v);
        parent.AddChild(row);
    }

    // ---- ACHIEVEMENTS -----------------------------------------------------
    private Control BuildAchievementsTab()
    {
        var save = Bootstrap.Instance.Save;
        int have = 0;
        var scroll = new ScrollContainer { Name = "ACHIEVEMENTS", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(box);

        var count = new Label { ThemeTypeVariation = "SectionLabel" };
        box.AddChild(count);

        foreach (var a in AchievementCatalog.All)
        {
            bool unlocked = save.HasAchievement(a.Id);
            if (unlocked) have++;

            var card = new PanelContainer { ThemeTypeVariation = "Card", Modulate = unlocked ? Colors.White : new Color(1, 1, 1, 0.45f) };
            var row = new VBoxContainer();
            row.AddThemeConstantOverride("separation", 0);
            var name = new Label { Text = (unlocked ? "★  " : "🔒  ") + a.Name };
            name.AddThemeFontSizeOverride("font_size", 18);
            if (unlocked) name.AddThemeColorOverride("font_color", Palette.AccentGold);
            var desc = new Label { Text = a.Description, ThemeTypeVariation = "DimLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart };
            desc.AddThemeFontSizeOverride("font_size", 13);
            row.AddChild(name);
            row.AddChild(desc);
            card.AddChild(row);
            box.AddChild(card);
        }
        count.Text = Loc.T("{0} / {1} UNLOCKED", have, AchievementCatalog.All.Count);
        return scroll;
    }

    // ---- RANKS (leaderboards) ---------------------------------------------
    private Control BuildRanksTab()
    {
        var root = new VBoxContainer { Name = "RANKS", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        root.AddThemeConstantOverride("separation", 8);

        _modePick = new OptionButton { CustomMinimumSize = new Vector2(0, 44) };
        foreach (var m in RankModes) _modePick.AddItem(GameMode.ById(m).Name);
        _modePick.ItemSelected += _ => RebuildRanks();
        root.AddChild(_modePick);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _rankList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rankList.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_rankList);
        root.AddChild(scroll);

        RebuildRanks();
        return root;
    }

    private void RebuildRanks()
    {
        foreach (var c in _rankList.GetChildren()) ((Node)c).QueueFree();

        var mode = RankModes[Mathf.Clamp(_modePick.Selected, 0, RankModes.Length - 1)];
        bool timeAttack = GameMode.IsTimeAttack(mode);
        var entries = Bootstrap.Instance.Save.GetLeaderboard(mode);

        if (entries.Count == 0)
        {
            var empty = new Label { Text = Loc.T("No entries yet — play this mode to rank."), ThemeTypeVariation = "DimLabel", HorizontalAlignment = HorizontalAlignment.Center };
            _rankList.AddChild(empty);
            return;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var card = new PanelContainer { ThemeTypeVariation = "Card" };
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);

            var rank = new Label { Text = $"#{i + 1}", CustomMinimumSize = new Vector2(40, 0) };
            rank.AddThemeFontSizeOverride("font_size", 18);
            rank.AddThemeColorOverride("font_color", i == 0 ? Palette.AccentGold : Palette.TextPrimary);

            // Descent rows lead with the depth the board actually ranks by.
            string metricText = mode == GameModeId.Descent
                ? $"{e.Depth}/{RunDirector.StageCount} · {e.Score:N0}"
                : timeAttack ? FormatTime(e.TimeSeconds) : e.Score.ToString("N0");
            var metric = new Label { Text = metricText, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            metric.AddThemeFontSizeOverride("font_size", 18);
            var date = new Label { Text = Time.GetDatetimeStringFromUnixTime(e.DateUnix, true), ThemeTypeVariation = "DimLabel" };
            date.AddThemeFontSizeOverride("font_size", 12);

            row.AddChild(rank);
            row.AddChild(metric);
            row.AddChild(date);

            if (!string.IsNullOrEmpty(e.ReplayPath) && FileAccess.FileExists(e.ReplayPath))
            {
                var watch = new Button { Text = "▶", CustomMinimumSize = new Vector2(44, 40), ThemeTypeVariation = "GhostButton" };
                Motion.BindButtonFeel(watch);
                string path = e.ReplayPath;
                watch.Pressed += () =>
                {
                    var data = ReplayStore.Load(path);
                    if (data is not null) WatchReplay?.Invoke(data);
                };
                row.AddChild(watch);
            }
            card.AddChild(row);
            _rankList.AddChild(card);
        }
    }

    // ---- formatting -------------------------------------------------------
    private static string FormatTime(double t)
    {
        int m = (int)(t / 60);
        double s = t - m * 60;
        return $"{m}:{s:00.00}";
    }

    private static string FormatPlaytime(double seconds)
    {
        int h = (int)(seconds / 3600);
        int m = (int)((seconds - h * 3600) / 60);
        return h > 0 ? Loc.T("{0}h {1}m", h, m) : Loc.T("{0}m", m);
    }
}
