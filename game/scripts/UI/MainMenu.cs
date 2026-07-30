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
    public event Action? DescentChosen;

    private readonly HashSet<GameModifier> _mods = new();
    /// <summary>First arrival of the session gets the extra lead-in beat; later returns
    /// still re-run the stagger (it used to gate the WHOLE choreography, so coming back
    /// from a run landed on a dead-still screen that read as "the app froze").</summary>
    private static bool _introPlayed;

    /// <summary>Accumulated dt for the hero card's specular sweep — the ONE idle motion on
    /// this screen. Never wall-clock (view code must stay off any real-time source).</summary>
    private float _sweepT;
    private Control? _sweep;

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

        var scroll = new TouchScroll
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

        // Every game entry is now Block Fit: PLAY (endless), DAILY (seeded), DESCENT
        // (garbage-rising survival), VERSUS (CPU). The falling-mode grid + custom-run are
        // retired from the menu (the falling engine stays in code, just not menu-reachable).
        col.AddChild(BuildHeroCard());
        col.AddChild(BuildDailyCard());
        col.AddChild(BuildDescentCard());
        col.AddChild(Spacer(6));
        col.AddChild(BuildVersusCard());
        col.AddChild(Spacer(6));
        col.AddChild(BuildBottomButtons());
        col.AddChild(Spacer(40)); // reserve room for the floating footer so it can't overlap the bottom pills

        BuildFooter();

        // Entrance: the stagger runs on EVERY arrival, only the lead-in beat is
        // first-time-only. Motion.EnterStagger already caps the total stagger at 200ms
        // (Motion.cs:40) and degrades to a single fade under reduced motion, so a
        // re-entry still feels immediate — but the screen is never frozen on arrival.
        var items = new List<Control>();
        foreach (var child in col.GetChildren())
            if (child is Control c) items.Add(c);
        Motion.EnterStagger(items.ToArray(), initialDelay: _introPlayed ? 0f : 0.05f);
        _introPlayed = true;

        // Only the hero sweep needs a per-frame tick; reduced motion parks it statically.
        SetProcess(!Motion.Reduced && _sweep != null);
    }

    public override void _Process(double delta)
    {
        if (_sweep == null || !IsInstanceValid(_sweep)) { SetProcess(false); return; }
        _sweepT += (float)delta;
        _sweep.QueueRedraw();
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

    private Control BuildHeroCard()
    {
        var b = Card(Palette.Accent, 96);
        AttachSpecularSweep(b);
        var content = CardContent(b);

        content.AddChild(AccentBar(Palette.Accent, 60));
        content.AddChild(new Theme.Icon(IconKind.Blocks, Palette.Accent, 30) { SizeFlagsVertical = SizeFlags.ShrinkCenter });

        var text = new VBoxContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 0);
        var play = new Label { Text = Loc.T("PLAY") };
        play.AddThemeFontOverride("font", Fonts.UiBold);
        play.AddThemeFontSizeOverride("font_size", 27);
        var sub = new Label
        {
            Text = Loc.T("BLOCK FIT · DRAG & FIT, NO GRAVITY"),
            ThemeTypeVariation = "DimLabel",
            ClipText = true, // never push the best-chip off the card
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        sub.AddThemeFontSizeOverride("font_size", 15);
        text.AddChild(play);
        text.AddChild(sub);
        content.AddChild(text);

        // The primary PLAY is now the placement puzzle; the chip shows the Block Fit best.
        long bfBest = (long)Bootstrap.Instance.Save.BlockFitBest;
        content.AddChild(ChipLabel(bfBest > 0 ? $"★ {bfBest:N0}" : Loc.T("FIRST RUN"), Palette.TextSecondary));

        TouchScroll.Bind(b, () => BlockFitChosen?.Invoke());
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

        TouchScroll.Bind(b, () => DailyChosen?.Invoke());
        return b;
    }

    /// <summary>The flagship run mode: five strata, charm drafts between them.</summary>
    private Control BuildDescentCard()
    {
        var b = Card(Palette.AccentRed, 76);
        var content = CardContent(b);

        content.AddChild(AccentBar(Palette.AccentRed, 44));
        content.AddChild(new Theme.Icon(IconKind.Dice, Palette.AccentRed, 26) { SizeFlagsVertical = SizeFlags.ShrinkCenter });

        var text = new VBoxContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 0);
        var name = new Label { Text = Loc.T("DESCENT") };
        name.AddThemeFontOverride("font", Fonts.UiBold);
        name.AddThemeFontSizeOverride("font_size", 21);
        name.AddThemeColorOverride("font_color", Palette.AccentRed);
        var sub = new Label
        {
            Text = Loc.T("PLACE & SURVIVE · GARBAGE KEEPS RISING"),
            ThemeTypeVariation = "DimLabel",
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        sub.AddThemeFontSizeOverride("font_size", 14);
        text.AddChild(name);
        text.AddChild(sub);
        content.AddChild(text);

        long dBest = (long)Bootstrap.Instance.Save.DescentFitBest;
        if (dBest > 0)
            content.AddChild(ChipLabel($"★ {dBest:N0}", Palette.AccentRed));

        TouchScroll.Bind(b, () => DescentChosen?.Invoke());
        return b;
    }

    private Control BuildModeGrid()
    {
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 12);

        // The hero card is now Block Fit, so every falling mode lives here (none is skipped).
        foreach (var mode in SoloModes)
        {
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

            TouchScroll.Bind(b, () => ModeChosen?.Invoke(m));
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

        TouchScroll.Bind(b, () => VersusChosen?.Invoke());
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

    /// <summary>44pt at this project's 0.521pt-per-design-px scale (44 / 0.521 ≈ 84.4). The
    /// repo-wide minimum touch target; <c>SettingsScreen</c> and <c>StoreScreen</c> hold the
    /// same constant.</summary>
    private const int TouchTarget = 84;

    /// <summary>
    /// The five secondary entry points. A FIXED two-column grid, not a wrapping flow of
    /// fixed-width pills: the flow's 168px pills fell 3+2 across a 340-600px column, so the
    /// row structure changed with device width and the last row was a ragged pair of pills
    /// floating under three. Two columns are two columns on every phone, each pill is half a
    /// column wide (170-292px) instead of a constant 168, and the reading order — the order
    /// the arguments appear below — is unchanged: HOW TO PLAY, PROFILE, REPLAYS, STORE,
    /// SETTINGS, with SETTINGS alone in the last row.
    /// Separation is 16 design px ≈ 8.3pt, the HIG gap for adjacent 44pt targets (it was 8px
    /// vertical ≈ 4.2pt, i.e. two 56px-tall pills a thumb-width apart with almost no gutter).
    /// </summary>
    private Control BuildBottomButtons()
    {
        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 16);
        grid.AddThemeConstantOverride("v_separation", 16);
        // Accents follow meaning, not decoration: cyan = the standard UI accent, violet =
        // the store/cosmetics identity, gold = the records surface (PROFILE is the stats
        // and personal-best screen, so it is the one sanctioned reading of gold outside
        // daily/new-best; demote it to cyan if that rule is ever tightened).
        grid.AddChild(GhostIconButton(IconKind.Blocks, Loc.T("HOW TO PLAY"), Palette.Accent, () => TutorialChosen?.Invoke()));
        grid.AddChild(GhostIconButton(IconKind.Trophy, Loc.T("PROFILE"), Palette.AccentGold, () => ProfileChosen?.Invoke()));
        grid.AddChild(GhostIconButton(IconKind.Refresh, Loc.T("REPLAYS"), Palette.Accent, () => ReplaysChosen?.Invoke()));
        grid.AddChild(GhostIconButton(IconKind.Diamond, Loc.T("STORE"), Palette.AccentViolet, () => StoreChosen?.Invoke()));
        grid.AddChild(GhostIconButton(IconKind.Gear, Loc.T("SETTINGS"), Palette.TextSecondary.Lerp(Palette.Accent, 0.6f), () => SettingsChosen?.Invoke()));
        return grid;
    }

    /// <summary>
    /// A bottom-row pill. Styled per INSTANCE, not on the "GhostButton" variation: that
    /// variation is the quiet secondary-nav style on all twelve screens, and colouring it
    /// here would tint every ghost button in the game.
    /// </summary>
    private static Button GhostIconButton(IconKind icon, string text, Color accent, Action onPressed)
    {
        var b = new Button
        {
            ThemeTypeVariation = "GhostButton",
            // 84 design px = 44pt = 6.9mm — the actual mobile touch-target floor. The previous
            // value was 56px and the comment above it claimed 56 "clears the mobile
            // touch-target floor"; 56px is 29.2pt / 4.55mm, 66% of the floor, so the note
            // stopped the next reviewer from checking a control that had never passed. If this
            // number is ever lowered again, the conversion is 1 design px = 0.521pt.
            // Width comes from the grid (half a column, ExpandFill) rather than a fixed 168,
            // so all five pills stay identical in size at any column width.
            CustomMinimumSize = new Vector2(0, TouchTarget),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        // The stock ghost pill is transparent fill + white α0.14 border (1.487:1) with a
        // gray icon — five of them made the bottom quarter of the screen fully achromatic.
        // A faint accent wash + accent-mixed border gives each pill an identity while the
        // LABEL stays TextSecondary, so text contrast is unchanged.
        // Border alpha 0.38, not 0.30: at 0.30 the boundary measures 2.32:1 (cyan) — still
        // under the 3:1 non-text bar it is supposed to clear. 0.38 lands cyan/gold at
        // ~3.0:1; violet tops out at 2.60:1 because the mixed hue is the darkest of the
        // set, and it stays there rather than shouting — the pill is also identified by
        // its icon AND its label, so the outline is not the only affordance.
        b.AddThemeStyleboxOverride("normal",
            TextureFactory.GlassStyle(Palette.RadiusM,
                new Color(accent.R, accent.G, accent.B, 0.05f), new Color(accent.R, accent.G, accent.B, 0.02f),
                TintRgb(Colors.White, accent, 0.45f, 0.38f), 1f, 0f, 24, 12));
        b.AddThemeStyleboxOverride("hover",
            TextureFactory.GlassStyle(Palette.RadiusM,
                new Color(accent.R, accent.G, accent.B, 0.12f), new Color(accent.R, accent.G, accent.B, 0.05f),
                new Color(accent.R, accent.G, accent.B, 0.55f), 1.2f, 0f, 24, 12));
        var content = CardContent(b, marginH: 0);
        content.Alignment = BoxContainer.AlignmentMode.Center;
        content.AddChild(new Theme.Icon(icon, new Color(accent.R, accent.G, accent.B, 0.85f), 20) { SizeFlagsVertical = SizeFlags.ShrinkCenter });
        var l = new Label { Text = text, ThemeTypeVariation = "DimLabel" };
        l.AddThemeFontSizeOverride("font_size", 18);
        content.AddChild(l);
        Motion.BindButtonFeel(b);
        TouchScroll.Bind(b, onPressed);
        return b;
    }

    private void BuildFooter()
    {
        var footer = new Label
        {
            // Trademark rule (CLAUDE.md §8-1): no third-party mark may appear anywhere,
            // and this label renders on the menu on every launch — it was the single
            // highest-exposure violation in the build. The disclaimer SLOT is kept
            // (BottomWide, TextTertiary 12px = 3.99:1 on the background, legible); only
            // the wording changed. Final legal copy is publishing's call.
            Text = Loc.T("ORIGINAL BRAND · ALL ART AND RULES ARE OUR OWN"),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        footer.AddThemeFontSizeOverride("font_size", 12);
        footer.AddThemeColorOverride("font_color", Palette.TextTertiary);
        AddChild(footer);
        footer.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        footer.OffsetTop = -34;
        footer.OffsetBottom = -10;
        // The 24px slot is smaller than the label's own minimum height once the
        // accessibility TEXT SIZE slider is raised, and Godot then grows the control
        // past its offsets. Measured headless: the footer ran 2px BELOW the screen
        // bottom (rect end 1282 in a 1280 canvas) — i.e. straight into the home
        // indicator. Growing toward the top pins the bottom edge at -10 instead.
        footer.GrowVertical = GrowDirection.Begin;
    }

    // ---- Small builders -----------------------------------------------------

    /// <summary>
    /// "LIT GLASS" card button: the card's identity colour now tints the WHOLE surface
    /// instead of only the 4px bar (which touched ~2% of the card's area, leaving three
    /// near-identical gray slabs). Body/border contrast against the background midpoint:
    /// cyan 1.711 / gold 1.770 / red 1.548 / violet 1.566 (was 1.168 flat), borders
    /// 2.85–4.37:1. The tint is the FOURTH channel of identity, never the only one —
    /// every card keeps its icon, its label and its fixed position in the column, so the
    /// screen still parses with hue removed entirely.
    /// </summary>
    private static Button Card(Color accent, float minHeight)
    {
        var b = new Button { CustomMinimumSize = new Vector2(0, minHeight) };
        var top = TintRgb(UiTheme.SurfaceTop, accent, 0.45f, 0.24f);
        var bottom = TintRgb(UiTheme.SurfaceBottom, accent, 0.25f, 0.07f);
        var border = TintRgb(Colors.White, accent, 0.45f, 0.44f);
        b.AddThemeStyleboxOverride("normal",
            TextureFactory.GlassStyle(Palette.RadiusM, top, bottom, border, 1.2f, 0.22f, 18, 12));
        // Hover/pressed keep the shipped recipe SHAPE but are re-based on the lit normal —
        // the old literals (0.145 / 0.18 alpha) now sit BELOW the new normal, which would
        // have made hovering a card visibly darken it.
        b.AddThemeStyleboxOverride("hover",
            TextureFactory.GlassStyle(Palette.RadiusM,
                Mul(top, 1.5f), Mul(bottom, 1.5f),
                new Color(accent.R, accent.G, accent.B, 0.6f), 1.4f, 0.26f, 18, 12));
        b.AddThemeStyleboxOverride("pressed",
            TextureFactory.GlassStyle(Palette.RadiusM,
                new Color(accent.R, accent.G, accent.B, 0.30f), new Color(accent.R, accent.G, accent.B, 0.14f),
                new Color(accent.R, accent.G, accent.B, 0.9f), 1.4f, 0.12f, 18, 12));
        var focus = TextureFactory.GlassStyle(Palette.RadiusM,
            new Color(0, 0, 0, 0), new Color(0, 0, 0, 0),
            new Color(accent.R, accent.G, accent.B, 0.8f), 1.6f, 0f, 18, 12);
        focus.DrawCenter = false;
        b.AddThemeStyleboxOverride("focus", focus);
        Motion.BindButtonFeel(b);
        return b;
    }

    /// <summary>Lerp a base colour's RGB toward an accent, then pin an explicit alpha.</summary>
    private static Color TintRgb(Color baseCol, Color accent, float mix, float alpha)
        => new(Mathf.Lerp(baseCol.R, accent.R, mix),
               Mathf.Lerp(baseCol.G, accent.G, mix),
               Mathf.Lerp(baseCol.B, accent.B, mix),
               alpha);

    /// <summary>
    /// The one idle motion on this screen: a slow iridescent sweep across the hero card,
    /// 6s period. Exactly one moving element means nothing competes for the eye, and it
    /// answers "is this thing running?" without a particle budget. Driven by accumulated
    /// dt (never wall-clock); under reduced motion the phase is frozen mid-sweep so the
    /// card keeps a static highlight instead of losing the treatment (same fallback the
    /// Holographic block finish uses).
    /// </summary>
    private void AttachSpecularSweep(Control card)
    {
        var strip = TextureFactory.HoloStrip(64);
        // Inset past the 14px corner radius (the arc bites ~4.1px in), so a rectangular
        // overlay can never spill a square halo outside the rounded card.
        var sweep = new Control { MouseFilter = MouseFilterEnum.Ignore };
        sweep.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        sweep.OffsetLeft = 6; sweep.OffsetTop = 6;
        sweep.OffsetRight = -6; sweep.OffsetBottom = -6;
        sweep.Draw += () =>
        {
            var size = sweep.Size;
            if (size.X <= 1f || size.Y <= 1f) return;
            float px = strip.GetHeight() * 0.5f;           // the scrolling window height
            float srcY = Motion.Reduced ? px * 0.5f : Mathf.PosMod(_sweepT * px / 6f, px);
            sweep.DrawTextureRectRegion(strip, new Rect2(Vector2.Zero, size),
                new Rect2(0, srcY, strip.GetWidth(), px), new Color(1, 1, 1, 0.06f));
        };
        card.AddChild(sweep);
        _sweep = sweep;
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

    /// <summary>
    /// The card's identity edge: a 4px round-capped core with a 5px outward glow falloff,
    /// so it reads as a lit neon strip rather than a flat paint chip. Baked once per
    /// (width, height) and tinted with Modulate, so all four cards together add zero
    /// per-frame draw work over the old ColorRect.
    /// </summary>
    private static Control AccentBar(Color color, float height)
    {
        int h = Mathf.Max(4, Mathf.RoundToInt(height));
        return new TextureRect
        {
            Texture = AccentEdge(AccentEdgeWidth, h),
            Modulate = color,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(AccentEdgeWidth, h),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        };
    }

    private const int AccentEdgeWidth = 10; // 4px visible core + 5px soft outward falloff
    private const float AccentEdgeCore = 4f;
    private const float AccentEdgeGlow = 5f;

    private static readonly Dictionary<string, ImageTexture> EdgeCache = new();

    /// <summary>
    /// Code-baked neon edge strip, white so callers tint via Modulate. Asset-free by rule
    /// (no imported images anywhere in the presentation layer).
    /// </summary>
    private static ImageTexture AccentEdge(int w, int h)
    {
        string key = $"accentedge:{w}:{h}";
        if (EdgeCache.TryGetValue(key, out var hit)) return hit;

        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        float radius = AccentEdgeCore * 0.20f;                 // rounded caps on the core
        var half = new Vector2(AccentEdgeCore * 0.5f, h * 0.5f);
        var centre = new Vector2(AccentEdgeCore * 0.5f, h * 0.5f);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f) - centre;
                // Rounded-box SDF of the solid core.
                var q = new Vector2(Mathf.Abs(p.X), Mathf.Abs(p.Y)) - half + new Vector2(radius, radius);
                float sdf = new Vector2(Mathf.Max(q.X, 0f), Mathf.Max(q.Y, 0f)).Length()
                            + Mathf.Min(Mathf.Max(q.X, q.Y), 0f) - radius;
                float core = Mathf.Clamp(0.5f - sdf / 1.5f, 0f, 1f);
                float t = Mathf.Clamp(sdf / AccentEdgeGlow, 0f, 1f);
                float glow = 0.55f * (1f - t) * (1f - t);       // 0.55 → 0 over 5px
                img.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Max(core, glow)));
            }
        }
        var tex = ImageTexture.CreateFromImage(img);
        EdgeCache[key] = tex;
        return tex;
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
        GameModeId.Descent => Loc.T("DESCENT"),
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
        GameModeId.Descent => Loc.T("DRAFT CHARMS, DIVE DEEP"),
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
        GameModeId.Descent => IconKind.Dice,
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
