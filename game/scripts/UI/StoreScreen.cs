using Godot;
using System;
using Blockfall.Platform;
using Blockfall.Theme;
using Blockfall.Core.Localization;

namespace Blockfall.UI;

/// <summary>
/// The store: cosmetic themes (buy → equip, with live piece-color swatches), burst-FX
/// artifacts, placement sound packs, the Second Chance booster pack, and remove-ads.
/// Payment runs through <see cref="IPlatformServices.PurchaseItem"/>; on success THIS screen
/// grants the item via SaveManager (platforms only handle money). Equipping applies
/// instantly — only the piece colors retint; the game-screen backdrop stays fixed
/// (see Palette.ApplyTheme).
/// <para>Section contract: EVERY cosmetic kind in <see cref="StoreCatalog"/> must have a
/// section here. A kind with rows in the catalog and no loop on this screen is invisible
/// merchandise — which is exactly what happened to <see cref="StoreItemKind.SoundPack"/>
/// between its catalog landing and this one. Adding a kind is two edits, not one.</para>
/// </summary>
public partial class StoreScreen : Control
{
    public event Action? BackRequested;

    private VBoxContainer _list = null!;

    /// <summary>Two stable passes over the catalog: flagged rows, then the rest. Keeps the
    /// declaration order inside each group (no comparer, no shuffling of the familiar list).</summary>
    private static readonly bool[] NewFirst = { true, false };

