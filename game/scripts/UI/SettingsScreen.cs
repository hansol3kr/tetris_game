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
/// </summary>
public partial class SettingsScreen : Control
{
    public event Action? BackRequested;

    private GameSettings _s = null!;
    private VBoxContainer _skinRows = null!;
    private VBoxContainer _controlRows = null!;

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
        outer.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });

        var col = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            CustomMinimumSize = new Vector2(560, 0),
        };
        col.AddThemeConstantOverride("separation", 14);
        outer.AddChild(col);
        outer.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });

        var titleRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        titleRow.AddThemeConstantOverride("separation", 12);
        titleRow.AddChild(new Theme.Icon(IconKind.Gear, Palette.Accent, 30) { SizeFlagsVertical = SizeFlags.ShrinkCenter });
        var title = new Label { Text = Loc.T("SETTINGS"), ThemeTypeVariation = "TitleLabel" };
        title.AddThemeFontSizeOverride("font_size", 36);
        titleRow.AddChild(title);
        col.AddChild(titleRow);

        // ---- LANGUAGE (first, so a new player can switch before anything else) --
        BuildLanguageSection(col);

        // ---- SKIN (background + block colors) --------------------------------
        BuildSkinSection(col);

        // ---- AUDIO -----------------------------------------------------------
        var audio = SectionCard(col, "AUDIO");
        audio.AddChild(Slider("SFX VOLUME", 0, 1, 0.05, _s.SfxVolume, v => { _s.SfxVolume = (float)v; ApplyAudio(); }, Pct));
        audio.AddChild(Slider("MUSIC VOLUME", 0, 1, 0.05, _s.MusicVolume, v => { _s.MusicVolume = (float)v; ApplyAudio(); }, Pct));
        audio.AddChild(Check("MUTE", _s.Muted, on => { _s.Muted = on; ApplyAudio(); }));

        // ---- VISUAL -----------------------------------------------------------
        var visual = SectionCard(col, "VISUAL");
        visual.AddChild(Check("GHOST PIECE", _s.GhostEnabled, on => _s.GhostEnabled = on));
        visual.AddChild(Check("COLORBLIND PALETTE", _s.ColorblindMode, on => { _s.ColorblindMode = on; Palette.ColorblindMode = on; }));
        visual.AddChild(Check("FINESSE METER", _s.ShowFinesse, on => _s.ShowFinesse = on));
        if (Bootstrap.GlowSupported)
        {
            visual.AddChild(Check("NEON GLOW (BLOOM)", _s.GlowEnabled, on =>
            {
                _s.GlowEnabled = on;
                Bootstrap.Instance.ApplyGlowSetting(); // live: HDR 2D + bloom together
            }));
        }
        else
        {
            // HDR 2D bloom blanks the canvas on this platform's driver — the neon
            // look still comes through the hand-drawn glow underlays. See Bootstrap.
            visual.AddChild(DisabledRow("NEON GLOW (BLOOM)", "UNAVAILABLE ON THIS DEVICE"));
        }
        visual.AddChild(Check("REDUCED MOTION", _s.ReducedMotion, on =>
        {
            _s.ReducedMotion = on;
            Motion.Reduced = on;
            Bootstrap.Instance.Bg.ApplyMotionSetting(); // freeze/unfreeze the backdrop
        }));
        visual.AddChild(Slider("JUICE / SHAKE", 0, 1, 0.1, _s.JuiceIntensity, v => _s.JuiceIntensity = (float)v, Pct));
        // Desktop-only: mobile is always fullscreen, so the toggle would be a no-op there.
        if (!OS.HasFeature("mobile"))
            visual.AddChild(Check("FULLSCREEN", _s.Fullscreen, on => { _s.Fullscreen = on; Bootstrap.Instance.ApplyFullscreen(); }));

        // ---- HANDLING ----------------------------------------------------------
        var handling = SectionCard(col, "HANDLING");
        handling.AddChild(Slider("DAS (DELAY)", 0.0, 0.30, 0.005, _s.DasSeconds, v => _s.DasSeconds = v, Ms));
        handling.AddChild(Slider("ARR (REPEAT)", 0.0, 0.10, 0.005, _s.ArrSeconds, v => _s.ArrSeconds = v, Ms));
        // Touch scheme: drag the piece into place vs. the classic on-screen d-pad.
        handling.AddChild(Check("DRAG CONTROLS (TOUCH)", _s.GestureControls, on => _s.GestureControls = on));

        // ---- CONTROLS (keyboard remap) ----------------------------------------
        BuildControlsSection(col);

        // ---- ACCESSIBILITY -----------------------------------------------------
        var access = SectionCard(col, "ACCESSIBILITY");
        access.AddChild(Slider("TEXT SIZE", 0.8, 1.6, 0.1, _s.TextScale,
            v => { _s.TextScale = (float)v; UiTheme.SetScale((float)v); }, Pct));

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

        var hint = new Label
        {
            Text = Loc.T("CLICK A KEY, THEN PRESS THE NEW ONE"),
            ThemeTypeVariation = "DimLabel",
        };
        hint.AddThemeFontSizeOverride("font_size", 13);
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
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
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
    /// player bought too). Picking one retints blocks + backdrop instantly and
    /// persists via <see cref="SaveManager.EquipTheme"/>. Premium skins live in the
    /// Store; this is the free, always-available appearance control.
    /// </summary>
    private void BuildSkinSection(Container parent)
    {
        var card = SectionCard(parent, "SKIN");

        var hint = new Label
        {
            Text = Loc.T("BACKGROUND & BLOCK COLORS · APPLIES INSTANTLY"),
            ThemeTypeVariation = "DimLabel",
        };
        hint.AddThemeFontSizeOverride("font_size", 13);
        card.AddChild(hint);

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
            CustomMinimumSize = new Vector2(0, 60),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        Motion.BindButtonFeel(b);

        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        row.OffsetLeft = 16; row.OffsetRight = -16;
        row.AddThemeConstantOverride("separation", 14);
        b.AddChild(row);

        var info = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        info.AddThemeConstantOverride("separation", 5);
        var name = new Label { Text = Loc.T(item.Name) };
        name.AddThemeFontOverride("font", Fonts.UiBold);
        name.AddThemeFontSizeOverride("font_size", 18);
        if (active) name.AddThemeColorOverride("font_color", Palette.Accent);
        info.AddChild(name);
        if (item.Theme is { } theme) info.AddChild(SwatchStrip(theme));
        row.AddChild(info);

        if (active)
        {
            var chip = new Label { Text = "✓ " + Loc.T("ACTIVE"), SizeFlagsVertical = SizeFlags.ShrinkCenter };
            chip.AddThemeFontOverride("font", Fonts.UiBold);
            chip.AddThemeFontSizeOverride("font_size", 14);
            chip.AddThemeColorOverride("font_color", Palette.Accent);
            row.AddChild(chip);
        }

        b.Pressed += () => EquipSkin(item);
        return b;
    }

    private void EquipSkin(StoreItem item)
    {
        var save = Bootstrap.Instance.Save;
        if (save.EquippedThemeId == item.Id) return;
        save.EquipTheme(item.Id);
        Palette.ApplyTheme(item.Theme);
        Bootstrap.Instance.Bg.ApplyThemeColors(); // backdrop retints live
        RebuildSkins();
    }

    /// <summary>Seven mini piece-color swatches — the skin preview.</summary>
    private static Control SwatchStrip(BlockTheme t)
    {
        var strip = new HBoxContainer();
        strip.AddThemeConstantOverride("separation", 4);
        foreach (var c in new[] { t.I, t.O, t.T, t.S, t.Z, t.J, t.L })
            strip.AddChild(new ColorRect
            {
                Color = c,
                CustomMinimumSize = new Vector2(22, 12),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        return strip;
    }

    // ---- Section / row builders ------------------------------------------------

    /// <summary>A glass card with a tracked section header; returns the rows container.</summary>
    private static VBoxContainer SectionCard(Container parent, string caption)
    {
        var header = new Label { Text = Loc.T(caption), ThemeTypeVariation = "SectionLabel" };
        parent.AddChild(header);

        var card = new PanelContainer { ThemeTypeVariation = "Card" };
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        card.AddChild(box);
        parent.AddChild(card);
        return box;
    }

    private Control Slider(string caption, double min, double max, double step, double value,
                           Action<double> onChanged, Func<double, string> fmt)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);

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
            CustomMinimumSize = new Vector2(76, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        valLabel.AddThemeColorOverride("font_color", Palette.Accent);
        valLabel.AddThemeFontSizeOverride("font_size", 17);
        slider.ValueChanged += v => { valLabel.Text = fmt(v); onChanged(v); };

        row.AddChild(slider);
        row.AddChild(valLabel);
        return row;
    }

    private Control Check(string caption, bool value, Action<bool> onChanged)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        row.AddChild(Caption(caption));
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var box = new CheckBox { ButtonPressed = value };
        box.Toggled += on => onChanged(on);
        row.AddChild(box);
        return row;
    }

    /// <summary>A dimmed, non-interactive row: a caption plus a status note (e.g. unsupported toggle).</summary>
    private Control DisabledRow(string caption, string note)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        var c = Caption(caption);
        c.AddThemeColorOverride("font_color", Palette.TextTertiary);
        row.AddChild(c);
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var n = new Label { Text = Loc.T(note), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        n.AddThemeFontSizeOverride("font_size", 14);
        n.AddThemeColorOverride("font_color", Palette.TextTertiary);
        row.AddChild(n);
        return row;
    }

    private static Label Caption(string text)
    {
        var l = new Label { Text = Loc.T(text), CustomMinimumSize = new Vector2(210, 0) };
        l.AddThemeFontSizeOverride("font_size", 18);
        l.AddThemeColorOverride("font_color", Palette.TextPrimary);
        return l;
    }

    private static string Pct(double v) => $"{Mathf.RoundToInt((float)v * 100)}%";
    private static string Ms(double v) => $"{Mathf.RoundToInt((float)v * 1000)} ms";
}
