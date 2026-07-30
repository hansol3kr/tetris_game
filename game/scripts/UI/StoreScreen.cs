using Godot;
using System;
using Blockfall.Platform;
using Blockfall.Theme;
using Blockfall.Core.Localization;

namespace Blockfall.UI;

/// <summary>
/// The store, organised as an app shell rather than one long scroll: title, a CATEGORY TAB BAR and
/// BACK are pinned, and only the selected category's rows live in the scrolling middle.
///
/// <para>WHY THE TABS EXIST. This screen used to append every section into a single flat column —
/// 34 skins, then 17 bursts, then the sound packs, then boosters — so the burst FX shelf began
/// roughly 4,200px down and the player's own report was that you had to scroll to the very bottom
/// to find out effects existed at all. Merchandise the player cannot reach is merchandise that
/// does not exist. Tabs also fix the cost side: <see cref="Rebuild"/> now builds ONE category, so
/// equipping something re-creates ~10 cards instead of ~60, each of which carries a live animated
/// preview.</para>
///
/// <para>WHY BACK AND THE TABS SIT OUTSIDE THE SCROLL. They were inside it, which meant the exit
/// receded as the catalog grew (and this screen has no Esc handler of its own — it does now).
/// Pinning them is what makes "more content" a safe thing to keep doing.</para>
///
/// <para>Payment runs through <see cref="IPlatformServices.PurchaseItem"/>; on success THIS screen
/// grants the item via SaveManager (platforms only handle money). Equipping applies instantly and
/// app-wide through <c>Bootstrap.ApplySkin()</c> — pieces, UI accent and the backdrop scene.</para>
///
/// <para>Section contract, unchanged and still load-bearing: EVERY cosmetic kind in
/// <see cref="StoreCatalog"/> must appear in <see cref="Categories"/> AND have a row builder. A
/// kind with catalog rows and no tab is invisible merchandise — which is exactly what happened to
/// <see cref="StoreItemKind.SoundPack"/> once already.</para>
/// </summary>
public partial class StoreScreen : Control
{
    public event Action? BackRequested;

    /// <summary>44pt at this project's 0.521pt-per-design-px scale — the repo-wide minimum touch
    /// target (<c>SettingsScreen.TouchTarget</c> and <c>MainMenu</c> hold the same number).</summary>
    private const int TouchTarget = 84;

    /// <summary>One shelf. <c>Kinds</c> is what the tab draws from the catalog, so adding a kind to
    /// an existing shelf is a one-line change and adding a shelf is one entry here plus a row
    /// builder — the pairing the class doc calls the section contract.</summary>
    private readonly record struct Category(string Label, StoreItemKind[] Kinds);

    private static readonly Category[] Categories =
    {
        new("SKINS",  new[] { StoreItemKind.Theme }),
        new("SCENES", new[] { StoreItemKind.Backdrop }),
        new("FX",     new[] { StoreItemKind.Artifact }),
        new("SOUND",  new[] { StoreItemKind.SoundPack }),
        new("EXTRAS", new[] { StoreItemKind.BoosterPack, StoreItemKind.RemoveAds }),
    };

    private VBoxContainer _list = null!;
    private TouchScroll _scroll = null!;
    private HFlowContainer _tabs = null!;
    private int _tab;

    /// <summary>Two stable passes over the catalog: flagged rows, then the rest. Keeps the
    /// declaration order inside each group (no comparer, no shuffling of the familiar list).</summary>
    private static readonly bool[] NewFirst = { true, false };

