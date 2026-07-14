using Godot;
using System;
using System.Collections.Generic;
using Blockfall.Core;
using Blockfall.Core.Localization;
using Blockfall.Gameplay;
using Blockfall.Theme;

namespace Blockfall.UI;

/// <summary>
/// The landing screen, tiered so one thing leads: a hero PLAY card (last-played
/// mode), the gold DAILY card with a live reset countdown, a compact 2-column
/// grid for the other modes, VERSUS pinned below, and power-user tools (modifier
/// chips + seed entry) folded away behind CUSTOM RUN. Emits the same events as
/// v1 so the router is unchanged.
/// </summary>
public partial class MainMenu : Control
{
    public event Action<GameModeId>? ModeChosen;
    public event Action<ulong>? SeedEntered;
    public event Action? DailyChosen;
    public event Action? SettingsChosen;
    public event Action? StoreChosen;
    public event Action? VersusChosen;
    public event Action? TutorialChosen;
    public event Action? ReplaysChosen;
    public event Action? ProfileChosen;
    public event Action? BlockFitChosen;

    private readonly HashSet<GameModifier> _mods = new();
    private static bool _introPlayed; // full entrance choreography only once per session

    private static readonly GameModeId[] SoloModes =
    {
        GameModeId.Marathon, GameModeId.Sprint, GameModeId.Ultra, GameModeId.Zen,
        GameModeId.Dig, GameModeId.Survival, GameModeId.Master,
    };

    /// <summary>Modifiers currently toggled on — applied to the next run.</summary>
    public GameModifier[] SelectedModifiers()
    {
        var list = new List<GameModifier>(_mods);
        return list.ToArray();
    }

