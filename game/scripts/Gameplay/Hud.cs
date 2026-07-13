using Godot;
using Blockfall.Core;
using Blockfall.Core.Localization;
using Blockfall.Theme;

namespace Blockfall.Gameplay;

/// <summary>
/// On-screen readouts: score / level / lines / clock in a glass card on the
/// left, Hold + Next queue in glass cards on the right (first NEXT emphasized).
/// Captions are tracked uppercase Rajdhani; values are Orbitron numerals inside
/// fixed-width containers so count changes never reflow the card. Built
/// programmatically so it scales to any resolution.
/// </summary>
public partial class Hud : Control
{
    private Label _score = null!, _level = null!, _lines = null!, _clock = null!, _goal = null!;
    private Label _finesseValue = null!, _finessePip = null!, _finesseStreak = null!;
    private Control _finesseBox = null!;
    private MiniPieceView _hold = null!;
    private readonly System.Collections.Generic.List<MiniPieceView> _next = new();
    private Game _game = null!;

    // Finesse readout (optional; only shown when a tracker is bound and the setting is on).
    private FinesseTracker? _finesse;
    private float _pipTimer, _flashTimer;
    private int _flashFaults;

    public void Bind(Game game) => _game = game;

    /// <summary>Attach the finesse tracker so the HUD can show the live input-economy readout.</summary>
    public void BindFinesse(FinesseTracker f)
    {
        _finesse = f;
        f.PieceScored += faults =>
        {
            if (faults == 0) _pipTimer = 0.35f;        // brief green tick
            else { _flashTimer = 0.6f; _flashFaults = faults; } // red "+N" flash (priority)
        };
    }

    public override void _Ready()
    {
        UiTheme.ApplyTo(this);
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // ---- Left: stats card ----------------------------------------------
        var statsCard = GlassPanel();
        statsCard.Position = new Vector2(16, 40);
        var leftCol = new VBoxContainer();
        leftCol.AddThemeConstantOverride("separation", 12);
        statsCard.AddChild(leftCol);
        AddChild(statsCard);

        _score = MakeStat(Loc.T("SCORE"), out var scoreBox);
        _level = MakeStat(Loc.T("LEVEL"), out var levelBox);
        _lines = MakeStat(Loc.T("LINES"), out var linesBox);
        _clock = MakeStat(Loc.T("TIME"), out var clockBox);
        _goal = MakeStat(Loc.T("GOAL"), out var goalBox);

        _finesseValue = MakeStat(Loc.T("FINESSE"), out var finesseBox);
        _finesseBox = finesseBox;
        _finessePip = MakeLabel("", Palette.TextSecondary, 15);
        finesseBox.AddChild(_finessePip);
        _finesseStreak = MakeLabel("", Palette.Accent, 14);
        finesseBox.AddChild(_finesseStreak);

        leftCol.AddChild(scoreBox);
        leftCol.AddChild(levelBox);
        leftCol.AddChild(linesBox);
        leftCol.AddChild(clockBox);
        leftCol.AddChild(goalBox);
        leftCol.AddChild(finesseBox);

        // ---- Right: hold + next --------------------------------------------
        var rightCol = new VBoxContainer();
        rightCol.SetAnchorsPreset(LayoutPreset.TopRight);
        rightCol.Position = new Vector2(-158, 40);
        rightCol.AddThemeConstantOverride("separation", 8);

        rightCol.AddChild(Section(Loc.T("HOLD")));
        var holdCard = GlassPanel();
        _hold = new MiniPieceView { CustomMinimumSize = new Vector2(112, 68) };
        holdCard.AddChild(_hold);
        rightCol.AddChild(holdCard);

        rightCol.AddChild(Spacer(6));
        rightCol.AddChild(Section(Loc.T("NEXT")));
        var nextCard = GlassPanel();
        var nextCol = new VBoxContainer();
        nextCol.AddThemeConstantOverride("separation", 4);
        nextCard.AddChild(nextCol);
        for (int i = 0; i < 5; i++)
        {
            // The first preview is the one that matters — it gets the size.
            var mv = new MiniPieceView
            {
                CustomMinimumSize = i == 0 ? new Vector2(112, 72) : new Vector2(112, 46),
                Dimmed = i > 0,
            };
            _next.Add(mv);
            nextCol.AddChild(mv);
        }
        rightCol.AddChild(nextCard);
        AddChild(rightCol);
    }

