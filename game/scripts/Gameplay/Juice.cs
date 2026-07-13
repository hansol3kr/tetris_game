using Godot;
using System.Collections.Generic;
using Blockfall.Core;
using Blockfall.Theme;

namespace Blockfall.Gameplay;

/// <summary>
/// All the "game feel" that makes clears satisfying: line-clear particle bursts,
/// floating popup text with a scale punch (TETRIS!, T-SPIN, COMBO ×N …),
/// a rate-limited full-board beam flash for the biggest moments, screen shake,
/// and mobile haptics. Feedback follows an escalation table — a triple must
/// never feel like a double. Purely cosmetic: reads nothing, mutates nothing on
/// the engine. Scales with <c>GameSettings.JuiceIntensity</c> (0 = off) and
/// respects reduced motion (popups stay — they're information — but punches,
/// shake, and flashes go).
/// </summary>
public partial class JuiceLayer : Node2D
{
    private struct Spark
    {
        public Vector2 Pos, Vel;
        public float Age, Life, Size;
        public Color Color;
    }

    private sealed class Popup
    {
        public string Text = "";
        public string Sub = "";
        public Vector2 Pos;
        public float Age, Life;
        public int FontSize;
        public Color Color;
        public float Punch = 1f; // entry scale (1 = none)
    }

    private readonly List<Spark> _sparks = new();
    private readonly List<Popup> _popups = new();
    private readonly RandomNumberGenerator _rng = new();

    private BoardView _board = null!;
    private float _intensity = 1f;

    // Screen shake: trauma decays each frame; visible offset ~ trauma².
    private Vector2 _boardBasePos;
    private float _trauma;
    private const float MaxShakePixels = 16f;

    // Full-board beam flash (TETRIS / PERFECT CLEAR): photosensitivity-budgeted —
    // peak 60% alpha, 200ms ramp-down, at most one per 500ms.
    private float _beamAge = 1f;
    private float _beamY, _beamH;
    private Color _beamColor;
    private float _sinceBeam = 10f;

    public void Configure(BoardView board, float intensity)
    {
        _board = board;
        _boardBasePos = board.Position;
        _intensity = Mathf.Clamp(intensity, 0f, 1f);
        _rng.Randomize();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _sinceBeam += dt;

        // --- Sparks ---
        for (int i = _sparks.Count - 1; i >= 0; i--)
        {
            var s = _sparks[i];
            s.Age += dt;
            if (s.Age >= s.Life) { _sparks.RemoveAt(i); continue; }
            s.Vel += new Vector2(0, 420f * dt); // gravity
            s.Vel *= 1f - 1.6f * dt;            // drag
            s.Pos += s.Vel * dt;
            _sparks[i] = s;
        }

        // --- Popups ---
        for (int i = _popups.Count - 1; i >= 0; i--)
        {
            var p = _popups[i];
            p.Age += dt;
            if (p.Age >= p.Life) { _popups.RemoveAt(i); continue; }
            if (!Motion.Reduced) p.Pos += new Vector2(0, -34f * dt); // drift up
        }

        // --- Beam ---
        if (_beamAge < 1f) _beamAge = Mathf.Min(1f, _beamAge + dt / 0.2f);

        // --- Screen shake ---
        if (_trauma > 0f)
        {
            _trauma = Mathf.Max(0f, _trauma - dt / 0.55f);
            float amp = MaxShakePixels * _intensity * _trauma * _trauma;
            var offset = new Vector2(_rng.RandfRange(-1, 1), _rng.RandfRange(-1, 1)) * amp;
            _board.Position = _boardBasePos + offset;
        }
        else if (_board.Position != _boardBasePos)
        {
            _board.Position = _boardBasePos;
        }

        if (_sparks.Count > 0 || _popups.Count > 0 || _beamAge < 1f)
            QueueRedraw();
    }

