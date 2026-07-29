using Godot;
using System;
using Blockfall.Core.Localization;
using Blockfall.Gameplay;
using Blockfall.Platform;
using Blockfall.Theme;

namespace Blockfall.UI;

/// <summary>
/// Player options grouped into glass cards: audio, visual comfort (ghost,
/// colorblind palette, glow, reduced motion, juice intensity), and handling
/// (DAS/ARR). Audio, palette, glow, and motion apply live; handling knobs take
/// effect on the next run. Changes are held in the shared settings object and
/// flushed to disk once when leaving via <see cref="BackRequested"/>.
///
/// Visual language (legibility pass): every group leads with an accent tab-marker
/// header + hairline-ruled rows at a fixed touch height, and every on/off option is a
/// pill switch (ON/OFF, accent-lit) instead of the tiny stock checkbox — state reads
/// at a glance on both desktop and touch.
/// </summary>
public partial class SettingsScreen : Control
{
    public event Action? BackRequested;
    public event Action? ReplayTutorialRequested;

    private GameSettings _s = null!;
    private VBoxContainer _skinRows = null!;
    private VBoxContainer _controlRows = null!;

    // Row rhythm tokens — one place to retune the whole screen's density.
    // 84px = 44pt on the 720×1280 design canvas (1 design px ≈ 0.52pt on an iPhone SE3):
    // the platform minimum touch target. The old 52px row was 27pt — every control in it
    // was a near-miss on a phone.
    private const int TouchTarget = 84;
    private const int RowHeight = TouchTarget;   // min touch height for every option row
    private static readonly Color Hairline = new(1f, 1f, 1f, 0.05f); // between-row divider

    // Key-remap "listening" state: while an action is armed, the next physical key
    // pressed (via _UnhandledKeyInput) binds to it. Null = not listening.
    private string? _listeningAction;
    private Button? _listeningButton;

    public override void _Ready()
    {
        UiTheme.ApplyTo(this);
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _s = Bootstrap.Instance.Save.Settings;
        BuildUi();
    }