    public override void _Ready()
    {
        UiTheme.ApplyTo(this);
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(scroll);

        // Outer vbox fills the scroll viewport; expanding spacers center short content.
        var outer = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        scroll.AddChild(outer);
        outer.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });

        // Scale the menu column to the device: fill most of the (safe-area) width
        // on a phone, but cap it so it doesn't sprawl on tablets / desktop.
        float menuW = Mathf.Clamp(Bootstrap.Instance.SafeCanvasSize.X * 0.92f, 340f, 600f);
        var col = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            CustomMinimumSize = new Vector2(menuW, 0),
        };
        col.AddThemeConstantOverride("separation", 12);
        outer.AddChild(col);
        outer.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });

        BuildLogo(col);
        col.AddChild(Spacer(14));

        var hero = ResolveHeroMode();
        col.AddChild(BuildHeroCard(hero));
        col.AddChild(BuildDailyCard());
        col.AddChild(Spacer(2));
        col.AddChild(BuildModeGrid(hero));
        col.AddChild(Spacer(2));
        col.AddChild(BuildVersusCard());
        col.AddChild(Spacer(6));
        col.AddChild(BuildBlockFitCard());
        col.AddChild(Spacer(6));
        BuildCustomRun(col);
        col.AddChild(BuildBottomButtons());

        BuildFooter();

        // Entrance: staggered on first arrival, instant-ish afterwards.
        var items = new List<Control>();
        foreach (var child in col.GetChildren())
            if (child is Control c) items.Add(c);
        if (_introPlayed || Motion.Reduced)
        {
            // The router already fades the whole screen in; nothing extra.
        }
        else
        {
            _introPlayed = true;
            Motion.EnterStagger(items.ToArray(), initialDelay: 0.05f);
        }
    }

    // ---- Logo ----------------------------------------------------------------

    private void BuildLogo(Container parent)
    {
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        box.AddThemeConstantOverride("separation", 8);

        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        row.AddThemeConstantOverride("separation", 16);

        var mark = new Theme.Icon(IconKind.Blocks, Palette.Accent, 42)
        {
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        row.AddChild(mark);
        Motion.PulseLoop(mark, lowAlpha: 0.65f, period: 2.2f);

        var title = new Label { Text = "BLOCKFALL", ThemeTypeVariation = "TitleLabel" };
        title.AddThemeFontSizeOverride("font_size", 46);
        row.AddChild(title);
        box.AddChild(row);

        // Underline: cyan bar with a violet tail — the one bespoke brand detail.
        var bar = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        bar.AddThemeConstantOverride("separation", 4);
        var cyan = new ColorRect
        {
            Color = Palette.Accent,
            CustomMinimumSize = new Vector2(0, 3),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 3f,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        var violet = new ColorRect
        {
            Color = Palette.AccentViolet,
            CustomMinimumSize = new Vector2(0, 3),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1f,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        bar.AddChild(cyan);
        bar.AddChild(violet);
        box.AddChild(bar);

        var subtitle = new Label
        {
            Text = "N E O N   D R O P",
            ThemeTypeVariation = "SectionLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        box.AddChild(subtitle);

        parent.AddChild(box);
    }

    // ---- Cards -----------------------------------------------------------------

    private GameModeId ResolveHeroMode()
    {
        var last = Bootstrap.Instance.Save.Settings.LastPlayedMode;
        return Enum.TryParse<GameModeId>(last, out var m) && Array.IndexOf(SoloModes, m) >= 0
            ? m : GameModeId.Marathon;
    }

    private Control BuildHeroCard(GameModeId hero)
    {
        var b = Card(Palette.Accent, 96);
        var content = CardContent(b);

        content.AddChild(AccentBar(Palette.Accent, 60));
        content.AddChild(new Theme.Icon(IconKind.Play, Palette.Accent, 30) { SizeFlagsVertical = SizeFlags.ShrinkCenter });

        var text = new VBoxContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 0);
        var play = new Label { Text = Loc.T("PLAY") };
        play.AddThemeFontOverride("font", Fonts.UiBold);
        play.AddThemeFontSizeOverride("font_size", 27);
        var sub = new Label
        {
            Text = $"{ModeTitle(hero)} · {ModeBlurb(hero)}",
            ThemeTypeVariation = "DimLabel",
            ClipText = true, // never push the best-chip off the card
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        sub.AddThemeFontSizeOverride("font_size", 15);
        text.AddChild(play);
        text.AddChild(sub);
        content.AddChild(text);

        content.AddChild(BestChip(hero));

        b.Pressed += () => ModeChosen?.Invoke(hero);
        return b;
    }

    private Control BuildDailyCard()
    {
        var (key, _) = DailyChallenge.Today();
        var best = Bootstrap.Instance.Save.GetDailyBest(key);

        var b = Card(Palette.AccentGold, 76);
        var content = CardContent(b);

        content.AddChild(AccentBar(Palette.AccentGold, 44));
        content.AddChild(new Theme.Icon(IconKind.Calendar, Palette.AccentGold, 26) { SizeFlagsVertical = SizeFlags.ShrinkCenter });

        var text = new VBoxContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 0);
        var name = new Label { Text = Loc.T("DAILY CHALLENGE") };
        name.AddThemeFontOverride("font", Fonts.UiBold);
        name.AddThemeFontSizeOverride("font_size", 21);
        name.AddThemeColorOverride("font_color", Palette.AccentGold);
        var until = TimeSpan.FromTicks(DateTime.UtcNow.Date.AddDays(1).Ticks - DateTime.UtcNow.Ticks);
        var sub = new Label
        {
            Text = Loc.T("ONE SEED, ONE SHOT · NEW SEED IN {0}H {1}M", (int)until.TotalHours, until.Minutes.ToString("00")),
            ThemeTypeVariation = "DimLabel",
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        sub.AddThemeFontSizeOverride("font_size", 14);
        text.AddChild(name);
        text.AddChild(sub);
        content.AddChild(text);

        if (best.HasValue)
            content.AddChild(ChipLabel($"★ {best.Value:N0}", Palette.AccentGold));

        b.Pressed += () => DailyChosen?.Invoke();
        return b;
    }

    private Control BuildModeGrid(GameModeId hero)
    {
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 12);

        foreach (var mode in SoloModes)
        {
            if (mode == hero) continue;
            var m = mode;
            var b = Card(Palette.Accent, 72);
            b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            b.CustomMinimumSize = new Vector2(224, 72);
            var content = CardContent(b, marginH: 14);

            content.AddChild(new Theme.Icon(ModeIcon(m), Palette.TextSecondary, 22) { SizeFlagsVertical = SizeFlags.ShrinkCenter });

            var text = new VBoxContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            text.AddThemeConstantOverride("separation", 0);
            var name = new Label { Text = ModeTitle(m) };
            name.AddThemeFontSizeOverride("font_size", 19);
            text.AddChild(name);
            var best = Bootstrap.Instance.Save.GetBest(m);
            var sub = new Label
            {
                Text = best.HasValue ? FormatBest(m, best.Value) : ModeBlurb(m),
                ThemeTypeVariation = "DimLabel",
            };
            sub.AddThemeFontSizeOverride("font_size", 13);
            text.AddChild(sub);
            content.AddChild(text);

            b.Pressed += () => ModeChosen?.Invoke(m);
            grid.AddChild(b);
        }
        return grid;
    }

    private Control BuildVersusCard()
    {
        var b = Card(Palette.AccentViolet, 72);
        var content = CardContent(b);

        content.AddChild(AccentBar(Palette.AccentViolet, 40));
        content.AddChild(new Theme.Icon(IconKind.Swords, Palette.AccentViolet, 26) { SizeFlagsVertical = SizeFlags.ShrinkCenter });

        var text = new VBoxContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 0);
        var name = new Label { Text = Loc.T("VERSUS CPU") };
        name.AddThemeFontOverride("font", Fonts.UiBold);
        name.AddThemeFontSizeOverride("font_size", 21);
        name.AddThemeColorOverride("font_color", Palette.AccentViolet);
        var sub = new Label { Text = Loc.T("GARBAGE BATTLE · FIVE DIFFICULTIES"), ThemeTypeVariation = "DimLabel" };
        sub.AddThemeFontSizeOverride("font_size", 14);
        text.AddChild(name);
        text.AddChild(sub);
        content.AddChild(text);

        b.Pressed += () => VersusChosen?.Invoke();
        return b;
    }

    private Control BuildBlockFitCard()
    {
        var b = Card(Palette.AccentGreen, 72);
        var content = CardContent(b);

        content.AddChild(AccentBar(Palette.AccentGreen, 40));
        content.AddChild(new Theme.Icon(IconKind.Blocks, Palette.AccentGreen, 26) { SizeFlagsVertical = SizeFlags.ShrinkCenter });

        var text = new VBoxContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 0);
        var name = new Label { Text = Loc.T("BLOCK FIT") };
        name.AddThemeFontOverride("font", Fonts.UiBold);
        name.AddThemeFontSizeOverride("font_size", 21);
        name.AddThemeColorOverride("font_color", Palette.AccentGreen);
        var sub = new Label { Text = Loc.T("DRAG & FIT · NO GRAVITY"), ThemeTypeVariation = "DimLabel" };
        sub.AddThemeFontSizeOverride("font_size", 14);
        text.AddChild(name);
        text.AddChild(sub);
        content.AddChild(text);

        b.Pressed += () => BlockFitChosen?.Invoke();
        return b;
    }

    // ---- Custom run (modifiers + seed, collapsed by default) --------------------

    private void BuildCustomRun(Container parent)
    {
        var toggle = new Button
        {
            Text = Loc.T("CUSTOM RUN") + "  ▾",
            ThemeTypeVariation = "GhostButton",
            ToggleMode = true,
            CustomMinimumSize = new Vector2(0, 48),
        };
        Motion.BindButtonFeel(toggle);
        parent.AddChild(toggle);

        var panel = new VBoxContainer { Visible = false };
        panel.AddThemeConstantOverride("separation", 10);

        var hint = new Label
        {
            Text = Loc.T("MODIFIERS STACK ON ANY RUN · MODIFIED RUNS SET NO RECORDS"),
            ThemeTypeVariation = "SectionLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        panel.AddChild(hint);

        var flow = new HFlowContainer();
        flow.AddThemeConstantOverride("h_separation", 8);
        flow.AddThemeConstantOverride("v_separation", 8);
        foreach (GameModifier m in Enum.GetValues<GameModifier>())
        {
            var mod = m;
            var chip = new Button
            {
                Text = Loc.T(ModifierSet.Label(m)),
                ToggleMode = true,
                ThemeTypeVariation = "ChipButton",
                CustomMinimumSize = new Vector2(0, 44),
            };
            Motion.BindButtonFeel(chip);
            chip.Toggled += on => { if (on) _mods.Add(mod); else _mods.Remove(mod); };
            flow.AddChild(chip);
        }
        panel.AddChild(flow);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var input = new LineEdit
        {
            PlaceholderText = Loc.T("SEED CODE OR ANY WORD"),
            CustomMinimumSize = new Vector2(0, 46),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        var play = new Button { Text = Loc.T("PLAY SEED"), CustomMinimumSize = new Vector2(130, 46) };
        play.AddThemeFontSizeOverride("font_size", 16);
        Motion.BindButtonFeel(play);
        play.Pressed += () =>
        {
            string txt = input.Text.Trim();
            if (txt.Length == 0) return;
            ulong seed = SeedCode.TryDecode(txt, out var s) ? s : SeedCode.FromText(txt);
            SeedEntered?.Invoke(seed);
        };
        row.AddChild(input);
        row.AddChild(play);
        panel.AddChild(row);

        parent.AddChild(panel);

        toggle.Toggled += on =>
        {
            toggle.Text = Loc.T("CUSTOM RUN") + (on ? "  ▴" : "  ▾");
            panel.Visible = on;
            if (on) Motion.PopIn(panel, 0.14f);
        };
    }

    private Control BuildBottomButtons()
    {
        // HFlow wraps to a second line if the icons don't all fit one row.
        var row = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        row.AddThemeConstantOverride("h_separation", 10);
        row.AddThemeConstantOverride("v_separation", 8);
        row.AddChild(GhostIconButton(IconKind.Blocks, Loc.T("HOW TO PLAY"), () => TutorialChosen?.Invoke()));
        row.AddChild(GhostIconButton(IconKind.Trophy, Loc.T("PROFILE"), () => ProfileChosen?.Invoke()));
        row.AddChild(GhostIconButton(IconKind.Refresh, Loc.T("REPLAYS"), () => ReplaysChosen?.Invoke()));
        row.AddChild(GhostIconButton(IconKind.Diamond, Loc.T("STORE"), () => StoreChosen?.Invoke()));
        row.AddChild(GhostIconButton(IconKind.Gear, Loc.T("SETTINGS"), () => SettingsChosen?.Invoke()));
        return row;
    }

    private static Button GhostIconButton(IconKind icon, string text, Action onPressed)
    {
        var b = new Button
        {
            ThemeTypeVariation = "GhostButton",
            CustomMinimumSize = new Vector2(0, 50),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        var content = CardContent(b, marginH: 0);
        content.Alignment = BoxContainer.AlignmentMode.Center;
        content.AddChild(new Theme.Icon(icon, Palette.TextSecondary, 20) { SizeFlagsVertical = SizeFlags.ShrinkCenter });
        var l = new Label { Text = text, ThemeTypeVariation = "DimLabel" };
        l.AddThemeFontSizeOverride("font_size", 18);
        content.AddChild(l);
        Motion.BindButtonFeel(b);
        b.Pressed += () => onPressed();
        return b;
    }

    private void BuildFooter()
    {
        var footer = new Label
        {
            Text = Loc.T("Original brand — not affiliated with Tetris®."),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        footer.AddThemeFontSizeOverride("font_size", 12);
        footer.AddThemeColorOverride("font_color", Palette.TextTertiary);
        AddChild(footer);
        footer.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        footer.OffsetTop = -34;
        footer.OffsetBottom = -10;
    }

    // ---- Small builders -----------------------------------------------------

    /// <summary>Glass card button with an accent-tinted hover/pressed treatment.</summary>
    private static Button Card(Color accent, float minHeight)
    {
        var b = new Button { CustomMinimumSize = new Vector2(0, minHeight) };
        b.AddThemeStyleboxOverride("normal",
            TextureFactory.GlassStyle(Palette.RadiusM, Palette.GlassTop, Palette.GlassBottom, Palette.GlassBorder, 1f, 0.16f, 18, 12));
        b.AddThemeStyleboxOverride("hover",
            TextureFactory.GlassStyle(Palette.RadiusM,
                Mul(Palette.GlassTop, 1.7f), Mul(Palette.GlassBottom, 1.7f),
                new Color(accent.R, accent.G, accent.B, 0.6f), 1.3f, 0.2f, 18, 12));
        b.AddThemeStyleboxOverride("pressed",
            TextureFactory.GlassStyle(Palette.RadiusM,
                new Color(accent.R, accent.G, accent.B, 0.18f), new Color(accent.R, accent.G, accent.B, 0.08f),
                new Color(accent.R, accent.G, accent.B, 0.9f), 1.4f, 0.12f, 18, 12));
        var focus = TextureFactory.GlassStyle(Palette.RadiusM,
            new Color(0, 0, 0, 0), new Color(0, 0, 0, 0),
            new Color(accent.R, accent.G, accent.B, 0.8f), 1.6f, 0f, 18, 12);
        focus.DrawCenter = false;
        b.AddThemeStyleboxOverride("focus", focus);
        Motion.BindButtonFeel(b);
        return b;
    }

    /// <summary>Full-rect HBox inside a button for icon/label/chip content (input-transparent).</summary>
    private static HBoxContainer CardContent(Button b, float marginH = 18)
    {
        var box = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        box.OffsetLeft = marginH;
        box.OffsetRight = -marginH;
        box.AddThemeConstantOverride("separation", 14);
        b.AddChild(box);
        return box;
    }

    private static Control AccentBar(Color color, float height)
    {
        return new ColorRect
        {
            Color = color,
            CustomMinimumSize = new Vector2(4, height),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        };
    }

    private Control BestChip(GameModeId mode)
    {
        var best = Bootstrap.Instance.Save.GetBest(mode);
        return ChipLabel(best.HasValue ? FormatBest(mode, best.Value) : Loc.T("FIRST RUN"), Palette.TextSecondary);
    }

    private static Control ChipLabel(string text, Color color)
    {
        var l = new Label { Text = text, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        l.AddThemeFontSizeOverride("font_size", 14);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }

    private static Control Spacer(float h) => new Control { CustomMinimumSize = new Vector2(0, h), MouseFilter = MouseFilterEnum.Ignore };

    private static Color Mul(Color c, float alphaMul) => new(c.R, c.G, c.B, Mathf.Clamp(c.A * alphaMul, 0f, 1f));

    // ---- Mode metadata --------------------------------------------------------

    private static string ModeTitle(GameModeId m) => m switch
    {
        GameModeId.Marathon => Loc.T("MARATHON"),
        GameModeId.Sprint => Loc.T("SPRINT 40"),
        GameModeId.Ultra => Loc.T("ULTRA 2:00"),
        GameModeId.Zen => Loc.T("ZEN"),
        GameModeId.Dig => Loc.T("DIG RACE"),
        GameModeId.Survival => Loc.T("SURVIVAL"),
        GameModeId.Master => Loc.T("MASTER 20G"),
        _ => m.ToString().ToUpperInvariant(),
    };

    private static string ModeBlurb(GameModeId m) => m switch
    {
        GameModeId.Marathon => Loc.T("CLIMB TO LEVEL 15"),
        GameModeId.Sprint => Loc.T("40 LINES, FASTEST TIME"),
        GameModeId.Ultra => Loc.T("MAX SCORE IN 2 MINUTES"),
        GameModeId.Zen => Loc.T("NO PRESSURE, NO END"),
        GameModeId.Dig => Loc.T("DIG THROUGH THE GARBAGE"),
        GameModeId.Survival => Loc.T("THE FLOOR KEEPS RISING"),
        GameModeId.Master => Loc.T("INSTANT GRAVITY"),
        _ => "",
    };

    private static IconKind ModeIcon(GameModeId m) => m switch
    {
        GameModeId.Marathon => IconKind.Trophy,
        GameModeId.Sprint => IconKind.Timer,
        GameModeId.Ultra => IconKind.Bolt,
        GameModeId.Zen => IconKind.Infinity,
        GameModeId.Dig => IconKind.Shovel,
        GameModeId.Survival => IconKind.Skull,
        GameModeId.Master => IconKind.Diamond,
        _ => IconKind.Play,
    };

    private static string FormatBest(GameModeId mode, double value)
    {
        if (GameMode.IsTimeAttack(mode))
        {
            int m = (int)(value / 60);
            double s = value - m * 60;
            return $"⏱ {m}:{s:00.00}";
        }
        return $"★ {value:N0}";
    }
}