    private static PanelContainer GlassPanel()
    {
        var p = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        p.AddThemeStyleboxOverride("panel",
            TextureFactory.GlassStyle(Palette.RadiusM, Palette.GlassTop, Palette.GlassBottom,
                Palette.GlassBorder, 1f, 0.14f, 14, 10));
        return p;
    }

    private static Label Section(string text)
    {
        var l = new Label { Text = text, ThemeTypeVariation = "SectionLabel" };
        l.AddThemeFontSizeOverride("font_size", 13);
        return l;
    }

    private static Control Spacer(float h) => new Control { CustomMinimumSize = new Vector2(0, h), MouseFilter = MouseFilterEnum.Ignore };

    private Label MakeStat(string caption, out VBoxContainer box)
    {
        box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 0);
        box.AddChild(Section(caption));
        var val = new Label
        {
            Text = "0",
            ThemeTypeVariation = "StatValueLabel",
            // Fixed width: Orbitron digits are wide and non-tabular — without
            // this the card reflows every frame during play.
            CustomMinimumSize = new Vector2(148, 0),
        };
        val.AddThemeFontSizeOverride("font_size", 24);
        box.AddChild(val);
        return val;
    }

    private static Label MakeLabel(string text, Color color, int size)
    {
        var l = new Label { Text = text };
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeFontSizeOverride("font_size", size);
        return l;
    }

    public override void _Process(double delta)
    {
        if (_game is null) return;
        _score.Text = _game.Scoring.Score.ToString("N0");
        _level.Text = _game.Scoring.Level.ToString();
        _lines.Text = _game.Scoring.LinesCleared.ToString();
        _clock.Text = FormatTime(_game.Elapsed);
        _goal.Text = GoalText();

        _hold.Show(_game.Hold);
        var preview = _game.Preview();
        for (int i = 0; i < _next.Count; i++)
            _next[i].Show(i < preview.Count ? preview[i] : PieceType.Empty);

        UpdateFinesse(delta);
    }

    private void UpdateFinesse(double delta)
    {
        if (_finesse is null) return;
        bool show = Bootstrap.Instance.Save.Settings.ShowFinesse;
        _finesseBox.Visible = show;
        if (!show) return;

        _finesseValue.Text = $"{_finesse.FinessePercent:0}%  {RankLetter(_finesse.Rank)}";
        _finesseValue.AddThemeColorOverride("font_color", RankColor(_finesse.Rank));

        // Transient pip: a red fault flash takes priority over the green clean tick.
        if (_flashTimer > 0f)
        {
            _flashTimer -= (float)delta;
            _finessePip.Text = $"+{_flashFaults}";
            _finessePip.AddThemeColorOverride("font_color", Palette.AccentRed);
        }
        else if (_pipTimer > 0f)
        {
            _pipTimer -= (float)delta;
            _finessePip.Text = Loc.T("✓ CLEAN");
            _finessePip.AddThemeColorOverride("font_color", Palette.AccentGreen);
        }
        else
        {
            _finessePip.Text = "";
        }

        _finesseStreak.Text = _finesse.CleanStreak >= 3 ? Loc.T("CLEAN ×{0}", _finesse.CleanStreak) : "";
    }

    private static string RankLetter(FinesseRank r) => r == FinesseRank.None ? "—" : r.ToString();

    private static Color RankColor(FinesseRank r) => r switch
    {
        FinesseRank.S or FinesseRank.A => Palette.AccentGold,
        FinesseRank.B or FinesseRank.C => Palette.TextPrimary,
        _ => Palette.TextSecondary, // D/F/None — quiet, never alarm-red
    };

    private string GoalText() => _game.Mode.Id switch
    {
        GameModeId.Sprint => Loc.T("{0} left", Mathf.Max(0, _game.Mode.LineGoal - _game.Scoring.LinesCleared)),
        // System.Math.Max for the double args — Godot's Mathf.Max is float/int only.
        GameModeId.Ultra => FormatTime(System.Math.Max(0.0, _game.Mode.TimeLimit - _game.Elapsed)),
        GameModeId.Daily => FormatTime(System.Math.Max(0.0, _game.Mode.TimeLimit - _game.Elapsed)),
        GameModeId.Marathon => Loc.T("Lv {0}", _game.Mode.LevelCap),
        GameModeId.Dig => Loc.T("{0} rows", _game.Board.GarbageRowCount()),     // garbage left to dig
        GameModeId.Survival => $"▲ {_game.PendingGarbage}",            // incoming garbage
        GameModeId.Master => Loc.T("Lv {0}", _game.Scoring.Level),
        _ => "∞", // infinity for Zen
    };

    private static string FormatTime(double t)
    {
        int m = (int)(t / 60);
        double s = t - m * 60;
        return $"{m}:{s:00.00}";
    }
}