    public override void _Ready()
    {
        UiTheme.ApplyTo(this);
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Shell: title / tabs / scroll / back. A plain VBox, NOT a scrolled column — the middle
        // child is the only thing that scrolls, which is what keeps the tabs and the exit on
        // screen no matter how deep the shelf gets.
        var shell = new VBoxContainer();
        shell.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        shell.AddThemeConstantOverride("separation", 10);
        AddChild(shell);

        // Width tracks the device the way MainMenu's column does instead of pinning 520px, so the
        // tab row has room to stay on ONE line on a phone and does not sprawl on a tablet.
        float colW = Mathf.Clamp(Bootstrap.Instance.SafeCanvasSize.X * 0.94f, 340f, 600f);

        shell.AddChild(Centered(TitleRow(), colW));
        _tabs = new HFlowContainer();
        _tabs.AddThemeConstantOverride("h_separation", 8);
        _tabs.AddThemeConstantOverride("v_separation", 8);
        shell.AddChild(Centered(_tabs, colW));

        _scroll = new TouchScroll
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        shell.AddChild(_scroll);

        // The scrolled content is centred at the same width as the chrome above it.
        var inner = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _scroll.AddChild(inner);
        inner.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _list = new VBoxContainer { CustomMinimumSize = new Vector2(colW, 0) };
        _list.AddThemeConstantOverride("separation", 12);
        inner.AddChild(_list);
        inner.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

        var back = new Button
        {
            Text = Loc.T("BACK"),
            ThemeTypeVariation = "PrimaryButton",
            CustomMinimumSize = new Vector2(0, TouchTarget),
        };
        Motion.BindButtonFeel(back);
        back.Pressed += () => BackRequested?.Invoke();
        shell.AddChild(Centered(back, colW));

        BuildTabs();
        Rebuild();
    }