    public override void _Ready()
    {
        UiTheme.ApplyTo(this);
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

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
            CustomMinimumSize = new Vector2(520, 0),
        };
        col.AddThemeConstantOverride("separation", 12);
        outer.AddChild(col);
        outer.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });

        var titleRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        titleRow.AddThemeConstantOverride("separation", 12);
        titleRow.AddChild(new Theme.Icon(IconKind.Diamond, Palette.AccentGold, 30) { SizeFlagsVertical = SizeFlags.ShrinkCenter });
        var title = new Label { Text = Loc.T("STORE"), ThemeTypeVariation = "TitleLabel" };
        title.AddThemeFontSizeOverride("font_size", 36);
        titleRow.AddChild(title);
        col.AddChild(titleRow);

        _list = new VBoxContainer();
        _list.AddThemeConstantOverride("separation", 12);
        col.AddChild(_list);

        var back = new Button { Text = Loc.T("BACK"), ThemeTypeVariation = "PrimaryButton", CustomMinimumSize = new Vector2(0, TouchTarget) };
        Motion.BindButtonFeel(back);
        back.Pressed += () => BackRequested?.Invoke();
        col.AddChild(back);

        Rebuild();
    }

    /// <summary>Rebuild all rows (called after any purchase/equip so states refresh).</summary>
    private void Rebuild()
    {
        foreach (var child in _list.GetChildren()) ((Node)child).QueueFree();

        var platform = Bootstrap.Instance.Platform;
        var save = Bootstrap.Instance.Save;
        // On a mobile store build WITHOUT a real billing plugin, hide everything
        // that carries a price tag: buy buttons that don't charge (or do nothing)
        // are an App Store review rejection (guideline 3.1.1). Desktop dev/Steam
        // keep the full store — their "purchases" are grant-by-design.
        bool paidOk = !OS.HasFeature("mobile") || platform.SupportsIap;

        _list.AddChild(Section(Loc.T("THEMES")));
        // State the accessibility split instead of letting the player find it after paying:
        // previews below render each skin's OWN colours (otherwise every card looks identical),
        // while the play field keeps the colour-safe palette. See Palette.PlanFill.
        if (Palette.ColorblindMode)
            _list.AddChild(Note(Loc.T("COLOR-SAFE PALETTE IS ON: THE BOARD KEEPS IT. PREVIEWS BELOW SHOW EACH SKIN'S OWN COLORS.")));
        // New arrivals first. The list is 31 rows of 122px-tall cards; appended, the newest
        // drop lands at rows 28-31 and the only skins that read differently at arm's length
        // sit behind a scroll of look-alikes. Two stable passes — no sort, no comparer.
        foreach (bool fresh in NewFirst)
            foreach (var item in StoreCatalog.Items)
                if (item.Kind == StoreItemKind.Theme && item.IsNew == fresh && (paidOk || save.OwnsItem(item.Id)))
                    _list.AddChild(ThemeRow(item));

        // Burst-FX artifacts (all free ⇒ always shown, even on a mobile build without billing).
        // Same two-pass NEW-first order as THEMES: this list is 14 rows deep, so appended the
        // three signature bursts of the newest wave landed at 12/13/14 — behind a scroll of
        // effects the player already owns and has already seen fire.
        _list.AddChild(Section(Loc.T("BURST FX")));
        foreach (bool fresh in NewFirst)
            foreach (var item in StoreCatalog.Items)
                if (item.Kind == StoreItemKind.Artifact && item.IsNew == fresh && (paidOk || save.OwnsItem(item.Id)))
                    _list.AddChild(ArtifactRow(item));

        // Placement sound packs. Free by catalog design (empty ProductId ⇒ owned), so there is
        // no price and no buy button on these rows — printing "FREE" on a button that cannot
        // charge is the kind of half-truth that teaches players to distrust the rest of the
        // shelf. The section says it once, in words, and the rows only offer HEAR / EQUIP.
        _list.AddChild(Section(Loc.T("SOUND")));
        _list.AddChild(Note(Loc.T("EVERY PACK IS FREE AND ALREADY YOURS. TAP A WAVEFORM TO HEAR IT, EQUIP TO KEEP IT — IT CHANGES ONLY HOW A PLACED PIECE SOUNDS.")));
        foreach (bool fresh in NewFirst)
            foreach (var item in StoreCatalog.Items)
                if (item.Kind == StoreItemKind.SoundPack && item.IsNew == fresh)
                    _list.AddChild(SoundRow(item));

        if (paidOk)
        {
            _list.AddChild(Section(Loc.T("BOOSTERS")));
            foreach (var item in StoreCatalog.Items)
                if (item.Kind == StoreItemKind.BoosterPack)
                    _list.AddChild(BoosterRow(item));
        }

        // Remove-ads is only meaningful where ads actually run AND can be paid off.
        if (!platform.IsPremium && platform.SupportsAds && paidOk)
        {
            _list.AddChild(Section(Loc.T("PREMIUM")));
            foreach (var item in StoreCatalog.Items)
                if (item.Kind == StoreItemKind.RemoveAds)
                    _list.AddChild(RemoveAdsRow(item));
        }

        if (platform.SupportsIap)
        {
            var restore = new Button { Text = Loc.T("RESTORE PURCHASES"), ThemeTypeVariation = "GhostButton", CustomMinimumSize = new Vector2(0, TouchTarget) };
            Motion.BindButtonFeel(restore);
            restore.Pressed += () => platform.RestorePurchases();
            _list.AddChild(restore);
        }
    }

    // ---- Rows -----------------------------------------------------------------

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
        var blurb = new Label
        {
            Text = Loc.T(item.Blurb),
            ThemeTypeVariation = "DimLabel",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        blurb.AddThemeFontSizeOverride("font_size", 13);
        info.AddChild(blurb);
        // A set skin says what it brings with it, BEFORE the tap — equipping it also swaps the
        // line-clear burst, and a silent change to something the player picked reads as a bug.
        if (item.Theme?.Signature is { } sig)
        {
            var pack = StoreCatalog.ById(BurstArtifacts.ToId(sig));
            if (pack is not null) info.AddChild(Note(Loc.T("INCLUDES THE {0} BURST", Loc.T(pack.Name))));
        }
        row.AddChild(info);

        if (equipped)
        {
            row.AddChild(StateChip(Loc.T("EQUIPPED"), Palette.Accent));
        }
        else if (owned)
        {
            var equip = ActionBtn(Loc.T("EQUIP"), "GhostButton");
            equip.Pressed += () => Equip(item);
            row.AddChild(equip);
        }
        else
        {
            var buy = ActionBtn(item.PriceLabel, "PrimaryButton");
            buy.Pressed += () => Purchase(item, onGranted: () =>
            {
                Bootstrap.Instance.Save.GrantItem(item.Id);
                Equip(item); // buying a skin means you want to wear it
            });
            row.AddChild(buy);
        }
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
        var blurb = new Label { Text = Loc.T(item.Blurb), ThemeTypeVariation = "DimLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        blurb.AddThemeFontSizeOverride("font_size", 13);
        info.AddChild(blurb);
        row.AddChild(info);

        if (equipped)
        {
            row.AddChild(StateChip(Loc.T("EQUIPPED"), Palette.Accent));
        }
        else if (owned)
        {
            var equip = ActionBtn(Loc.T("EQUIP"), "GhostButton");
            equip.Pressed += () => EquipArtifact(item);
            row.AddChild(equip);
        }
        else
        {
            var buy = ActionBtn(item.PriceLabel, "PrimaryButton");
            buy.Pressed += () => Purchase(item, onGranted: () =>
            {
                Bootstrap.Instance.Save.GrantItem(item.Id);
                EquipArtifact(item);
            });
            row.AddChild(buy);
        }
        return card;
    }

    /// <summary>
    /// A placement sound pack. Two targets, both at the 84px (44pt) touch floor: the waveform
    /// card AUDITIONS the pack without changing anything, and EQUIP commits it.
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
        // spend 120px of a 520px row saying the same thing twice.
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
        hear.Pressed += () => { PlayPack(item.SoundPackIndex); preview.Ping(); };
        row.AddChild(hear);

        var info = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        info.AddThemeConstantOverride("separation", 4);
        info.AddChild(NameLine(item, equipped));
        var blurb = new Label { Text = Loc.T(item.Blurb), ThemeTypeVariation = "DimLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        blurb.AddThemeFontSizeOverride("font_size", 13);
        info.AddChild(blurb);
        row.AddChild(info);

        if (equipped)
        {
            row.AddChild(StateChip(Loc.T("EQUIPPED"), Palette.Accent));
        }
        else
        {
            var equip = ActionBtn(Loc.T("EQUIP"), "GhostButton");
            equip.Pressed += () => EquipSound(item);
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
        var blurb = new Label
        {
            Text = Loc.T(item.Blurb),
            ThemeTypeVariation = "DimLabel",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        blurb.AddThemeFontSizeOverride("font_size", 13);
        info.AddChild(blurb);
        row.AddChild(info);

        var buy = ActionBtn(item.PriceLabel, "PrimaryButton");
        buy.Pressed += () => Purchase(item, onGranted: () =>
            Bootstrap.Instance.Save.AddBoosters(item.BoosterId, item.BoosterCount));
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
        var blurb = new Label { Text = Loc.T(item.Blurb), ThemeTypeVariation = "DimLabel" };
        blurb.AddThemeFontSizeOverride("font_size", 13);
        info.AddChild(blurb);
        row.AddChild(info);

        var buy = ActionBtn(item.PriceLabel, "PrimaryButton");
        buy.Pressed += () => Bootstrap.Instance.Platform.PurchaseRemoveAds(ok => { if (ok) Rebuild(); });
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
        // A themed SET lands whole: a skin that was designed with a celebration equips it too,
        // so the player gets the thing that was on the card instead of last week's burst on a
        // new surface. Not a lock — the burst is still its own free row they can change back.
        if (item.Theme?.Signature is { } sig) save.EquipArtifact(BurstArtifacts.ToId(sig));
        Palette.ApplyTheme(item.Theme); // retints blocks only — the backdrop stays fixed (see Palette.ApplyTheme)
        Bootstrap.Instance.Bg.Pulse(Palette.Accent, 0.30f); // equip flourish (Motion.Reduced-gated)
        Rebuild();
    }

    /// <summary>
    /// Audition a pack WITHOUT committing it. <c>AudioManager.PlayPlace</c> reads
    /// <c>SfxPack</c>, picks the cue and starts a pooled player, all inside the call — nothing
    /// is deferred — so borrowing the field across that one line and handing it straight back
    /// is airtight. <c>SetSettings</c> is never called, so the save is not marked dirty and
    /// nothing reaches disk; the player hears the pack and the setting is untouched.
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

    private void EquipArtifact(StoreItem item)
    {
        Bootstrap.Instance.Save.EquipArtifact(item.Id);
        Bootstrap.Instance.Bg.Pulse(ArtifactPulseColor(item.Id), 0.34f); // equip flourish in the burst's colour
        Rebuild();
    }

    private static Color ArtifactPulseColor(string id) => id switch
    {
        "artifact_supernova" => new Color(1f, 0.98f, 0.9f),
        "artifact_rainbow" or "artifact_prismbloom" => Palette.AccentViolet,
        "artifact_confetti" => Palette.AccentGreen,
        "artifact_shards" or "artifact_lightning" or "artifact_bubblepop" => Palette.Accent,
        "artifact_aurora" => new Color(0.3f, 0.9f, 0.8f),
        "artifact_starfall" => new Color(0.9f, 0.85f, 1f),
        "artifact_fluff" => new Color(1f, 0.86f, 0.70f),
        "artifact_splash" => new Color(0.55f, 0.95f, 1f),
        "artifact_swarm" => new Color(0.75f, 1f, 0.90f),
        _ => Palette.AccentGold,
    };

    // ---- Small builders ------------------------------------------------------------

    private static PanelContainer Card() => new() { ThemeTypeVariation = "Card" };

    private static HBoxContainer CardRow(PanelContainer card)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        card.AddChild(row);
        return row;
    }

    /// <summary>
    /// Name + the NEW tag on one line, shared by every equippable row (theme / burst / sound).
    /// The badge is what makes a floated-up row legible as a fresh drop rather than an arbitrary
    /// reordering of a list the player knows — so it belongs to the SORT, and any section that
    /// sorts NEW-first has to draw it. BURST FX sorted without one for a release; three new
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

    /// <summary>44pt at this project's 0.521pt-per-design-px scale — the repo-wide minimum
    /// touch target (<c>SettingsScreen.TouchTarget</c> holds the same number).</summary>
    private const int TouchTarget = 84;

    private static Button ActionBtn(string text, string variation)
    {
        var b = new Button
        {
            Text = text,
            ThemeTypeVariation = variation,
            // Was 46 (24pt) — every buy/equip control in the shop sat at just over half the
            // touch floor, on the one screen where a mis-tap can spend money.
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
    {
        var l = new Label { Text = text, ThemeTypeVariation = "SectionLabel" };
        return l;
    }

    /// <summary>A standing, section-level caption (wraps). Used for policy the player must know
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
