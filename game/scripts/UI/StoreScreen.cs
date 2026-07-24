using Godot;
using System;
using Blockfall.Platform;
using Blockfall.Theme;
using Blockfall.Core.Localization;

namespace Blockfall.UI;

/// <summary>
/// The store: cosmetic themes (buy → equip, with live piece-color swatches),
/// the Second Chance booster pack, and remove-ads. Payment runs through
/// <see cref="IPlatformServices.PurchaseItem"/>; on success THIS screen grants
/// the item via SaveManager (platforms only handle money). Equipping applies
/// instantly — palette + backdrop retint live behind the store.
/// </summary>
public partial class StoreScreen : Control
{
    public event Action? BackRequested;

    private VBoxContainer _list = null!;

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

        var back = new Button { Text = Loc.T("BACK"), ThemeTypeVariation = "PrimaryButton", CustomMinimumSize = new Vector2(0, 54) };
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
        foreach (var item in StoreCatalog.Items)
            if (item.Kind == StoreItemKind.Theme && (paidOk || save.OwnsItem(item.Id)))
                _list.AddChild(ThemeRow(item));

        // Burst-FX artifacts (all free ⇒ always shown, even on a mobile build without billing).
        _list.AddChild(Section(Loc.T("BURST FX")));
        foreach (var item in StoreCatalog.Items)
            if (item.Kind == StoreItemKind.Artifact && (paidOk || save.OwnsItem(item.Id)))
                _list.AddChild(ArtifactRow(item));

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
            var restore = new Button { Text = Loc.T("RESTORE PURCHASES"), ThemeTypeVariation = "GhostButton", CustomMinimumSize = new Vector2(0, 44) };
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
        var name = new Label { Text = item.Name };
        name.AddThemeFontOverride("font", Fonts.UiBold);
        name.AddThemeFontSizeOverride("font_size", 20);
        if (equipped) name.AddThemeColorOverride("font_color", Palette.Accent);
        info.AddChild(name);
        if (item.Theme is { } theme)
        {
            if (theme.Glyph != SkinGlyph.None)
            {
                var previewRow = new HBoxContainer();
                previewRow.AddThemeConstantOverride("separation", 8);
                previewRow.AddChild(SwatchStrip(theme));
                previewRow.AddChild(new GlyphIcon(theme.Glyph, new Color(0.95f, 0.95f, 1f), 20) { SizeFlagsVertical = SizeFlags.ShrinkCenter });
                info.AddChild(previewRow);
            }
            else info.AddChild(SwatchStrip(theme));
        }
        var blurb = new Label { Text = item.Blurb, ThemeTypeVariation = "DimLabel" };
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
        row.AddChild(new Theme.Icon(IconKind.Diamond, ArtifactColor(item.Id), 26) { SizeFlagsVertical = SizeFlags.ShrinkCenter });

        var info = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        info.AddThemeConstantOverride("separation", 4);
        var name = new Label { Text = item.Name };
        name.AddThemeFontOverride("font", Fonts.UiBold);
        name.AddThemeFontSizeOverride("font_size", 20);
        if (equipped) name.AddThemeColorOverride("font_color", Palette.Accent);
        info.AddChild(name);
        var blurb = new Label { Text = item.Blurb, ThemeTypeVariation = "DimLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart };
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

    private static Color ArtifactColor(string id) => id switch
    {
        "artifact_fireworks" => Palette.AccentGold,
        "artifact_confetti" => Palette.AccentGreen,
        "artifact_supernova" => new Color(1f, 0.98f, 0.9f),
        "artifact_shards" => Palette.Accent,
        "artifact_rainbow" => Palette.AccentViolet,
        _ => Palette.AccentGold,
    };

    private Control BoosterRow(StoreItem item)
    {
        int owned = Bootstrap.Instance.Save.BoosterCount(item.BoosterId);

        var card = Card();
        var row = CardRow(card);

        row.AddChild(new Theme.Icon(IconKind.Refresh, Palette.AccentGreen, 26) { SizeFlagsVertical = SizeFlags.ShrinkCenter });

        var info = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        info.AddThemeConstantOverride("separation", 2);
        var name = new Label { Text = Loc.T("{0}   ·   OWNED: {1}", item.Name, owned) };
        name.AddThemeFontOverride("font", Fonts.UiBold);
        name.AddThemeFontSizeOverride("font_size", 19);
        info.AddChild(name);
        var blurb = new Label
        {
            Text = item.Blurb,
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
        var name = new Label { Text = item.Name };
        name.AddThemeFontOverride("font", Fonts.UiBold);
        name.AddThemeFontSizeOverride("font_size", 19);
        info.AddChild(name);
        var blurb = new Label { Text = item.Blurb, ThemeTypeVariation = "DimLabel" };
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
        Palette.ApplyTheme(item.Theme);
        Bootstrap.Instance.Bg.ApplyThemeColors(); // backdrop retints live behind the store
        Rebuild();
    }

    private void EquipArtifact(StoreItem item)
    {
        Bootstrap.Instance.Save.EquipArtifact(item.Id);
        Rebuild();
    }

    // ---- Small builders ------------------------------------------------------------

    /// <summary>Seven mini piece-color swatches — the theme preview.</summary>
    private static Control SwatchStrip(BlockTheme t)
    {
        var strip = new HBoxContainer();
        strip.AddThemeConstantOverride("separation", 4);
        foreach (var c in new[] { t.I, t.O, t.T, t.S, t.Z, t.J, t.L })
        {
            strip.AddChild(new ColorRect
            {
                Color = c,
                CustomMinimumSize = new Vector2(22, 12),
                MouseFilter = MouseFilterEnum.Ignore,
            });
        }
        return strip;
    }

    private static PanelContainer Card() => new() { ThemeTypeVariation = "Card" };

    private static HBoxContainer CardRow(PanelContainer card)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        card.AddChild(row);
        return row;
    }

    private static Button ActionBtn(string text, string variation)
    {
        var b = new Button
        {
            Text = text,
            ThemeTypeVariation = variation,
            CustomMinimumSize = new Vector2(120, 46),
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
}
