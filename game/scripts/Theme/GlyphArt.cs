using Godot;

namespace Blockfall.Theme;

/// <summary>The cute face/emblem a "glyph" skin stamps on every block cell. None = plain block.</summary>
public enum SkinGlyph { None, Smile, Star, Heart, Sparkle, Flower, Ghost, Bolt }

/// <summary>
/// Draws the small emoji-like glyph a skin stamps on each block, using only vector
/// primitives (no assets, no emoji font — those render as tofu without a bundled
/// font). Callable from any node's <c>_Draw</c> via the CanvasItem. <paramref name="size"/>
/// is the glyph's full extent; <paramref name="ink"/> is the (usually dark, translucent)
/// mark colour, chosen to read on a bright neon cell.
/// </summary>
public static class GlyphArt
{
    public static void Draw(CanvasItem ci, SkinGlyph kind, Vector2 c, float size, Color ink)
    {
        float h = size * 0.5f;
        switch (kind)
        {
            case SkinGlyph.Smile:
                ci.DrawCircle(c + new Vector2(-h * 0.42f, -h * 0.28f), h * 0.15f, ink);
                ci.DrawCircle(c + new Vector2(h * 0.42f, -h * 0.28f), h * 0.15f, ink);
                ci.DrawArc(c + new Vector2(0, -h * 0.05f), h * 0.52f, 0.25f, Mathf.Pi - 0.25f, 12, ink, Mathf.Max(1.5f, size * 0.09f));
                break;
            case SkinGlyph.Star:
                ci.DrawColoredPolygon(StarPoints(c, h, 5, 0.45f, -Mathf.Pi / 2f), ink);
                break;
            case SkinGlyph.Sparkle:
                ci.DrawColoredPolygon(StarPoints(c, h, 4, 0.32f, -Mathf.Pi / 2f), ink);
                break;
            case SkinGlyph.Heart:
                ci.DrawCircle(c + new Vector2(-h * 0.30f, -h * 0.16f), h * 0.34f, ink);
                ci.DrawCircle(c + new Vector2(h * 0.30f, -h * 0.16f), h * 0.34f, ink);
                ci.DrawColoredPolygon(new[]
                {
                    c + new Vector2(-h * 0.62f, 0f),
                    c + new Vector2(h * 0.62f, 0f),
                    c + new Vector2(0f, h * 0.70f),
                }, ink);
                break;
            case SkinGlyph.Flower:
                for (int i = 0; i < 5; i++)
                {
                    float a = i * Mathf.Tau / 5f - Mathf.Pi / 2f;
                    ci.DrawCircle(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * h * 0.5f, h * 0.30f, ink);
                }
                ci.DrawCircle(c, h * 0.24f, new Color(ink.R, ink.G, ink.B, ink.A * 0.55f));
                break;
            case SkinGlyph.Ghost:
                ci.DrawCircle(c + new Vector2(0, -h * 0.12f), h * 0.60f, ink);
                ci.DrawRect(new Rect2(c + new Vector2(-h * 0.60f, -h * 0.12f), new Vector2(h * 1.20f, h * 0.66f)), ink);
                var eye = new Color(0.98f, 0.98f, 1f, 0.9f);
                ci.DrawCircle(c + new Vector2(-h * 0.22f, -h * 0.14f), h * 0.11f, eye);
                ci.DrawCircle(c + new Vector2(h * 0.22f, -h * 0.14f), h * 0.11f, eye);
                break;
            case SkinGlyph.Bolt:
                ci.DrawColoredPolygon(new[]
                {
                    c + new Vector2(h * 0.12f, -h * 0.72f),
                    c + new Vector2(-h * 0.44f, h * 0.10f),
                    c + new Vector2(-h * 0.02f, h * 0.04f),
                    c + new Vector2(-h * 0.12f, h * 0.72f),
                    c + new Vector2(h * 0.44f, -h * 0.08f),
                    c + new Vector2(h * 0.02f, -h * 0.02f),
                }, ink);
                break;
        }
    }

    private static Vector2[] StarPoints(Vector2 c, float outer, int points, float innerRatio, float rot)
    {
        var pts = new Vector2[points * 2];
        float inner = outer * innerRatio;
        for (int i = 0; i < points * 2; i++)
        {
            float r = (i % 2 == 0) ? outer : inner;
            float a = rot + i * Mathf.Pi / points;
            pts[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }
        return pts;
    }
}

/// <summary>A little Control that renders one <see cref="SkinGlyph"/> on a bright chip —
/// the shop/settings preview so a glyph skin reads at a glance.</summary>
public partial class GlyphIcon : Control
{
    private readonly SkinGlyph _kind;
    private readonly Color _face;
    private static readonly Color Ink = new(0.06f, 0.07f, 0.12f, 0.85f);

    public GlyphIcon(SkinGlyph kind, Color face, float size)
    {
        _kind = kind;
        _face = face;
        CustomMinimumSize = new Vector2(size, size);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        float s = Mathf.Min(Size.X, Size.Y);
        DrawRect(new Rect2(Vector2.Zero, Size), _face);
        if (_kind != SkinGlyph.None) GlyphArt.Draw(this, _kind, Size / 2f, s * 0.78f, Ink);
    }
}
