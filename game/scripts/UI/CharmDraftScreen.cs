using Godot;
using System;
using Blockfall.Core;
using Blockfall.Core.Localization;
using Blockfall.Gameplay;
using Blockfall.Theme;

namespace Blockfall.UI;

/// <summary>
/// The between-strata draft: the stage-clear banner, the banked score, and a
/// one-of-three charm pick (or SKIP for a small bank bonus). Cards are a single
/// thumb-reach vertical stack — this screen is visited four times per run, so it
/// must read in two seconds and never need a scroll hunt on a phone.
/// Offers come pre-planned from the run's seed (same seed = same offers), so this
/// screen only PRESENTS choices; it never rolls anything.
/// </summary>
public partial class CharmDraftScreen : Control
{
    public event Action<Charm>? CharmPicked;
    public event Action? Skipped;

    private readonly DescentRunState _state;
    private bool _choiceMade; // a double-tap must not pick twice

    public CharmDraftScreen(DescentRunState state) => _state = state;

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

        float colW = Mathf.Clamp(Bootstrap.Instance.SafeCanvasSize.X * 0.92f, 340f, 520f);
        var col = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            CustomMinimumSize = new Vector2(colW, 0),
        };
        col.AddThemeConstantOverride("separation", 12);
        outer.AddChild(col);
        outer.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });

        var run = _state.Run;
        int clearedStratum = run.CurrentStage.Stratum;

        // ---- Banner: what you just did + what you now hold ---------------------
        var banner = new Label
        {
            Text = Loc.T("STRATUM {0} CLEARED!", clearedStratum),
            ThemeTypeVariation = "TitleLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        banner.AddThemeFontSizeOverride("font_size", 34);
        banner.AddThemeColorOverride("font_color", Palette.AccentGold);
        col.AddChild(banner);

        var bank = new Label
        {
            Text = Loc.T("BANKED {0}", run.Bank.ToString("N0")),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        bank.AddThemeFontOverride("font", Fonts.UiBold);
        bank.AddThemeFontSizeOverride("font_size", 20);
        bank.AddThemeColorOverride("font_color", Palette.TextPrimary);
        col.AddChild(bank);

        // What's waiting below — the draft is a bet on the road ahead.
        var next = run.Director.Stages[run.StageIndex + 1];
        var nextLine = new Label
        {
            Text = Loc.T("NEXT: DEPTH {0}/{1}", next.Stratum, RunDirector.StageCount)
                   + "  ·  " + KindLabel(next.Kind),
            ThemeTypeVariation = "DimLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        nextLine.AddThemeFontSizeOverride("font_size", 15);
        col.AddChild(nextLine);

        col.AddChild(SpacerBox(4));

        var section = new Label
        {
            Text = Loc.T("CHOOSE A CHARM"),
            ThemeTypeVariation = "SectionLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        col.AddChild(section);

        // ---- The offer (pre-planned, never rolled here) -------------------------
        var offer = run.DraftOffer();
        var cards = new System.Collections.Generic.List<Control>();
        foreach (var charm in offer)
        {
            var c = charm;
            var card = BuildCharmCard(c);
            cards.Add(card);
            col.AddChild(card);
        }

        var skip = new Button
        {
            Text = Loc.T("SKIP (+{0} BANK)", RunDirector.SkipBonus.ToString("N0")),
            ThemeTypeVariation = "GhostButton",
            CustomMinimumSize = new Vector2(0, 52),
        };
        Motion.BindButtonFeel(skip);
        skip.Pressed += () => Choose(() => Skipped?.Invoke());
        col.AddChild(skip);

        if (run.Owned.Count > 0)
        {
            var owned = new Label
            {
                Text = Loc.T("OWNED: {0}", string.Join(" · ",
                    System.Linq.Enumerable.Select(run.Owned, o => Loc.T(CharmSet.Label(o))))),
                ThemeTypeVariation = "DimLabel",
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            owned.AddThemeFontSizeOverride("font_size", 13);
            col.AddChild(owned);
        }

        if (!Motion.Reduced)
            Motion.EnterStagger(cards.ToArray(), initialDelay: 0.08f);
    }

    private Button BuildCharmCard(Charm charm)
    {
        var b = new Button
        {
            ThemeTypeVariation = "CardButton",
            CustomMinimumSize = new Vector2(0, 84),
        };
        Motion.BindButtonFeel(b);

        var box = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        box.OffsetLeft = 16;
        box.OffsetRight = -16;
        box.AddThemeConstantOverride("separation", 14);
        b.AddChild(box);

        var bar = new ColorRect
        {
            Color = Palette.AccentViolet,
            CustomMinimumSize = new Vector2(4, 52),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        box.AddChild(bar);

        var text = new VBoxContainer
        {
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        text.AddThemeConstantOverride("separation", 2);
        var name = new Label { Text = Loc.T(CharmSet.Label(charm)) };
        name.AddThemeFontOverride("font", Fonts.UiBold);
        name.AddThemeFontSizeOverride("font_size", 20);
        name.AddThemeColorOverride("font_color", Palette.AccentViolet);
        text.AddChild(name);
        var desc = new Label
        {
            Text = Loc.T(CharmSet.Describe(charm)),
            ThemeTypeVariation = "DimLabel",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        desc.AddThemeFontSizeOverride("font_size", 14);
        text.AddChild(desc);
        box.AddChild(text);

        b.Pressed += () => Choose(() => CharmPicked?.Invoke(charm));
        return b;
    }

    /// <summary>Latch the first choice; later taps (double-tap, mid-fade) are no-ops.
    /// A router-busy press (focus activation during the crossfade) must NOT latch:
    /// its navigation would be dropped, and this screen has no other way out, so a
    /// latched no-op would soft-lock the run.</summary>
    private void Choose(Action fire)
    {
        if (_choiceMade || Bootstrap.Instance.Router.Busy) return;
        _choiceMade = true;
        Bootstrap.Instance.Audio.PlaySfx("level_up");
        fire();
    }

    private static string KindLabel(StageKind k) => k switch
    {
        StageKind.Dig => Loc.T("DIG"),
        StageKind.Burst => Loc.T("BURST"),
        StageKind.Siege => Loc.T("SIEGE"),
        _ => k.ToString().ToUpperInvariant(),
    };

    private static Control SpacerBox(float h)
        => new Control { CustomMinimumSize = new Vector2(0, h), MouseFilter = MouseFilterEnum.Ignore };
}