/// <summary>
/// Small self-drawing preview of a single piece (Hold / Next), using the same
/// baked rounded-cell texture as the board, with a quick scale pop whenever the
/// content changes so the queue reads as a living system.
/// </summary>
public partial class MiniPieceView : Control
{
    private PieceType _type = PieceType.Empty;
    private float _pop = 1f; // 0 → 1 after a change

    /// <summary>Later queue entries render dimmer so NEXT #1 leads.</summary>
    public bool Dimmed { get; set; }

    public MiniPieceView() => MouseFilter = MouseFilterEnum.Ignore;

    public void Show(PieceType type)
    {
        if (_type == type) return;
        _type = type;
        _pop = Motion.Reduced ? 1f : 0f;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_pop >= 1f) return;
        _pop = Mathf.Min(1f, _pop + (float)delta / 0.12f);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_type == PieceType.Empty) return;
        var cells = Tetromino.Cells(_type, RotationState.Spawn);
        // Center the piece's cells inside this control.
        int minC = 9, maxC = 0, minR = 9, maxR = 0;
        foreach (var c in cells)
        {
            minC = Mathf.Min(minC, c.Col); maxC = Mathf.Max(maxC, c.Col);
            minR = Mathf.Min(minR, c.Row); maxR = Mathf.Max(maxR, c.Row);
        }
        float pieceW = maxC - minC + 1, pieceH = maxR - minR + 1;
        float cell = Mathf.Floor(Mathf.Min(Size.X / (pieceW + 1), Size.Y / (pieceH + 1)));

        // Settle pop: 1.12 → 1.0 cubic-out.
        float ease = 1f - (1f - _pop) * (1f - _pop) * (1f - _pop);
        cell *= Mathf.Lerp(1.12f, 1f, ease);

        var origin = new Vector2((Size.X - cell * pieceW) / 2f, (Size.Y - cell * pieceH) / 2f);
        var color = Palette.ForPiece(_type);
        float alpha = Dimmed ? 0.55f : 1f;
        var tex = TextureFactory.Cell(Mathf.Clamp((int)cell, 8, 128));
        foreach (var c in cells)
        {
            var pos = origin + new Vector2((c.Col - minC) * cell, (c.Row - minR) * cell);
            DrawTextureRect(tex, new Rect2(pos + new Vector2(1, 1), new Vector2(cell - 2, cell - 2)),
                false, new Color(color.R, color.G, color.B, alpha));
        }
    }
}
