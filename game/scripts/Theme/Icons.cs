using Godot;

namespace Blockfall.Theme;

/// <summary>Which pictogram an <see cref="Icon"/> draws.</summary>
public enum IconKind
{
    Play, Timer, Bolt, Infinity, Shovel, Skull, Diamond,
    Calendar, Swords, Gear, Trophy, Dice, Refresh, Blocks,
}

/// <summary>
/// Tiny geometric line icons drawn in code (no assets): 2px rounded strokes on a
/// square canvas, tinted via <see cref="Tint"/>. Deliberately minimal — the icon
/// system exists to break up text-only buttons, not to be illustrative art.
/// </summary>
public partial class Icon : Control
{
    private IconKind _kind;
    private Color _tint = Palette.TextSecondary;

    public IconKind Kind { get => _kind; set { _kind = value; QueueRedraw(); } }
    public Color Tint { get => _tint; set { _tint = value; QueueRedraw(); } }

    public Icon() { }
    public Icon(IconKind kind, Color tint, float size = 24)
    {
        _kind = kind;
        _tint = tint;
        CustomMinimumSize = new Vector2(size, size);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        float s = Mathf.Min(Size.X, Size.Y);
        if (s <= 4) return;
        // All shapes live in a unit square; P maps to pixels with a small inset.
        float w = Mathf.Max(1.6f, s * 0.085f); // stroke width

        switch (_kind)
        {
            case IconKind.Play:
                DrawColoredPolygon(new[] { P(0.30f, 0.18f), P(0.85f, 0.5f), P(0.30f, 0.82f) }, _tint);
                break;

            case IconKind.Timer:
                DrawArc(P(0.5f, 0.56f), R(0.34f), 0, Mathf.Tau, 28, _tint, w, true);
                Line(0.5f, 0.56f, 0.5f, 0.32f, w);            // hand
                Line(0.40f, 0.10f, 0.60f, 0.10f, w);           // top button
                Line(0.5f, 0.10f, 0.5f, 0.20f, w);
                break;

            case IconKind.Bolt:
                DrawColoredPolygon(new[] { P(0.58f, 0.10f), P(0.30f, 0.55f), P(0.48f, 0.55f), P(0.42f, 0.90f), P(0.72f, 0.42f), P(0.53f, 0.42f) }, _tint);
                break;

            case IconKind.Infinity:
                DrawArc(P(0.32f, 0.5f), R(0.16f), 0, Mathf.Tau, 24, _tint, w, true);
                DrawArc(P(0.68f, 0.5f), R(0.16f), 0, Mathf.Tau, 24, _tint, w, true);
                break;

            case IconKind.Shovel:
                Line(0.62f, 0.14f, 0.34f, 0.42f, w);                       // handle shaft
                Line(0.55f, 0.07f, 0.69f, 0.21f, w);                       // grip
                DrawColoredPolygon(new[] { P(0.34f, 0.42f), P(0.52f, 0.60f), P(0.34f, 0.86f), P(0.14f, 0.66f) }, _tint); // blade
                break;

            case IconKind.Skull:
                DrawArc(P(0.5f, 0.42f), R(0.28f), Mathf.Pi * 0.95f, Mathf.Tau + Mathf.Pi * 0.05f, 24, _tint, w, true);
                Line(0.28f, 0.55f, 0.28f, 0.72f, w);
                Line(0.72f, 0.55f, 0.72f, 0.72f, w);
                Line(0.28f, 0.72f, 0.72f, 0.72f, w);
                DrawCircle(P(0.40f, 0.44f), R(0.055f), _tint);
                DrawCircle(P(0.60f, 0.44f), R(0.055f), _tint);
                break;

            case IconKind.Diamond:
                Stroke(new[] { P(0.5f, 0.12f), P(0.85f, 0.42f), P(0.5f, 0.88f), P(0.15f, 0.42f), P(0.5f, 0.12f) }, w);
                Line(0.15f, 0.42f, 0.85f, 0.42f, w);
                break;

            case IconKind.Calendar:
                Stroke(new[] { P(0.16f, 0.22f), P(0.84f, 0.22f), P(0.84f, 0.86f), P(0.16f, 0.86f), P(0.16f, 0.22f) }, w);
                Line(0.16f, 0.40f, 0.84f, 0.40f, w);
                Line(0.34f, 0.12f, 0.34f, 0.28f, w);
                Line(0.66f, 0.12f, 0.66f, 0.28f, w);
                DrawCircle(P(0.40f, 0.60f), R(0.05f), _tint);
                DrawCircle(P(0.60f, 0.60f), R(0.05f), _tint);
                break;

            case IconKind.Swords:
                Line(0.18f, 0.18f, 0.72f, 0.72f, w);   // blade 1
                Line(0.82f, 0.18f, 0.28f, 0.72f, w);   // blade 2
                Line(0.60f, 0.84f, 0.84f, 0.60f, w);   // guard 1
                Line(0.16f, 0.60f, 0.40f, 0.84f, w);   // guard 2
                break;

            case IconKind.Gear:
                DrawArc(P(0.5f, 0.5f), R(0.20f), 0, Mathf.Tau, 24, _tint, w, true);
                for (int i = 0; i < 8; i++)
                {
                    float a = Mathf.Tau * i / 8f;
                    var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    DrawLine(P(0.5f, 0.5f) + dir * R(0.28f), P(0.5f, 0.5f) + dir * R(0.40f), _tint, w);
                }
                break;

            case IconKind.Trophy:
                Stroke(new[] { P(0.30f, 0.14f), P(0.70f, 0.14f), P(0.70f, 0.44f), P(0.5f, 0.60f), P(0.30f, 0.44f), P(0.30f, 0.14f) }, w);
                DrawArc(P(0.24f, 0.26f), R(0.10f), Mathf.Pi * 0.5f, Mathf.Pi * 1.5f, 12, _tint, w, true);
                DrawArc(P(0.76f, 0.26f), R(0.10f), -Mathf.Pi * 0.5f, Mathf.Pi * 0.5f, 12, _tint, w, true);
                Line(0.5f, 0.60f, 0.5f, 0.76f, w);
                Line(0.34f, 0.86f, 0.66f, 0.86f, w);
                break;

            case IconKind.Dice:
                Stroke(new[] { P(0.18f, 0.18f), P(0.82f, 0.18f), P(0.82f, 0.82f), P(0.18f, 0.82f), P(0.18f, 0.18f) }, w);
                DrawCircle(P(0.35f, 0.35f), R(0.06f), _tint);
                DrawCircle(P(0.65f, 0.65f), R(0.06f), _tint);
                DrawCircle(P(0.65f, 0.35f), R(0.06f), _tint);
                DrawCircle(P(0.35f, 0.65f), R(0.06f), _tint);
                break;

            case IconKind.Refresh:
                DrawArc(P(0.5f, 0.5f), R(0.32f), Mathf.Pi * 0.15f, Mathf.Pi * 1.60f, 24, _tint, w, true);
                DrawColoredPolygon(new[] { P(0.86f, 0.34f), P(0.70f, 0.46f), P(0.70f, 0.24f) }, _tint);
                break;

            case IconKind.Blocks:
                // A 2x2 O-piece — the brand mark.
                Box(0.16f, 0.16f, w); Box(0.54f, 0.16f, w);
                Box(0.16f, 0.54f, w); Box(0.54f, 0.54f, w);
                break;
        }
    }

    private Vector2 P(float x, float y)
    {
        float s = Mathf.Min(Size.X, Size.Y);
        var origin = new Vector2((Size.X - s) / 2f, (Size.Y - s) / 2f);
        return origin + new Vector2(x, y) * s;
    }

    private float R(float f) => Mathf.Min(Size.X, Size.Y) * f;

    private void Line(float x0, float y0, float x1, float y1, float w)
        => DrawLine(P(x0, y0), P(x1, y1), _tint, w);

    private void Stroke(Vector2[] pts, float w) => DrawPolyline(pts, _tint, w);

    private void Box(float x, float y, float w)
    {
        float s = R(0.30f);
        DrawRect(new Rect2(P(x, y), new Vector2(s, s)), _tint, false, w);
    }
}