    /// <summary>Builds (or rebuilds, after a language switch) the whole options tree.</summary>
    private void BuildUi()
    {
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(scroll);

        var outer = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        scroll.AddChild(outer);
        outer.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });

        var col = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            CustomMinimumSize = new Vector2(560, 0),
        };
        col.AddThemeConstantOverride("separation", 16);
        outer.AddChild(col);
        outer.AddChild(new Control { CustomMinimumSize = new Vector2(0, 24) });

        var titleRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        titleRow.AddThemeConstantOverride("separation", 12);
        titleRow.AddChild(new Theme.Icon(IconKind.Gear, Palette.Accent, 30) { SizeFlagsVertical = SizeFlags.ShrinkCenter });
        var title = new Label { Text = Loc.T("SETTINGS"), ThemeTypeVariation = "TitleLabel" };
        title.AddThemeFontSizeOverride("font_size", 36);
        titleRow.AddChild(title);
        col.AddChild(titleRow);

        // ---- LANGUAGE (first, so a new player can switch before anything else) --
        BuildLanguageSection(col);

        // ---- SKIN (block colors only — the game screen background never changes) --
        BuildSkinSection(col);

        // ---- AUDIO -----------------------------------------------------------
        var audio = SectionCard(col, "AUDIO");
        AddRow(audio, Slider("SFX VOLUME", 0, 1, 0.05, _s.SfxVolume, v => { _s.SfxVolume = (float)v; ApplyAudio(); }, Pct));
        AddRow(audio, Slider("MUSIC VOLUME", 0, 1, 0.05, _s.MusicVolume, v => { _s.MusicVolume = (float)v; ApplyAudio(); }, Pct));
        AddRow(audio, Check("MUTE", _s.Muted, on => { _s.Muted = on; ApplyAudio(); }));
        // Placement timbre. Audition on every tap — a sound picker you cycle in silence
        // is unusable, and the packs differ only in timbre, so a name tells you nothing.
        AddRow(audio, CycleRow(Loc.T("PLACE SOUND"), Blockfall.Audio.AudioManager.PackNames, _s.SfxPack,
            i => { _s.SfxPack = i; Bootstrap.Instance.Audio.PlayPlace(); }));

        // ---- VISUAL -----------------------------------------------------------
        var visual = SectionCard(col, "VISUAL");
        AddRow(visual, Check("GHOST PIECE", _s.GhostEnabled, on => _s.GhostEnabled = on));
        AddRow(visual, Check("COLORBLIND PALETTE", _s.ColorblindMode, on => { _s.ColorblindMode = on; Palette.ColorblindMode = on; }));
        AddRow(visual, Check("FINESSE METER", _s.ShowFinesse, on => _s.ShowFinesse = on));
        if (Bootstrap.GlowSupported)
        {
            AddRow(visual, Check("NEON GLOW (BLOOM)", _s.GlowEnabled, on =>
            {
                _s.GlowEnabled = on;
                Bootstrap.Instance.ApplyGlowSetting(); // live: HDR 2D + bloom together
            }));
        }
        else
        {
            // HDR 2D bloom blanks the canvas on this platform's driver — the neon
            // look still comes through the hand-drawn glow underlays. See Bootstrap.
            AddRow(visual, DisabledRow("NEON GLOW (BLOOM)", "UNAVAILABLE ON THIS DEVICE"));
        }
        AddRow(visual, Check("REDUCED MOTION", _s.ReducedMotion, on =>
        {
            _s.ReducedMotion = on;
            Motion.Reduced = on;
            Bootstrap.Instance.Bg.ApplyMotionSetting(); // freeze/unfreeze the backdrop
        }));
        AddRow(visual, Slider("JUICE / SHAKE", 0, 1, 0.1, _s.JuiceIntensity, v => _s.JuiceIntensity = (float)v, Pct));
        AddRow(visual, CycleRow(Loc.T("CLEAR EFFECT"), JuiceLayer.ClearFxNames, _s.ClearFxStyle, i => _s.ClearFxStyle = i));
        // Desktop-only: mobile is always fullscreen, so the toggle would be a no-op there.
        if (!OS.HasFeature("mobile"))
            AddRow(visual, Check("FULLSCREEN", _s.Fullscreen, on => { _s.Fullscreen = on; Bootstrap.Instance.ApplyFullscreen(); }));

        // ---- HANDLING ----------------------------------------------------------
        var handling = SectionCard(col, "HANDLING");
        AddRow(handling, Slider("DAS (DELAY)", 0.0, 0.30, 0.005, _s.DasSeconds, v => _s.DasSeconds = v, Ms));
        AddRow(handling, Slider("ARR (REPEAT)", 0.0, 0.10, 0.005, _s.ArrSeconds, v => _s.ArrSeconds = v, Ms));
        // Touch scheme: drag the piece into place vs. the classic on-screen d-pad.
        AddRow(handling, Check("DRAG CONTROLS (TOUCH)", _s.GestureControls, on => _s.GestureControls = on));

        // ---- CONTROLS (keyboard remap) ----------------------------------------
        BuildControlsSection(col);

        // ---- ACCESSIBILITY -----------------------------------------------------
        var access = SectionCard(col, "ACCESSIBILITY");
        AddRow(access, Slider("TEXT SIZE", 0.8, 1.6, 0.1, _s.TextScale,
            v => { _s.TextScale = (float)v; UiTheme.SetScale((float)v); }, Pct));

        // ---- HELP --------------------------------------------------------------
        // The tutorial is no longer forced on first launch, so it lives here (and
        // on the menu's HOW TO PLAY) as an opt-in replay.
        var help = SectionCard(col, "HELP");
        var replay = new Button { Text = Loc.T("REPLAY TUTORIAL"), CustomMinimumSize = new Vector2(0, 46) };
        Motion.BindButtonFeel(replay);
        replay.Pressed += () => { Bootstrap.Instance.Save.SetSettings(_s); ReplayTutorialRequested?.Invoke(); };
        help.AddChild(replay);

        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        var back = new Button
        {
            Text = Loc.T("BACK"),
            ThemeTypeVariation = "PrimaryButton",
            CustomMinimumSize = new Vector2(0, 54),
        };
        Motion.BindButtonFeel(back);
        back.Pressed += () => { Bootstrap.Instance.Save.SetSettings(_s); BackRequested?.Invoke(); };
        col.AddChild(back);
    }

    private void ApplyAudio() => Bootstrap.Instance.Audio.ApplySettings(_s);

    // ---- Language --------------------------------------------------------------

    /// <summary>A segmented row of the available languages; picking one switches live.</summary>
    private void BuildLanguageSection(Container parent)
    {
        var card = SectionCard(parent, "LANGUAGE");
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        foreach (var lang in Loc.Available)
        {
            bool active = _s.Language == lang;
            var b = new Button
            {
                Text = Loc.DisplayName(lang), // endonym — never translated
                ThemeTypeVariation = active ? "PrimaryButton" : "GhostButton",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 46),
            };
            Motion.BindButtonFeel(b);
            var chosen = lang;
            b.Pressed += () => SetLanguage(chosen);
            row.AddChild(b);
        }
        card.AddChild(row);
    }

    private void SetLanguage(Language lang)
    {
        if (_s.Language == lang) return;
        _s.Language = lang;
        Loc.Current = lang;                       // retranslate every Loc.T going forward
        Bootstrap.Instance.Save.SetSettings(_s);  // persist the choice immediately
        Rebuild();                                // reflect the new language across this screen
    }

    /// <summary>Tears down and rebuilds the whole screen in the newly-chosen language.</summary>
    private void Rebuild()
    {
        StopListening(); // the armed key button is about to be freed
        foreach (var child in GetChildren())
        {
            RemoveChild(child);   // detach now so the old tree stops rendering this frame
            child.QueueFree();
        }
        BuildUi();
    }

    // ---- Controls (keyboard remap) ---------------------------------------------

    /// <summary>
    /// A per-action list of key buttons: click one to "listen", then press the new
    /// key. A collision swaps the two actions so nothing is ever left unbound. The
    /// game reads input by action name, so this only repoints Godot's InputMap —
    /// no gameplay code is touched. Gamepad bindings are never affected.
    /// </summary>
    private void BuildControlsSection(Container parent)
    {
        var card = SectionCard(parent, "CONTROLS");

        var hint = Hint("CLICK A KEY, THEN PRESS THE NEW ONE");
        card.AddChild(hint);

        _controlRows = new VBoxContainer();
        _controlRows.AddThemeConstantOverride("separation", 8);
        card.AddChild(_controlRows);
        RebuildControls();

        var reset = new Button
        {
            Text = Loc.T("RESET TO DEFAULTS"),
            ThemeTypeVariation = "GhostButton",
            CustomMinimumSize = new Vector2(0, 46),
        };
        Motion.BindButtonFeel(reset);
        reset.Pressed += () =>
        {
            StopListening();
            KeyBinds.ResetDefaults(_s);
            Bootstrap.Instance.Save.SetSettings(_s);
            RebuildControls();
        };
        card.AddChild(reset);
    }

    private void RebuildControls()
    {
        foreach (var child in _controlRows.GetChildren()) ((Node)child).QueueFree();
        foreach (var b in KeyBinds.Bindable)
            _controlRows.AddChild(KeyRow(b.Action, b.Label));
    }

    private Control KeyRow(string action, string label)
    {
        var row = Row();
        row.AddChild(Caption(label));
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

        var btn = new Button
        {
            Text = KeyBinds.KeyName(KeyBinds.CurrentKey(_s, action)),
            ThemeTypeVariation = "GhostButton",
            CustomMinimumSize = new Vector2(150, 44),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        Motion.BindButtonFeel(btn);
        btn.Pressed += () => StartListening(action, btn);
        row.AddChild(btn);
        return row;
    }

    private void StartListening(string action, Button btn)
    {
        // Clicking the armed button again cancels; clicking a different one re-arms.
        if (_listeningAction == action) { StopListening(); RebuildControls(); return; }
        StopListening();
        _listeningAction = action;
        _listeningButton = btn;
        btn.Text = Loc.T("PRESS A KEY…");
        // Drop focus so the button doesn't swallow Space/Enter as an activation —
        // those keys must reach _UnhandledKeyInput to be bindable.
        btn.ReleaseFocus();
    }

    private void StopListening()
    {
        // Restore the armed button's label so a cancelled/switched listen doesn't
        // leave a stale "PRESS A KEY…" behind (harmless if it's already been freed).
        if (_listeningAction is { } a && _listeningButton is { } b && GodotObject.IsInstanceValid(b))
            b.Text = KeyBinds.KeyName(KeyBinds.CurrentKey(_s, a));
        _listeningAction = null;
        _listeningButton = null;
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (_listeningAction is not { } action) return;
        if (@event is not InputEventKey k || !k.Pressed || k.Echo) return;

        GetViewport().SetInputAsHandled();
        var key = k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;

        // Escape backs out of listening without binding (so it can't get trapped);
        // it stays the default Pause key and is restorable via reset.
        if (key != Key.Escape && key != Key.None)
        {
            KeyBinds.Rebind(_s, action, key);
            Bootstrap.Instance.Save.SetSettings(_s);
        }
        StopListening();
        RebuildControls(); // a swap may have changed another action's key too
    }

    // ---- Skins (free appearance picker) ----------------------------------------

    /// <summary>
    /// A gallery of the owned skins (the default + all free ones; any premium the
    /// player bought too). Picking one retints the blocks instantly and persists via
    /// <see cref="SaveManager.EquipTheme"/>. The game screen background is NOT part of
    /// a skin — it stays fixed so piece contrast reads the same under every skin.
    /// Premium skins live in the Store; this is the free, always-available control.
    /// </summary>
    private void BuildSkinSection(Container parent)
    {
        var card = SectionCard(parent, "SKIN");

        card.AddChild(Hint("BLOCK COLORS · APPLIES INSTANTLY"));

        _skinRows = new VBoxContainer();
        _skinRows.AddThemeConstantOverride("separation", 8);
        card.AddChild(_skinRows);
        RebuildSkins();

        var more = new Label
        {
            Text = Loc.T("MORE SKINS IN THE STORE"),
            ThemeTypeVariation = "SectionLabel",
        };
        card.AddChild(more);
    }

    private void RebuildSkins()
    {
        foreach (var child in _skinRows.GetChildren()) ((Node)child).QueueFree();
        var save = Bootstrap.Instance.Save;
        foreach (var item in StoreCatalog.Items)
        {
            if (item.Kind != StoreItemKind.Theme || !save.OwnsItem(item.Id)) continue;
            _skinRows.AddChild(SkinRow(item));
        }
    }

    private Control SkinRow(StoreItem item)
    {
        bool active = Bootstrap.Instance.Save.EquippedThemeId == item.Id;

        var b = new Button
        {
            ThemeTypeVariation = "CardButton",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        Motion.BindButtonFeel(b);

        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        row.OffsetLeft = 16; row.OffsetRight = -16;
        row.AddThemeConstantOverride("separation", 14);
        b.AddChild(row);

        var info = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        info.AddThemeConstantOverride("separation", InfoSep);
        var name = new Label { Text = Loc.T(item.Name) };
        name.AddThemeFontOverride("font", Fonts.UiBold);
        name.AddThemeFontSizeOverride("font_size", 18);
        if (active) name.AddThemeColorOverride("font_color", Palette.Accent);
        info.AddChild(name);
        ThemePreview? preview = null;
        if (item.Theme is { } theme)
        {
            preview = new ThemePreview(theme) { Selected = active };
            info.AddChild(preview);
        }
        row.AddChild(info);

        if (active)
        {
            var chip = new Label { Text = "✓ " + Loc.T("ACTIVE"), SizeFlagsVertical = SizeFlags.ShrinkCenter };
            chip.AddThemeFontOverride("font", Fonts.UiBold);
            chip.AddThemeFontSizeOverride("font_size", 14);
            chip.AddThemeColorOverride("font_color", Palette.Accent);
            row.AddChild(chip);
        }

        b.CustomMinimumSize = new Vector2(0, SkinRowHeight(name, preview));
        b.Pressed += () => EquipSkin(item);
        return b;
    }

    /// <summary>Vertical separation inside a skin row's info column (name over swatch).</summary>
    private const int InfoSep = 5;

    /// <summary>Breathing room above + below the info column inside the row.</summary>
    private const float SkinRowPad = 20f;

    /// <summary>Floor for a skin row: the 44pt touch target this list has always used.</summary>
    private const float SkinRowMinH = 60f;

    /// <summary>
    /// The height a skin row actually needs, derived from what is in it.
    ///
    /// This used to be a hard 60. A <see cref="Button"/> is NOT a <see cref="Container"/>: it
    /// never grows to fit children pinned inside it, and its own minimum size only counts its
    /// text and icon. Meanwhile every Control clamps its size UP to its combined minimum — so
    /// when <see cref="ThemePreview"/> grew from a 26px strip to a 122px 3×3 grid, the row's
    /// inner HBox was forced to ≈151px (name 18px ⇒ ≈25 + sep 5 + swatch 122) inside a 60px
    /// button and spilled ≈91px past it, straight over the neighbouring rows (they sit 8px
    /// apart). The store never showed this because <c>StoreScreen.Card()</c> is a real
    /// PanelContainer, which does absorb its children's minimum.
    ///
    /// The swatch is NOT shrunk to fit instead: 38px cells are what clear BlockRender's layer
    /// guards (glow ≥16, overlay ≥14, rim ≥16, gloss ≥12, glyph ≥18, glyph detail ≥26), and the
    /// settings gallery and the store must sell the same skin with the same picture. So the row
    /// grows to the content, and the content is measured — the label's real font metrics, the
    /// preview's own declared minimum — rather than re-hardcoded here.
    /// </summary>
    private static float SkinRowHeight(Label name, Control? preview)
    {
        float h = Mathf.Max(name.GetCombinedMinimumSize().Y, 25f);
        if (preview is not null) h += InfoSep + Mathf.Max(preview.CustomMinimumSize.Y, preview.GetCombinedMinimumSize().Y);
        return Mathf.Max(SkinRowMinH, h + SkinRowPad);
    }

    private void EquipSkin(StoreItem item)
    {
        var save = Bootstrap.Instance.Save;
        if (save.EquippedThemeId == item.Id) return;
        save.EquipTheme(item.Id);
        Palette.ApplyTheme(item.Theme);                     // retints blocks only — backdrop stays fixed
        Bootstrap.Instance.Bg.Pulse(Palette.Accent, 0.28f); // equip flourish (Motion.Reduced-gated)
        RebuildSkins();
    }

    // ---- Section / row builders ------------------------------------------------

    /// <summary>
    /// A titled group: an accent tab-marker header sitting just above its glass card.
    /// The header is brighter and tracked (vs. the stock dim SectionLabel) so a player
    /// scanning a long options list finds the group they want fast. Returns the rows box.
    /// </summary>
    private static VBoxContainer SectionCard(Container parent, string caption)
    {
        var group = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        group.AddThemeConstantOverride("separation", 8);
        parent.AddChild(group);

        var header = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        header.AddThemeConstantOverride("separation", 10);
        header.AddChild(new ColorRect
        {
            Color = Palette.Accent,
            CustomMinimumSize = new Vector2(3, 18),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        });
        var label = new Label
        {
            Text = Loc.T(caption),
            ThemeTypeVariation = "SectionHeaderLabel", // scale-aware — honors TEXT SIZE
            VerticalAlignment = VerticalAlignment.Center,
        };
        header.AddChild(label);
        group.AddChild(header);

        var card = new PanelContainer { ThemeTypeVariation = "Card" };
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        card.AddChild(box);
        group.AddChild(card);
        return box;
    }

    /// <summary>Add an option row to a card, seating a hairline divider before every row
    /// but the first so the list reads as clean, scannable lines.</summary>
    private static void AddRow(VBoxContainer card, Control row)
    {
        if (card.GetChildCount() > 0)
            card.AddChild(new ColorRect { Color = Hairline, CustomMinimumSize = new Vector2(0, 1) });
        card.AddChild(row);
    }

    /// <summary>A fixed-height row so every option lines up and stays a comfortable touch target.</summary>
    private static HBoxContainer Row()
    {
        var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, RowHeight) };
        row.AddThemeConstantOverride("separation", 16);
        return row;
    }

    /// <summary>A small secondary-contrast helper line under a section header (scale-aware).</summary>
    private static Label Hint(string text)
        => new() { Text = Loc.T(text), ThemeTypeVariation = "OptionHint" };

    private Control Slider(string caption, double min, double max, double step, double value,
                           Action<double> onChanged, Func<double, string> fmt)
    {
        var row = Row();
        row.AddChild(Caption(caption));

        var slider = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = value,
            CustomMinimumSize = new Vector2(220, 24),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        var valLabel = new Label
        {
            Text = fmt(value),
            ThemeTypeVariation = "OptionValue", // scale-aware bold accent readout
            CustomMinimumSize = new Vector2(80, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        slider.ValueChanged += v => { valLabel.Text = fmt(v); onChanged(v); };

        row.AddChild(slider);
        row.AddChild(valLabel);
        return row;
    }

    /// <summary>A caption + a button that cycles through <paramref name="names"/> on tap.</summary>
    private Control CycleRow(string caption, string[] names, int current, Action<int> onChanged)
    {
        var row = Row();
        row.AddChild(Caption(caption));
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        int idx = Mathf.Clamp(current, 0, names.Length - 1);
        // Full 44pt tall: this segment picker cycles on every tap, so a mis-tap on a 40px
        // strip meant scrolling past the value you wanted.
        // The VALUE is localized too, not just the caption — this was why CLEAR EFFECT
        // showed SPARKS/BURST/... in English on a Korean UI while its label was translated.
        var btn = new Button
        {
            Text = Loc.T(names[idx]),
            CustomMinimumSize = new Vector2(168, TouchTarget),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        Motion.BindButtonFeel(btn);
        btn.Pressed += () =>
        {
            idx = (idx + 1) % names.Length;
            btn.Text = Loc.T(names[idx]);
            onChanged(idx);
        };
        row.AddChild(btn);
        return row;
    }

    /// <summary>An on/off option row: a caption plus a pill switch (ON/OFF, accent-lit when on).</summary>
    private Control Check(string caption, bool value, Action<bool> onChanged)
    {
        var row = Row();
        row.AddChild(Caption(caption));
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        row.AddChild(Toggle(value, onChanged));
        return row;
    }

    /// <summary>
    /// A pill switch built on the ChipButton look (toggled-on = the accent-lit pressed
    /// state). Replaces the tiny stock CheckBox: the ON/OFF label + accent fill make the
    /// state unmistakable at a glance.
    ///
    /// The old 82×40 pill was NOT "a proper touch target" as this comment used to claim —
    /// 40px is 21pt on an iPhone SE3, under half the 44pt floor. The tappable button is now
    /// <see cref="TouchTarget"/> square-ish (108×84 = 56×44pt) with the pill DRAWN inside it
    /// at the original slim height: growing the art to 84px would turn the switch into a
    /// slab, so only the hit box grew.
    /// </summary>
    private Button Toggle(bool value, Action<bool> onChanged)
    {
        var t = new Button
        {
            ToggleMode = true,
            ButtonPressed = value,
            CustomMinimumSize = new Vector2(108, TouchTarget),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            FocusMode = FocusModeEnum.None,
        };
        // The hit box itself paints nothing — the pill child below is the whole visual.
        foreach (var state in new[] { "normal", "hover", "pressed", "focus", "disabled" })
            t.AddThemeStyleboxOverride(state, new StyleBoxEmpty());

        var pillOff = UiTheme.Shared.GetStylebox("normal", "ChipButton");
        var pillOn = UiTheme.Shared.GetStylebox("pressed", "ChipButton");
        var pill = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        pill.AddThemeStyleboxOverride("panel", value ? pillOn : pillOff);
        t.AddChild(pill);
        pill.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        pill.OffsetTop = (TouchTarget - 40f) / 2f;      // 40px pill centred in the 84px target
        pill.OffsetBottom = -(TouchTarget - 40f) / 2f;

        var label = new Label
        {
            Text = value ? Loc.T("ON") : Loc.T("OFF"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 15);
        label.AddThemeColorOverride("font_color", value ? Palette.Accent : Palette.TextSecondary);
        pill.AddChild(label);
        label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        Motion.BindButtonFeel(t);
        t.Toggled += on =>
        {
            pill.AddThemeStyleboxOverride("panel", on ? pillOn : pillOff);
            label.Text = on ? Loc.T("ON") : Loc.T("OFF");
            label.AddThemeColorOverride("font_color", on ? Palette.Accent : Palette.TextSecondary);
            onChanged(on);
        };
        return t;
    }

    /// <summary>A dimmed, non-interactive row: a caption plus a status note (e.g. unsupported toggle).</summary>
    private Control DisabledRow(string caption, string note)
    {
        var row = Row();
        var c = Caption(caption);
        c.AddThemeColorOverride("font_color", Palette.TextTertiary);
        row.AddChild(c);
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var n = new Label
        {
            Text = Loc.T(note),
            ThemeTypeVariation = "OptionHint", // scale-aware; recoloured dimmer below
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        n.AddThemeColorOverride("font_color", Palette.TextTertiary);
        row.AddChild(n);
        return row;
    }

    private static Label Caption(string text)
        => new()
        {
            Text = Loc.T(text),
            ThemeTypeVariation = "OptionLabel", // scale-aware — every option row honors TEXT SIZE
            CustomMinimumSize = new Vector2(210, 0),
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };

    private static string Pct(double v) => $"{Mathf.RoundToInt((float)v * 100)}%";
    private static string Ms(double v) => $"{Mathf.RoundToInt((float)v * 1000)} ms";
}
