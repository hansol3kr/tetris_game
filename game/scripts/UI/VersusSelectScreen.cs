using Godot;
using System;
using Blockfall.Core;
using Blockfall.Core.Localization;
using Blockfall.Theme;

namespace Blockfall.UI;

/// <summary>
/// Difficulty select for the CPU battle, in the versus identity color (violet).
/// One glass card per difficulty; emits <see cref="DifficultyChosen"/> with the
/// picked preset.
/// </summary>
public partial class VersusSelectScreen : Control
{
    public event Action<BotDifficulty>? DifficultyChosen;
    public event Action? OnlineChosen;
    public event Action? BackRequested;

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
            CustomMinimumSize = new Vector2(420, 0),
        };
        col.AddThemeConstantOverride("separation", 12);
        outer.AddChild(col);
        outer.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });

        var titleRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        titleRow.AddThemeConstantOverride("separation", 12);
        titleRow.AddChild(new Theme.Icon(IconKind.Swords, Palette.AccentViolet, 32) { SizeFlagsVertical = SizeFlags.ShrinkCenter });
        var title = new Label { Text = Loc.T("VERSUS CPU"), ThemeTypeVariation = "TitleLabel" };
        title.AddThemeFontSizeOverride("font_size", 38);
        title.AddThemeColorOverride("font_color", Palette.AccentViolet);
        titleRow.AddChild(title);
        col.AddChild(titleRow);

        // Online duel first — playing a human is the headline act.
        var online = new Button { CustomMinimumSize = new Vector2(0, 72) };
        StyleVersusCard(online);
        Motion.BindButtonFeel(online);
        var onlineContent = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        onlineContent.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        onlineContent.OffsetLeft = 18; onlineContent.OffsetRight = -18;
        onlineContent.AddThemeConstantOverride("separation", 14);
        onlineContent.AddChild(new Theme.Icon(IconKind.Bolt, Palette.AccentViolet, 24) { SizeFlagsVertical = SizeFlags.ShrinkCenter });
        var onlineText = new VBoxContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter };
        onlineText.AddThemeConstantOverride("separation", 0);
        var onlineName = new Label { Text = Loc.T("ONLINE MATCH") };
        onlineName.AddThemeFontOverride("font", Fonts.UiBold);
        onlineName.AddThemeFontSizeOverride("font_size", 21);
        onlineName.AddThemeColorOverride("font_color", Palette.AccentViolet);
        onlineText.AddChild(onlineName);
        var onlineSub = new Label { Text = Loc.T("DUEL A FRIEND — HOST OR JOIN BY IP"), ThemeTypeVariation = "DimLabel" };
        onlineSub.AddThemeFontSizeOverride("font_size", 13);
        onlineText.AddChild(onlineSub);
        onlineContent.AddChild(onlineText);
        online.AddChild(onlineContent);
        online.Pressed += () => OnlineChosen?.Invoke();
        col.AddChild(online);

        var hint = new Label
        {
            Text = Loc.T("OR CHOOSE A CPU OPPONENT"),
            ThemeTypeVariation = "SectionLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        col.AddChild(hint);
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

        var items = new System.Collections.Generic.List<Control>();
        foreach (var diff in BotDifficulty.All)
        {
            var d = diff;
            var b = new Button { CustomMinimumSize = new Vector2(0, 62) };
            StyleVersusCard(b);
            Motion.BindButtonFeel(b);

            var content = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            content.OffsetLeft = 18; content.OffsetRight = -18;
            content.AddThemeConstantOverride("separation", 14);
            var name = new Label { Text = Loc.T(diff.Name.ToUpperInvariant()) };
            name.AddThemeFontOverride("font", Fonts.UiBold);
            name.AddThemeFontSizeOverride("font_size", 21);
            name.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            content.AddChild(name);
            b.AddChild(content);

            b.Pressed += () => DifficultyChosen?.Invoke(d);
            col.AddChild(b);
            items.Add(b);
        }

        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        var back = new Button { Text = Loc.T("BACK"), ThemeTypeVariation = "GhostButton", CustomMinimumSize = new Vector2(0, 48) };
        Motion.BindButtonFeel(back);
        back.Pressed += () => BackRequested?.Invoke();
        col.AddChild(back);

        Motion.EnterStagger(items.ToArray(), initialDelay: 0.05f);
    }

    private static void StyleVersusCard(Button b)
    {
        var v = Palette.AccentViolet;
        b.AddThemeStyleboxOverride("normal",
            TextureFactory.GlassStyle(Palette.RadiusM, Palette.GlassTop, Palette.GlassBottom, Palette.GlassBorder, 1f, 0.16f, 18, 12));
        b.AddThemeStyleboxOverride("hover",
            TextureFactory.GlassStyle(Palette.RadiusM,
                new Color(v.R, v.G, v.B, 0.10f), new Color(v.R, v.G, v.B, 0.04f),
                new Color(v.R, v.G, v.B, 0.6f), 1.3f, 0.18f, 18, 12));
        b.AddThemeStyleboxOverride("pressed",
            TextureFactory.GlassStyle(Palette.RadiusM,
                new Color(v.R, v.G, v.B, 0.20f), new Color(v.R, v.G, v.B, 0.09f),
                new Color(v.R, v.G, v.B, 0.9f), 1.4f, 0.12f, 18, 12));
    }
}
