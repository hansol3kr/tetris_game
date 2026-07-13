using Godot;
using System;
using Blockfall.Core;
using Blockfall.Core.Localization;
using Blockfall.Platform;
using Blockfall.Theme;

namespace Blockfall.UI;

/// <summary>
/// Browser for saved replays: watch or delete each, or import one from a shared
/// code. Metadata comes straight from <see cref="ReplayStore.List"/> (filename-
/// encoded, no decompression), and a replay is only loaded when actually watched.
/// </summary>
public partial class ReplaysScreen : Control
{
    public event Action<ReplayData>? WatchRequested;
    public event Action? BackRequested;

    private VBoxContainer _list = null!;
    private LineEdit _import = null!;

    public override void _Ready()
    {
        UiTheme.ApplyTo(this);
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(scroll);

        var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(460, 0) };
        col.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(col);

        var title = new Label { Text = Loc.T("REPLAYS"), ThemeTypeVariation = "TitleLabel", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 38);
        title.AddThemeColorOverride("font_color", Palette.Accent);
        col.AddChild(title);

        // Import-from-code row.
        var importRow = new HBoxContainer { CustomMinimumSize = new Vector2(460, 0) };
        importRow.AddThemeConstantOverride("separation", 8);
        _import = new LineEdit { PlaceholderText = Loc.T("paste a replay share code"), CustomMinimumSize = new Vector2(320, 44) };
        var importBtn = new Button { Text = Loc.T("IMPORT"), CustomMinimumSize = new Vector2(120, 44), ThemeTypeVariation = "GhostButton" };
        Motion.BindButtonFeel(importBtn);
        importBtn.Pressed += OnImport;
        importRow.AddChild(_import);
        importRow.AddChild(importBtn);
        col.AddChild(importRow);

        _list = new VBoxContainer { CustomMinimumSize = new Vector2(460, 0) };
        _list.AddThemeConstantOverride("separation", 8);
        col.AddChild(_list);

        var back = new Button { Text = Loc.T("BACK"), ThemeTypeVariation = "GhostButton", CustomMinimumSize = new Vector2(0, 48) };
        Motion.BindButtonFeel(back);
        back.Pressed += () => BackRequested?.Invoke();
        col.AddChild(back);

        RebuildList();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("pause_game")) { BackRequested?.Invoke(); GetViewport().SetInputAsHandled(); }
    }

    private void OnImport()
    {
        var code = _import.Text.Trim();
        if (code.Length == 0) return;
        if (ReplayData.TryFromShareCode(code, out var data) && data is not null)
            WatchRequested?.Invoke(data);
        else
            _import.Text = ""; // reject silently; placeholder returns
    }

    private void RebuildList()
    {
        foreach (var c in _list.GetChildren()) ((Node)c).QueueFree();

        var entries = ReplayStore.List();
        if (entries.Count == 0)
        {
            var empty = new Label { Text = Loc.T("No saved replays yet — save one from the results screen."), ThemeTypeVariation = "DimLabel", HorizontalAlignment = HorizontalAlignment.Center };
            empty.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            empty.CustomMinimumSize = new Vector2(440, 0);
            _list.AddChild(empty);
            return;
        }

        foreach (var e in entries)
            _list.AddChild(BuildRow(e));
    }

    private Control BuildRow(ReplayStore.Entry e)
    {
        var card = new PanelContainer { ThemeTypeVariation = "Card" };
        var row = new HBoxContainer { CustomMinimumSize = new Vector2(440, 52) };
        row.AddThemeConstantOverride("separation", 10);

        var info = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var head = new Label { Text = Loc.T("{0}   {1}", ModeName(e.Mode), e.Score.ToString("N0")) };
        head.AddThemeFontSizeOverride("font_size", 18);
        var date = new Label { Text = Loc.T("{0} lines · {1}", e.Lines, Time.GetDatetimeStringFromUnixTime(e.UnixTime, true)), ThemeTypeVariation = "DimLabel" };
        date.AddThemeFontSizeOverride("font_size", 13);
        info.AddChild(head);
        info.AddChild(date);
        row.AddChild(info);

        var watch = new Button { Text = Loc.T("WATCH"), CustomMinimumSize = new Vector2(90, 44), ThemeTypeVariation = "PrimaryButton" };
        Motion.BindButtonFeel(watch);
        watch.Pressed += () =>
        {
            var data = ReplayStore.Load(e.Path);
            if (data is not null) WatchRequested?.Invoke(data);
        };
        var del = new Button { Text = "✕", CustomMinimumSize = new Vector2(44, 44), ThemeTypeVariation = "GhostButton" };
        Motion.BindButtonFeel(del);
        del.Pressed += () => { ReplayStore.Delete(e.Path); RebuildList(); };
        row.AddChild(watch);
        row.AddChild(del);

        card.AddChild(row);
        return card;
    }

    private static string ModeName(GameModeId m) => GameMode.ById(m).Name;
}
