using Godot;
using System;
using Blockfall.Core;
using Blockfall.Core.Localization;
using Blockfall.Gameplay;
using Blockfall.Theme;

namespace Blockfall.UI;

/// <summary>
/// Post-run summary: the headline metric counts up inside a glass card, a gold
/// NEW BEST badge punches in, stat rows reveal with a stagger, and the retry
/// button is the single loud (primary) action — the play-again loop is the
/// hottest path in the game. On mobile this is where the interstitial ad slot
/// lives (gated so it never interrupts mid-game and respects remove-ads).
/// </summary>
public partial class ResultsScreen : Control
{
    public event Action? PlayAgain;
    public event Action? ReplaySeed;
    public event Action? BackToMenu;
    public event Action? WatchReplay;

    private readonly GameModeId _mode;
    private readonly RunResults _results;

    public ResultsScreen(GameModeId mode, RunResults results)
    {
        _mode = mode;
        _results = results;
    }

    public override void _Ready()
    {
        UiTheme.ApplyTo(this);
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Offer an interstitial ad before showing results (mobile, non-paying users).
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

        // ---- Result card -----------------------------------------------------
        var card = new PanelContainer { ThemeTypeVariation = "Card" };
        var cardBox = new VBoxContainer();
        cardBox.AddThemeConstantOverride("separation", 8);
        card.AddChild(cardBox);
        col.AddChild(card);

        var heading = new Label
        {
            Text = _results.Completed ? Loc.T("CLEAR!") : Loc.T("GAME OVER"),
            ThemeTypeVariation = "TitleLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        heading.AddThemeFontSizeOverride("font_size", 40);
        heading.AddThemeColorOverride("font_color", _results.Completed ? Palette.Accent : Palette.TextPrimary);
        cardBox.AddChild(heading);

        if (_results.IsNewBest)
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

        // Achievement unlocks earned by this run.
        if (_results.UnlockedAchievements is { Length: > 0 } unlocked)
        {
            foreach (var id in unlocked)
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
        }

        // Headline metric: time (time attacks) or score, counting up.
        var caption = new Label
        {
            Text = GameMode.IsTimeAttack(_mode) ? Loc.T("TIME") : Loc.T("SCORE"),
            ThemeTypeVariation = "SectionLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        cardBox.AddChild(caption);

        var headline = new Label
        {
            ThemeTypeVariation = "HeadlineLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(320, 0), // Orbitron digits reflow otherwise
        };
        headline.AddThemeFontSizeOverride("font_size", 52);
        cardBox.AddChild(headline);
        if (GameMode.IsTimeAttack(_mode))
            headline.Text = FormatTime(_results.Time);
        else
            Motion.CountUp(headline, _results.Score, v => ((long)v).ToString("N0"));

        cardBox.AddChild(Divider());

        // ---- Stat rows (staggered reveal) ------------------------------------
        var s = _results.Stats;
        var rows = new System.Collections.Generic.List<Control>
        {
            Stat(Loc.T("LINES"), _results.Lines.ToString()),
            Stat(Loc.T("LEVEL"), _results.Level.ToString()),
            Stat(Loc.T("TETRISES"), s.Quads.ToString()),
            Stat(Loc.T("T-SPINS"), s.TSpins.ToString()),
            Stat(Loc.T("MAX COMBO"), s.MaxCombo.ToString()),
            Stat(Loc.T("PIECES/S"), s.PiecesPerSecond.ToString("0.00")),
        };
        if (_results.GarbageSent > 0)
            rows.Add(Stat(Loc.T("SENT"), _results.GarbageSent.ToString()));

        var fin = _results.Finesse;
        if (fin.ScoredPieces > 0 && Bootstrap.Instance.Save.Settings.ShowFinesse)
        {
            string rank = fin.Rank == FinesseRank.None ? "" : fin.Rank.ToString();
            rows.Add(Stat(Loc.T("FINESSE"), $"{fin.Percent:0}%  {rank}".TrimEnd()));
            rows.Add(Stat(Loc.T("FAULTS"), fin.TotalFaults.ToString()));
            rows.Add(Stat(Loc.T("BEST STREAK"), fin.LongestStreak.ToString()));
        }

        foreach (var row in rows) cardBox.AddChild(row);
        Motion.EnterStagger(rows.ToArray(), initialDelay: 0.15f);

        cardBox.AddChild(Divider());

        // Seed + modifiers footer inside the card.
        cardBox.AddChild(Stat(Loc.T("SEED"), SeedCode.Encode(_results.Seed), dim: true));
        if (_results.Modifiers is { Length: > 0 })
            cardBox.AddChild(Stat(Loc.T("MODS"),
                string.Join("  ", Array.ConvertAll(_results.Modifiers, ModifierSet.Label)), dim: true));

        // ---- Actions -----------------------------------------------------------
        col.AddChild(Spacer(8));
        col.AddChild(ActionButton(Loc.T("PLAY AGAIN"), "PrimaryButton", () => PlayAgain?.Invoke()));
        if (_results.Replay is { } replay)
        {
            col.AddChild(ActionButton(Loc.T("WATCH REPLAY"), "GhostButton", () => WatchReplay?.Invoke()));

            Button saveBtn = null!;
            saveBtn = ActionButton(Loc.T("SAVE REPLAY"), "GhostButton", () =>
            {
                var path = Platform.ReplayStore.Save(replay);
                saveBtn.Text = path is not null ? Loc.T("SAVED") + " ✓" : Loc.T("SAVE FAILED");
                saveBtn.Disabled = path is not null;
            });
            col.AddChild(saveBtn);

            Button shareBtn = null!;
            shareBtn = ActionButton(Loc.T("SHARE CODE"), "GhostButton", () =>
            {
                DisplayServer.ClipboardSet(replay.ToShareCode());
                shareBtn.Text = Loc.T("COPIED") + " ✓";
            });
            col.AddChild(shareBtn);
        }
        if (_mode != GameModeId.Daily)
            col.AddChild(ActionButton(Loc.T("REPLAY SEED"), "GhostButton", () => ReplaySeed?.Invoke()));
        col.AddChild(ActionButton(Loc.T("MENU"), "GhostButton", () => BackToMenu?.Invoke()));
    }

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

    private static Control Divider()
    {
        var d = new ColorRect
        {
            Color = new Color(1, 1, 1, 0.08f),
            CustomMinimumSize = new Vector2(0, 1),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        return d;
    }

    private static Control Spacer(float h) => new Control { CustomMinimumSize = new Vector2(0, h) };

    private static Control Stat(string cap, string value, bool dim = false)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        var capLabel = new Label
        {
            Text = cap,
            ThemeTypeVariation = "SectionLabel",
            CustomMinimumSize = new Vector2(150, 0),
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

    private static string FormatTime(double t)
    {
        int m = (int)(t / 60);
        double sec = t - m * 60;
        return $"{m}:{sec:00.00}";
    }
}