    /// <summary>Esc / the pause binding leaves the store. Every other list screen honours it
    /// (ProfileScreen does); this one did not, so on a controller or a keyboard the only exit was
    /// a mouse click on a button that used to be below the fold.</summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("pause_game") || @event.IsActionPressed("ui_cancel"))
        {
            BackRequested?.Invoke();
            GetViewport().SetInputAsHandled();
        }
    }

    private static Control Centered(Control child, float width)
    {
        child.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var slot = new VBoxContainer { CustomMinimumSize = new Vector2(width, 0) };
        slot.AddChild(child);
        row.AddChild(slot);
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        return row;
    }

    private Control TitleRow()
    {
        // Centre by ALIGNMENT, not by ShrinkCenter: Centered() gives this row the full column
        // width, and a shrink-centred box inside a stretched slot lands left.
        var titleRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        titleRow.AddThemeConstantOverride("separation", 12);
        titleRow.AddChild(new Theme.Icon(IconKind.Diamond, Palette.AccentGold, 30) { SizeFlagsVertical = SizeFlags.ShrinkCenter });
        var title = new Label { Text = Loc.T("STORE"), ThemeTypeVariation = "TitleLabel" };
        title.AddThemeFontSizeOverride("font_size", 36);
        titleRow.AddChild(title);
        return titleRow;
    }

    // ---- Tabs -------------------------------------------------------------------

    /// <summary>
    /// The tab bar, built from <see cref="Categories"/>. Chips, not a Godot
    /// <c>TabBar</c>: <see cref="UiTheme"/> defines no TabBar styling at all, so a stock one would
    /// render as unthemed grey in the middle of a neon screen, and "ChipButton" is already this
    /// design system's pill-toggle vocabulary (the modifier chips on the menu).
    /// <para>An <see cref="HFlowContainer"/> rather than a fixed grid because the labels are
    /// different lengths: five chips fit one line at the normal column width and wrap by
    /// themselves on a narrow device or a raised accessibility text scale, instead of clipping.</para>
    /// </summary>
    private void BuildTabs()
    {
        foreach (var child in _tabs.GetChildren()) ((Node)child).QueueFree();

        for (int i = 0; i < Categories.Length; i++)
        {
            int idx = i;
            var cat = Categories[i];
            var chip = new Button
            {
                Text = Loc.T(cat.Label),
                ThemeTypeVariation = "ChipButton",
                ToggleMode = true,
                ButtonPressed = idx == _tab,
                CustomMinimumSize = new Vector2(0, TouchTarget),
                FocusMode = FocusModeEnum.All,
            };
            chip.AddThemeFontSizeOverride("font_size", 17);
            Motion.BindButtonFeel(chip);
            // Toggled, not Pressed: a ChipButton draws its "on" state from the pressed stylebox,
            // so the selection has to BE the toggle state rather than be tracked beside it.
            chip.Toggled += on =>
            {
                if (!on) { if (idx == _tab) chip.SetPressedNoSignal(true); return; }   // never zero-selected
                if (idx == _tab) return;
                _tab = idx;
                SyncTabs();
                _scroll.ResetToTop();   // top of the new shelf — and kill any glide still running
                Rebuild();
            };
            _tabs.AddChild(chip);
        }
    }

    private void SyncTabs()
    {
        int i = 0;
        foreach (var child in _tabs.GetChildren())
            if (child is Button b) b.SetPressedNoSignal(i++ == _tab);
    }

    private bool Shows(StoreItemKind kind)
    {
        foreach (var k in Categories[_tab].Kinds) if (k == kind) return true;
        return false;
    }

    // ---- Rows -----------------------------------------------------------------

    /// <summary>Rebuild the SELECTED category's rows (called on tab change and after any
    /// purchase/equip so states refresh).</summary>
    private void Rebuild()
    {
        foreach (var child in _list.GetChildren())
        {
            // Detach NOW, then free: QueueFree is deferred to the end of the frame, so the doomed
            // rows are still parented — and still counted by GetChildCount — while this method
            // runs. That made the empty-shelf guard at the bottom permanently false, so the one
            // shelf that legitimately produces no rows rendered as a blank pane. (Same detach-then-
            // free idiom as SettingsScreen.Rebuild; GetChildren returns a snapshot, so mutating
            // the parent while iterating it is safe.)
            _list.RemoveChild(child);
            ((Node)child).QueueFree();
        }

        var platform = Bootstrap.Instance.Platform;
        var save = Bootstrap.Instance.Save;
        // On a mobile store build WITHOUT a real billing plugin, hide everything that carries a
        // price tag: buy buttons that don't charge (or do nothing) are an App Store review
        // rejection (guideline 3.1.1). Desktop dev/Steam keep the full store — their "purchases"
        // are grant-by-design.
        bool paidOk = !OS.HasFeature("mobile") || platform.SupportsIap;

        if (Shows(StoreItemKind.Theme))
        {
            // State the accessibility split instead of letting the player find it after paying:
            // previews render each skin's OWN colours (otherwise every card looks identical),
            // while the play field keeps the colour-safe palette. See Palette.PlanFill.
            if (Palette.ColorblindMode)
                _list.AddChild(Note(Loc.T("COLOR-SAFE PALETTE IS ON: THE BOARD KEEPS IT. PREVIEWS BELOW SHOW EACH SKIN'S OWN COLORS.")));
            _list.AddChild(Note(Loc.T("A SKIN NOW DRESSES THE WHOLE APP: BLOCKS, MENU ACCENT AND THE SCENE BEHIND EVERY SCREEN.")));
            foreach (bool fresh in NewFirst)
                foreach (var item in StoreCatalog.Items)
                    if (item.Kind == StoreItemKind.Theme && item.IsNew == fresh && (paidOk || save.OwnsItem(item.Id)))
                        _list.AddChild(ThemeRow(item));
        }

        if (Shows(StoreItemKind.Backdrop))
        {
            _list.AddChild(Note(Loc.T("A SCENE IS MOTION, NOT COLOUR — IT BORROWS THE EQUIPPED SKIN'S PALETTE, SO EVERY SCENE LOOKS DIFFERENT UNDER EVERY SKIN.")));
            foreach (bool fresh in NewFirst)
                foreach (var item in StoreCatalog.Items)
                    if (item.Kind == StoreItemKind.Backdrop && item.IsNew == fresh && (paidOk || save.OwnsItem(item.Id)))
                        _list.AddChild(BackdropRow(item));
        }

        if (Shows(StoreItemKind.Artifact))
        {
            // All free ⇒ always shown, even on a mobile build without billing.
            foreach (bool fresh in NewFirst)
                foreach (var item in StoreCatalog.Items)
                    if (item.Kind == StoreItemKind.Artifact && item.IsNew == fresh && (paidOk || save.OwnsItem(item.Id)))
                        _list.AddChild(ArtifactRow(item));
        }

        if (Shows(StoreItemKind.SoundPack))
        {
            // Free by catalog design (empty ProductId ⇒ owned), so there is no price and no buy
            // button on these rows — printing "FREE" on a button that cannot charge is the kind of
            // half-truth that teaches players to distrust the rest of the shelf. The shelf says it
            // once, in words, and the rows only offer HEAR / EQUIP.
            _list.AddChild(Note(Loc.T("EVERY PACK IS FREE AND ALREADY YOURS. TAP A WAVEFORM TO HEAR IT, EQUIP TO KEEP IT — IT CHANGES ONLY HOW A PLACED PIECE SOUNDS.")));
            foreach (bool fresh in NewFirst)
                foreach (var item in StoreCatalog.Items)
                    if (item.Kind == StoreItemKind.SoundPack && item.IsNew == fresh)
                        _list.AddChild(SoundRow(item));
        }

        if (Shows(StoreItemKind.BoosterPack) && paidOk)
        {
            _list.AddChild(Section(Loc.T("BOOSTERS")));
            foreach (var item in StoreCatalog.Items)
                if (item.Kind == StoreItemKind.BoosterPack)
                    _list.AddChild(BoosterRow(item));
        }

        // Remove-ads is only meaningful where ads actually run AND can be paid off.
        if (Shows(StoreItemKind.RemoveAds) && !platform.IsPremium && platform.SupportsAds && paidOk)
        {
            _list.AddChild(Section(Loc.T("PREMIUM")));
            foreach (var item in StoreCatalog.Items)
                if (item.Kind == StoreItemKind.RemoveAds)
                    _list.AddChild(RemoveAdsRow(item));
        }

        if (Shows(StoreItemKind.RemoveAds) && platform.SupportsIap)
        {
            var restore = new Button { Text = Loc.T("RESTORE PURCHASES"), ThemeTypeVariation = "GhostButton", CustomMinimumSize = new Vector2(0, TouchTarget) };
            Motion.BindButtonFeel(restore);
            TouchScroll.Bind(restore, () => platform.RestorePurchases());
            _list.AddChild(restore);
        }

        // An empty shelf must say so. EXTRAS is legitimately empty on a mobile build with no
        // billing plugin (paidOk false hides boosters, and remove-ads needs both ads and payment),
        // and a blank pane reads as a broken screen.
        if (_list.GetChildCount() == 0)
            _list.AddChild(Note(Loc.T("NOTHING ON THIS SHELF RIGHT NOW.")));
    }

    private Control ThemeRow(StoreItem item)
    {
        var save = Bootstrap.Instance.Save;
        bool owned = save.OwnsItem(item.Id);
        bool equipped = save.EquippedThemeId == item.Id;

        var card = Card();
        var row = CardRow(card);

        var info = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        info.AddThemeConstantOverride("separation", 4);
        info.AddChild(NameLine(item, equipped));
        if (item.Theme is { } theme)
            info.AddChild(new ThemePreview(theme) { Selected = equipped });
        info.AddChild(Blurb(item.Blurb));
        // A set skin says what it brings with it, BEFORE the tap — equipping it also swaps the
        // line-clear burst and the scene, and a silent change to something the player picked reads
        // as a bug.
        if (item.Theme?.Signature is { } sig)
        {
            var pack = StoreCatalog.ById(BurstArtifacts.ToId(sig));
            if (pack is not null) info.AddChild(Note(Loc.T("INCLUDES THE {0} BURST", Loc.T(pack.Name))));
        }
        if (item.Theme?.Scene is { } scene)
        {
            var sc = StoreCatalog.ById(Backdrops.ToId(scene));
            if (sc is not null) info.AddChild(Note(Loc.T("INCLUDES THE {0} SCENE", Loc.T(sc.Name))));
        }
        row.AddChild(info);

        row.AddChild(EquipControl(item, owned, equipped, () => Equip(item)));
        return card;
    }

    /// <summary>
    /// A backdrop scene. The preview is the REAL shader at card size (see
    /// <see cref="BackdropPreview"/>), so what the row shows is what the screen becomes — a
    /// hand-drawn approximation of a procedural scene is a promise the renderer never signed.
    /// </summary>
    private Control BackdropRow(StoreItem item)
    {
        var save = Bootstrap.Instance.Save;
        bool owned = save.OwnsItem(item.Id);
        bool equipped = save.EquippedBackdropId == item.Id;

        var card = Card();
        var row = CardRow(card);
        row.AddChild(new BackdropPreview(Backdrops.FromId(item.Id))
        {
            Selected = equipped,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        });

        var info = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        info.AddThemeConstantOverride("separation", 4);
        info.AddChild(NameLine(item, equipped));
        info.AddChild(Blurb(item.Blurb));
        row.AddChild(info);

        row.AddChild(EquipControl(item, owned, equipped, () => EquipBackdrop(item)));
        return card;
    }

    private Control ArtifactRow(StoreItem item)
    {
        var save = Bootstrap.Instance.Save;
        bool owned = save.OwnsItem(item.Id);
        bool equipped = save.EquippedArtifactId == item.Id;

        var card = Card();
        var row = CardRow(card);
        row.AddChild(new ArtifactPreview(BurstArtifacts.FromId(item.Id)) { SizeFlagsVertical = SizeFlags.ShrinkCenter, Selected = equipped });

        var info = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        info.AddThemeConstantOverride("separation", 4);
        info.AddChild(NameLine(item, equipped));
        info.AddChild(Blurb(item.Blurb));
        row.AddChild(info);

        row.AddChild(EquipControl(item, owned, equipped, () => EquipArtifact(item)));
        return card;
    }

    /// <summary>
    /// The equipped-chip / EQUIP / BUY triple that every cosmetic row ends with. Factored out
    /// because it was written three times and a fourth axis would have made it four — and the one
    /// rule it encodes (buying a cosmetic also equips it, because nobody buys a skin to not wear
    /// it) has to be identical on every shelf.
    /// </summary>
    private Control EquipControl(StoreItem item, bool owned, bool equipped, Action equip)
    {
        if (equipped) return StateChip(Loc.T("EQUIPPED"), Palette.Accent);
        if (owned)
        {
            var b = ActionBtn(Loc.T("EQUIP"), "GhostButton");
            TouchScroll.Bind(b, equip);
            return b;
        }
        var buy = ActionBtn(item.PriceLabel, "PrimaryButton");
        TouchScroll.Bind(buy, () => Purchase(item, onGranted: () =>
        {
            Bootstrap.Instance.Save.GrantItem(item.Id);
            equip();
        }));
        return buy;
    }

    /// <summary>
    /// A placement sound pack. Two targets, both at the 84px (44pt) touch floor: the waveform card
    /// AUDITIONS the pack without changing anything, and EQUIP commits it.
    /// <para>Equipping writes <c>GameSettings.SfxPack</c> — the same value the Settings › AUDIO
    /// picker owns. One setting, two front doors; the store deliberately does NOT keep its own
    /// "equipped sound" key, because two keys for one audible thing is how a player ends up
    /// hearing something neither screen claims to have selected.</para>
    /// </summary>
    private Control SoundRow(StoreItem item)
    {
        int current = Mathf.Clamp(Bootstrap.Instance.Save.Settings.SfxPack,
                                  0, Blockfall.Audio.AudioManager.PackNames.Length - 1);
        bool equipped = current == item.SoundPackIndex;

        var card = Card();
        var row = CardRow(card);

        // The waveform IS the audition button: a separate ▶ next to a picture of a sound would
        // spend 120px of the row saying the same thing twice.
        var preview = new SoundPreview(item.SoundPackIndex) { Selected = equipped };
        preview.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var hear = new Button
        {
            ThemeTypeVariation = "CardButton",
            CustomMinimumSize = new Vector2(132, TouchTarget),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            TooltipText = Loc.T("HEAR"),
        };
        hear.AddChild(preview);
        Motion.BindButtonFeel(hear);
        TouchScroll.Bind(hear, () => { PlayPack(item.SoundPackIndex); preview.Ping(); });
        row.AddChild(hear);

        var info = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        info.AddThemeConstantOverride("separation", 4);
        info.AddChild(NameLine(item, equipped));
        info.AddChild(Blurb(item.Blurb));
        row.AddChild(info);

        if (equipped)
        {
            row.AddChild(StateChip(Loc.T("EQUIPPED"), Palette.Accent));
        }
        else
        {
            var equip = ActionBtn(Loc.T("EQUIP"), "GhostButton");
            TouchScroll.Bind(equip, () => EquipSound(item));
            row.AddChild(equip);
        }
        return card;
    }

    private Control BoosterRow(StoreItem item)
    {
        int owned = Bootstrap.Instance.Save.BoosterCount(item.BoosterId);

        var card = Card();
        var row = CardRow(card);

        row.AddChild(new Theme.Icon(IconKind.Refresh, Palette.AccentGreen, 26) { SizeFlagsVertical = SizeFlags.ShrinkCenter });

        var info = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        info.AddThemeConstantOverride("separation", 2);
        var name = new Label { Text = Loc.T("{0}   ·   OWNED: {1}", Loc.T(item.Name), owned) };
        name.AddThemeFontOverride("font", Fonts.UiBold);
        name.AddThemeFontSizeOverride("font_size", 19);
        info.AddChild(name);
        info.AddChild(Blurb(item.Blurb));
        row.AddChild(info);

        var buy = ActionBtn(item.PriceLabel, "PrimaryButton");
        TouchScroll.Bind(buy, () => Purchase(item, onGranted: () =>
            Bootstrap.Instance.Save.AddBoosters(item.BoosterId, item.BoosterCount)));
        row.AddChild(buy);
        return card;
    }

    private Control RemoveAdsRow(StoreItem item)
    {
        var card = Card();
        var row = CardRow(card);

        var info = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        var name = new Label { Text = Loc.T(item.Name) };
        name.AddThemeFontOverride("font", Fonts.UiBold);
        name.AddThemeFontSizeOverride("font_size", 19);
        info.AddChild(name);
        info.AddChild(Blurb(item.Blurb));
        row.AddChild(info);

        var buy = ActionBtn(item.PriceLabel, "PrimaryButton");
        TouchScroll.Bind(buy, () => Bootstrap.Instance.Platform.PurchaseRemoveAds(ok => { if (ok) Rebuild(); }));
        row.AddChild(buy);
        return card;
    }

    // ---- Actions ----------------------------------------------------------------

    private void Purchase(StoreItem item, Action onGranted)
    {
        Bootstrap.Instance.Platform.PurchaseItem(item.ProductId, ok =>
        {
            if (!ok || !IsInstanceValid(this)) return;
            onGranted();
            Rebuild();
        });
    }

    private void Equip(StoreItem item)
    {
        var save = Bootstrap.Instance.Save;
        save.EquipTheme(item.Id);
        // A themed SET lands whole: a skin designed with a celebration and a scene equips both, so
        // the player gets the thing that was on the card instead of last week's burst on a new
        // surface. Not a lock — each is still its own free row they can change back.
        if (item.Theme?.Signature is { } sig) save.EquipArtifact(BurstArtifacts.ToId(sig));
        if (item.Theme?.Scene is { } scene) save.EquipBackdrop(Backdrops.ToId(scene));
        ApplyAndFlourish(() => Palette.Accent);
    }

    private void EquipBackdrop(StoreItem item)
    {
        Bootstrap.Instance.Save.EquipBackdrop(item.Id);
        ApplyAndFlourish(() => Palette.AccentViolet);
    }

    private void EquipArtifact(StoreItem item)
    {
        Bootstrap.Instance.Save.EquipArtifact(item.Id);
        ApplyAndFlourish(() => BurstArtifacts.AccentOf(BurstArtifacts.FromId(item.Id)));
    }

    /// <summary>
    /// Push the new cosmetics through every cached layer, flourish, and rebuild the shelf.
    /// <para>The order matters: <c>ApplySkin</c> re-bakes the shared control theme, so the pulse
    /// colour and the rebuilt rows below it read the NEW accent rather than the one the player just
    /// replaced. And the rebuild is what makes this screen retint itself — per-instance theme
    /// overrides (every <c>AddThemeColorOverride(Palette.Accent)</c> in the rows) are captured at
    /// build time and do not follow a theme rebuild.</para>
    /// </summary>
    private void ApplyAndFlourish(Func<Color> pulse)
    {
        Bootstrap.Instance.ApplySkin();
        // A FACTORY, not a Color: C# evaluates arguments before the call, so passing
        // Palette.Accent directly captured the accent of the skin the player had just REPLACED and
        // washed the screen in it — the precise opposite of what this method promises, and a
        // silent disagreement with SettingsScreen, which pulses after applying.
        Bootstrap.Instance.Bg.Pulse(pulse(), 0.32f);   // Motion.Reduced-gated inside Pulse
        Rebuild();
    }

    /// <summary>
    /// Audition a pack WITHOUT committing it. <c>AudioManager.PlayPlace</c> reads <c>SfxPack</c>,
    /// picks the cue and starts a pooled player, all inside the call — nothing is deferred — so
    /// borrowing the field across that one line and handing it straight back is airtight.
    /// <c>SetSettings</c> is never called, so the save is not marked dirty and nothing reaches
    /// disk; the player hears the pack and the setting is untouched.
    /// </summary>
    private static void PlayPack(int index)
    {
        var s = Bootstrap.Instance.Save.Settings;
        int keep = s.SfxPack;
        s.SfxPack = index;
        Bootstrap.Instance.Audio.PlayPlace();
        s.SfxPack = keep;
    }

    private void EquipSound(StoreItem item)
    {
        var save = Bootstrap.Instance.Save;
        var s = save.Settings;
        s.SfxPack = item.SoundPackIndex;
        save.SetSettings(s);                    // persists, exactly as the Settings picker does
        Bootstrap.Instance.Audio.PlayPlace();   // confirm in the pack that was just chosen
        Rebuild();
    }

    // ---- Small builders ------------------------------------------------------------

    private static PanelContainer Card() => new() { ThemeTypeVariation = "Card" };

    private static HBoxContainer CardRow(PanelContainer card)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        card.AddChild(row);
        return row;
    }

    private static Label Blurb(string text)
    {
        var l = new Label
        {
            Text = Loc.T(text),
            ThemeTypeVariation = "DimLabel",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        l.AddThemeFontSizeOverride("font_size", 13);
        return l;
    }

    /// <summary>
    /// Name + the NEW tag on one line, shared by every equippable row (theme / scene / burst /
    /// sound). The badge is what makes a floated-up row legible as a fresh drop rather than an
    /// arbitrary reordering of a list the player knows — so it belongs to the SORT, and any shelf
    /// that sorts NEW-first has to draw it. BURST FX sorted without one for a release; three new
    /// bursts sat at the top of the list with nothing saying why.
    /// </summary>
    private static Control NameLine(StoreItem item, bool equipped)
    {
        var name = new Label { Text = Loc.T(item.Name), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        name.AddThemeFontOverride("font", Fonts.UiBold);
        name.AddThemeFontSizeOverride("font_size", 20);
        if (equipped) name.AddThemeColorOverride("font_color", Palette.Accent);
        if (!item.IsNew) return name;

        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 8);
        line.AddChild(name);
        line.AddChild(NewBadge());
        return line;
    }

    private static Button ActionBtn(string text, string variation)
    {
        var b = new Button
        {
            Text = text,
            ThemeTypeVariation = variation,
            // Was 46 (24pt) — every buy/equip control in the shop sat at just over half the touch
            // floor, on the one screen where a mis-tap can spend money.
            CustomMinimumSize = new Vector2(120, TouchTarget),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        b.AddThemeFontSizeOverride("font_size", 16);
        Motion.BindButtonFeel(b);
        return b;
    }

    private static Control StateChip(string text, Color color)
    {
        var l = new Label { Text = text, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        l.AddThemeFontOverride("font", Fonts.UiBold);
        l.AddThemeFontSizeOverride("font_size", 15);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }

    private static Control Section(string text)
        => new Label { Text = text, ThemeTypeVariation = "SectionLabel" };

    /// <summary>A standing, shelf-level caption (wraps). Used for policy the player must know
    /// BEFORE buying, not for flavour text.</summary>
    private static Control Note(string text)
    {
        var l = new Label
        {
            Text = text,
            ThemeTypeVariation = "DimLabel",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        l.AddThemeFontSizeOverride("font_size", 13);
        return l;
    }

    /// <summary>The "NEW" tag. Gold is otherwise reserved (daily / new-best / perfect clear); a
    /// fresh drop is the store's equivalent of NEW BEST, and gold is already this screen's title
    /// accent, so the reservation holds. Text, not colour alone — never a hue-only signal.</summary>
    private static Control NewBadge()
    {
        var l = new Label { Text = Loc.T("NEW"), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        l.AddThemeFontOverride("font", Fonts.UiBold);
        l.AddThemeFontSizeOverride("font_size", 13);
        l.AddThemeColorOverride("font_color", Palette.AccentGold);
        return l;
    }
}