    public override void _Draw()
    {
        // Beam under everything else.
        if (_beamAge < 1f)
        {
            float a = (1f - _beamAge) * 0.6f;
            float w = _board.CellSize * _board.Columns;
            DrawRect(new Rect2(new Vector2(_board.BoardOrigin.X, _beamY), new Vector2(w, _beamH)),
                     new Color(_beamColor.R, _beamColor.G, _beamColor.B, a), filled: true);
        }

        foreach (var s in _sparks)
        {
            float t = 1f - s.Age / s.Life;
            var c = new Color(s.Color.R, s.Color.G, s.Color.B, s.Color.A * t);
            float sz = s.Size * (0.4f + 0.6f * t);
            DrawRect(new Rect2(s.Pos - new Vector2(sz, sz) * 0.5f, new Vector2(sz, sz)), c, filled: true);
        }

        float width = _board.CellSize * _board.Columns;
        foreach (var p in _popups)
        {
            // Alpha: pop in fast, hold, fade out.
            float t = p.Age / p.Life;
            float alpha = t < 0.15f ? t / 0.15f : Mathf.Clamp(1f - (t - 0.15f) / 0.85f, 0f, 1f);

            // Scale punch: 1.3 → 1.0 cubic-out over the first 120ms.
            float scale = 1f;
            if (!Motion.Reduced && p.Punch > 1f)
            {
                float pt = Mathf.Clamp(p.Age / 0.12f, 0f, 1f);
                float ease = 1f - (1f - pt) * (1f - pt) * (1f - pt);
                scale = Mathf.Lerp(p.Punch, 1f, ease);
            }

            var center = new Vector2(p.Pos.X + width / 2f, p.Pos.Y);
            DrawSetTransform(center, 0f, new Vector2(scale, scale));

            // Shadow pass keeps the text readable over a busy board.
            var shadow = new Color(0, 0, 0, alpha * 0.55f);
            DrawString(Fonts.Display, new Vector2(-width / 2f + 2, 3), p.Text,
                       HorizontalAlignment.Center, width, p.FontSize, shadow);
            var main = new Color(p.Color.R, p.Color.G, p.Color.B, alpha);
            DrawString(Fonts.Display, new Vector2(-width / 2f, 0), p.Text,
                       HorizontalAlignment.Center, width, p.FontSize, main);

            if (p.Sub.Length > 0)
            {
                var sub = new Color(Palette.TextPrimary.R, Palette.TextPrimary.G, Palette.TextPrimary.B, alpha * 0.9f);
                DrawString(Fonts.Ui, new Vector2(-width / 2f, p.FontSize * 0.95f), p.Sub,
                           HorizontalAlignment.Center, width, (int)(p.FontSize * 0.48f), sub);
            }
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }
    }

    // ---- Effect triggers (called from GameController on engine events) --------

