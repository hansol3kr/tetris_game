using Godot;
using System;
using System.Collections.Generic;
using Blockfall.Core;
using Blockfall.Core.Localization;
using Blockfall.Gameplay;
using Blockfall.Theme;

namespace Blockfall.UI;

/// <summary>
/// End-of-run summary for a Descent run: the depth reached is the headline, the
/// banked score counts up beneath it, every stratum gets a row (cleared or the
/// one that ended the run), and the drafted charms are listed as the run's
/// "build". ResultsScreen is single-Game shaped, so this is its run-shaped
/// sibling built on the same visual patterns. The interstitial slot lives here
/// (end of RUN — never between strata) under the usual 1-in-3 cap.
/// </summary>
public partial class DescentResultsScreen : Control
{
    public event Action? PlayAgain;
    public event Action? BackToMenu;

    private readonly DescentRunState _state;
    private readonly bool _isBest;
    private readonly IReadOnlyList<string> _unlocked;

    public DescentResultsScreen(DescentRunState state, bool isBest, IReadOnlyList<string> unlocked)
    {
        _state = state;
        _isBest = isBest;
        _unlocked = unlocked;
    }

    public override void _Ready()
    {
        UiTheme.ApplyTo(this);
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // The run's ad slot (mobile, non-paying): end of run only, 1-in-3 capped.
        Bootstrap.Instance.Platform.MaybeShowInterstitial();

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
            CustomMinimumSize = new Vector2(440, 0),
        };
        col.AddThemeConstantOverride("separation", 12);
        outer.AddChild(col);
        outer.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });

        var run = _state.Run;

        // ---- Result card -------------------------------------------------------
        var card = new PanelContainer { ThemeTypeVariation = "Card" };
        var cardBox = new VBoxContainer();
        cardBox.AddThemeConstantOverride("separation", 8);
        card.AddChild(cardBox);
        col.AddChild(card);

        var heading = new Label
        {
            Text = run.Victory ? Loc.T("BOTTOM REACHED!") : Loc.T("RUN OVER"),
            ThemeTypeVariation = "TitleLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        heading.AddThemeFontSizeOverride("font_size", 40);
        heading.AddThemeColorOverride("font_color", run.Victory ? Palette.AccentGold : Palette.TextPrimary);
        cardBox.AddChild(heading);

        if (_isBest)
        {
            var badge = new Label
            {
                Text = "★  " + Loc.T("NEW BEST") + "  ★",
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            badge.AddThemeFontOverride("font", Fonts.UiBold);
            badge.AddThemeFontSizeOverride("font_size", 20);
            badge.AddThemeColorOverride("font_color", Palette.AccentGold);
            cardBox.AddChild(badge);
            Motion.Punch(badge, peak: 1.18f, duration: 0.5f);
            Motion.PulseLoop(badge, lowAlpha: 0.7f, period: 1.4f);
        }

        foreach (var id in _unlocked)
        {
            var def = AchievementCatalog.ById(id);
            if (def is null) continue;
            var row = new Label
            {
                Text = "🏆  " + Loc.T("ACHIEVEMENT: {0}", def.Name),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            row.AddThemeFontSizeOverride("font_size", 16);
            row.AddThemeColorOverride("font_color", Palette.AccentGold);
            cardBox.AddChild(row);
            Motion.Punch(row, peak: 1.12f, duration: 0.45f);
        }

        // Headline: how deep you got — depth IS the trophy; the bank counts beneath.
        var depthCaption = new Label
        {
            Text = Loc.T("DEPTH"),
            ThemeTypeVariation = "SectionLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        cardBox.AddChild(depthCaption);

        var depth = new Label
        {
            Text = Loc.T("{0}/{1}", run.StrataCleared, RunDirector.StageCount),
            ThemeTypeVariation = "HeadlineLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(320, 0),
        };
        depth.AddThemeFontSizeOverride("font_size", 52);
        depth.AddThemeColorOverride("font_color", run.Victory ? Palette.AccentGold : Palette.AccentRed);
        cardBox.AddChild(depth);

        var bankCaption = new Label
        {
            Text = Loc.T("BANKED SCORE"),
            ThemeTypeVariation = "SectionLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        cardBox.AddChild(bankCaption);

        var bank = new Label
        {
            ThemeTypeVariation = "HeadlineLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(320, 0),
        };
        bank.AddThemeFontSizeOverride("font_size", 34);
        Motion.CountUp(bank, run.Bank, v => ((long)v).ToString("N0"));
        cardBox.AddChild(bank);

        cardBox.AddChild(Divider());

        // ---- The descent, stratum by stratum ------------------------------------
        var rows = new List<Control>();
        foreach (var o in _state.Outcomes)
        {
            string status = o.Completed ? "✓" : "✗";
            rows.Add(Stat(
                Loc.T("STRATUM {0}", o.Stratum) + " · " + KindLabel(o.Kind),
                $"{status}  {o.Score:N0}"));
        }
        foreach (var row in rows) cardBox.AddChild(row);
        if (rows.Count > 0) Motion.EnterStagger(rows.ToArray(), initialDelay: 0.15f);

        // The build: which charms shaped this run.
        if (run.Owned.Count > 0)
        {
            cardBox.AddChild(Divider());
            cardBox.AddChild(Stat(Loc.T("CHARMS"), string.Join("  ",
                System.Linq.Enumerable.Select(run.Owned, o => Loc.T(CharmSet.Label(o)))), dim: true));
        }

        cardBox.AddChild(Stat(Loc.T("SEED"), SeedCode.Encode(run.RunSeed), dim: true));

        // ---- Actions -------------------------------------------------------------
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        col.AddChild(ActionButton(Loc.T("DESCEND AGAIN"), "PrimaryButton", () => PlayAgain?.Invoke()));
        col.AddChild(ActionButton(Loc.T("MENU"), "GhostButton", () => BackToMenu?.Invoke()));
    }

    private static string KindLabel(StageKind k) => k switch
    {
        StageKind.Dig => Loc.T("DIG"),
        StageKind.Burst => Loc.T("BURST"),
        StageKind.Siege => Loc.T("SIEGE"),
        _ => k.ToString().ToUpperInvariant(),
    };

    private static Button ActionButton(string text, string variation, Action onPressed)
    {
        var b = new Button
        {
            Text = text,
            ThemeTypeVariation = variation,
            CustomMinimumSize = new Vector2(0, 56),
        };
        Motion.BindButtonFeel(b);
        b.Pressed += () => onPressed();
        return b;
    }

    private static Control Divider() => new ColorRect
    {
        Color = new Color(1, 1, 1, 0.08f),
        CustomMinimumSize = new Vector2(0, 1),
        MouseFilter = MouseFilterEnum.Ignore,
    };

    private static Control Stat(string cap, string value, bool dim = false)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        var capLabel = new Label
        {
            Text = cap,
            ThemeTypeVariation = "SectionLabel",
            CustomMinimumSize = new Vector2(170, 0),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        var val = new Label
        {
            Text = value,
            HorizontalAlignment = HorizontalAlignment.Right,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        val.AddThemeFontSizeOverride("font_size", dim ? 15 : 19);
        val.AddThemeColorOverride("font_color", dim ? Palette.TextSecondary : Palette.TextPrimary);
        row.AddChild(capLabel);
        row.AddChild(val);
        return row;
    }
}