    /// <summary>Line-clear burst: sparks + popup + shake + (for the crowns) a beam flash, per the escalation table.</summary>
    public void OnLineClear(IReadOnlyList<int> rows, ClearResult r, PieceType pieceType)
    {
        AddTrauma(TraumaFor(r));
        if (_intensity > 0f) SpawnClearSparks(rows, r);
        SpawnClearPopup(rows, r, pieceType);
        Haptic(r.LinesCleared >= 4 || r.PerfectClear ? 40 : 20);

        // Crown moments get the beam + an ambient background pulse.
        if ((r.LinesCleared >= 4 || r.PerfectClear) && !Motion.Reduced && _intensity > 0f && _sinceBeam >= 0.5f)
        {
            _sinceBeam = 0f;
            _beamAge = 0f;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (int row in rows)
            {
                float y = _board.VisibleRowY(row);
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y + _board.CellSize);
            }
            _beamY = minY; _beamH = Mathf.Max(_board.CellSize, maxY - minY);
            _beamColor = r.PerfectClear ? Palette.AccentGold : Palette.ForPiece(PieceType.I);
        }
        if (r.PerfectClear)
            Bootstrap.Instance.Bg.Pulse(Palette.AccentGold, 0.6f);
    }

    /// <summary>A landed hard drop: a short, snappy shake scaled by fall distance.</summary>
    public void OnHardDrop(int distance)
    {
        if (distance <= 0) return;
        AddTrauma(Mathf.Clamp(0.12f + distance * 0.012f, 0.12f, 0.32f));
        Haptic(10);
    }

    /// <summary>Attack readout: a "SENT ×N" flourish in the accent color.</summary>
    public void OnAttack(int lines)
    {
        _popups.Add(new Popup
        {
            Text = $"SENT ×{lines}",
            Pos = new Vector2(_board.BoardOrigin.X, _board.BoardOrigin.Y + _board.CellSize * 4.5f),
            Life = 1.0f,
            FontSize = System.Math.Max(16, (int)(_board.CellSize * 0.8f)),
            Color = Palette.Accent,
            Punch = 1.2f,
        });
        QueueRedraw();
    }

    /// <summary>Incoming garbage: a red "+N" near the stack plus a jolt.</summary>
    public void OnGarbage(int lines)
    {
        AddTrauma(Mathf.Clamp(0.15f + lines * 0.05f, 0.15f, 0.40f));
        Haptic(25);
        _popups.Add(new Popup
        {
            Text = $"+{lines}",
            Pos = new Vector2(_board.BoardOrigin.X, _board.BoardOrigin.Y + _board.CellSize * 15f),
            Life = 0.9f,
            FontSize = System.Math.Max(16, (int)(_board.CellSize * 0.9f)),
            Color = Palette.AccentRed,
            Punch = 1.25f,
        });
        QueueRedraw();
    }

    public void OnLevelUp(int level)
    {
        float cx = _board.BoardOrigin.X;
        float cy = _board.BoardOrigin.Y + _board.CellSize * 2.5f;
        _popups.Add(new Popup
        {
            Text = $"LEVEL {level}",
            Pos = new Vector2(cx, cy),
            Life = 1.3f,
            FontSize = System.Math.Max(18, (int)(_board.CellSize * 0.9f)),
            Color = Palette.Accent,
            Punch = 1.25f,
        });
        Bootstrap.Instance.Bg.Pulse(Palette.Accent, 0.4f);
        QueueRedraw();
    }

    /// <summary>The revive moment: a gold flourish + ambient pulse — a comeback should feel earned.</summary>
    public void OnRevive()
    {
        _popups.Add(new Popup
        {
            Text = "SECOND CHANCE!",
            Pos = new Vector2(_board.BoardOrigin.X, _board.BoardOrigin.Y + _board.CellSize * 7f),
            Life = 1.4f,
            FontSize = System.Math.Max(20, (int)(_board.CellSize * 1.0f)),
            Color = Palette.AccentGold,
            Punch = 1.4f,
        });
        Bootstrap.Instance.Bg.Pulse(Palette.AccentGold, 0.55f);
        Haptic(30);
        QueueRedraw();
    }

    /// <summary>Finesse clean-streak milestone: a gold "CLEAN ×N!" flourish plus a small jolt.</summary>
    public void OnFinesseMilestone(int streak)
    {
        _popups.Add(new Popup
        {
            Text = $"CLEAN ×{streak}!",
            Pos = new Vector2(_board.BoardOrigin.X, _board.BoardOrigin.Y + _board.CellSize * 8.5f),
            Life = 1.2f,
            FontSize = System.Math.Max(18, (int)(_board.CellSize * 0.85f)),
            Color = Palette.AccentGold,
            Punch = 1.2f,
        });
        AddTrauma(0.18f); // gated by intensity inside AddTrauma
        QueueRedraw();
    }

    private void AddTrauma(float amount)
    {
        if (_intensity <= 0f || Motion.Reduced) return;
        _trauma = Mathf.Min(1f, _trauma + amount);
    }

    private void Haptic(int ms)
    {
        if (_intensity <= 0f) return;
        if (OS.HasFeature("mobile")) Input.VibrateHandheld(ms);
    }

    /// <summary>
    /// The escalation table: every step up must FEEL bigger.
    /// single .10 / double .18 / triple .25 / tetris .40, spin +.12,
    /// perfect clear +.15, back-to-back +.05, combo up to +.10.
    /// </summary>
    private static float TraumaFor(ClearResult r)
    {
        float t = r.LinesCleared switch { >= 4 => 0.40f, 3 => 0.25f, 2 => 0.18f, 1 => 0.10f, _ => 0.12f };
        if (r.Spin != SpinType.None) t += 0.12f;
        if (r.PerfectClear) t += 0.15f;
        if (r.BackToBack) t += 0.05f;
        if (r.ComboCount > 0) t += Mathf.Min(0.02f * r.ComboCount, 0.10f);
        return Mathf.Min(t, 1f);
    }

    private void SpawnClearSparks(IReadOnlyList<int> rows, ClearResult r)
    {
        float cell = _board.CellSize;
        int cols = _board.Columns;
        float leftX = _board.BoardOrigin.X;
        int perCell = r.LinesCleared >= 4 ? 5 : 3;

        foreach (int boardRow in rows)
        {
            float y = _board.VisibleRowY(boardRow) + cell * 0.5f;
            for (int col = 0; col < cols; col++)
            {
                float x = leftX + (col + 0.5f) * cell;
                for (int k = 0; k < perCell; k++)
                {
                    var color = SparkColor(r);
                    _sparks.Add(new Spark
                    {
                        Pos = new Vector2(x + _rng.RandfRange(-cell * 0.3f, cell * 0.3f),
                                          y + _rng.RandfRange(-cell * 0.3f, cell * 0.3f)),
                        Vel = new Vector2(_rng.RandfRange(-140, 140), _rng.RandfRange(-260, -40)),
                        Life = _rng.RandfRange(0.35f, 0.75f),
                        Size = _rng.RandfRange(cell * 0.12f, cell * 0.28f),
                        Color = color,
                    });
                }
            }
        }
        QueueRedraw();
    }

    private Color SparkColor(ClearResult r)
    {
        if (r.PerfectClear) return Palette.Emissive(Palette.AccentGold, 1.6f);
        if (r.Spin != SpinType.None) return Palette.ForPiece(PieceType.T); // violet
        if (r.LinesCleared >= 4) return Palette.Emissive(Palette.ForPiece(PieceType.I), 1.5f); // cyan
        // Mostly-white spark with a faint cool tint.
        float w = _rng.RandfRange(0.75f, 1f);
        return new Color(w, w, Mathf.Min(1f, w + 0.1f), 1f);
    }

    private void SpawnClearPopup(IReadOnlyList<int> rows, ClearResult r, PieceType pieceType)
    {
        string text = ClearName(r, pieceType);
        if (text.Length == 0 && r.ComboCount < 1) return;

        // Vertical center of the cleared rows.
        float sum = 0;
        foreach (int row in rows) sum += _board.VisibleRowY(row);
        float y = rows.Count > 0 ? sum / rows.Count : _board.BoardOrigin.Y + _board.CellSize * 6;

        string sub = "";
        if (r.BackToBack) sub = "BACK-TO-BACK";
        if (r.ComboCount >= 1)
            sub = sub.Length > 0 ? $"{sub}   COMBO ×{r.ComboCount + 1}" : $"COMBO ×{r.ComboCount + 1}";

        _popups.Add(new Popup
        {
            Text = text.Length > 0 ? text : $"COMBO ×{r.ComboCount + 1}",
            Sub = text.Length > 0 ? sub : "",
            Pos = new Vector2(_board.BoardOrigin.X, y),
            Life = 1.15f,
            FontSize = PopupSize(r),
            Color = PopupColor(r),
            Punch = PunchFor(r),
        });
        QueueRedraw();
    }

    // Escalating entry punch: single 1.12 / double 1.2 / triple 1.28 / tetris+ 1.4.
    private static float PunchFor(ClearResult r)
    {
        if (r.PerfectClear || r.LinesCleared >= 4) return 1.4f;
        return r.LinesCleared switch { 3 => 1.28f, 2 => 1.2f, _ => 1.12f };
    }

    private int PopupSize(ClearResult r)
    {
        float scale = r.LinesCleared >= 4 || r.PerfectClear ? 1.15f : 0.85f;
        return System.Math.Max(18, (int)(_board.CellSize * scale));
    }

    private Color PopupColor(ClearResult r)
    {
        if (r.PerfectClear) return Palette.AccentGold;
        if (r.Spin != SpinType.None) return Palette.ForPiece(PieceType.T);
        if (r.LinesCleared >= 4) return Palette.ForPiece(PieceType.I);
        // Combos heat toward gold as they grow.
        float heat = Mathf.Clamp(r.ComboCount / 5f, 0f, 1f);
        return Palette.Accent.Lerp(Palette.AccentGold, heat);
    }

    private static string ClearName(ClearResult r, PieceType type)
    {
        if (r.PerfectClear) return "PERFECT CLEAR!";
        if (r.Spin != SpinType.None)
        {
            // T pieces read "T-SPIN"; all-spin S/Z/L/J/I read the generic "SPIN".
            string prefix = type == PieceType.T ? "T-SPIN" : "SPIN";
            if (r.Spin == SpinType.Mini)
                return r.LinesCleared > 0 ? $"{prefix} MINI {LineWord(r.LinesCleared)}" : $"{prefix} MINI";
            return r.LinesCleared > 0 ? $"{prefix} {LineWord(r.LinesCleared)}" : prefix;
        }
        return r.LinesCleared switch
        {
            4 => "TETRIS!",
            3 => "TRIPLE",
            2 => "DOUBLE",
            _ => "", // singles are quiet; combos still pop via the sub-line
        };
    }

    private static string LineWord(int lines) => lines switch
    {
        1 => "SINGLE",
        2 => "DOUBLE",
        3 => "TRIPLE",
        _ => "",
    };
}
