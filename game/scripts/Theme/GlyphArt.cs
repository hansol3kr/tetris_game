using Godot;

namespace Blockfall.Theme;

/// <summary>The cute face/emblem a "glyph" skin stamps on every block. None = plain block.
/// Append-only — values persist implicitly via the equipped <see cref="BlockTheme"/>.</summary>
public enum SkinGlyph { None, Smile, Star, Heart, Sparkle, Flower, Ghost, Bolt, Gem, Crown, Moon, Cat, Skull, Rainbow, Clover, Paw }

/// <summary>
/// Draws the emoji-like glyph a skin stamps on each block, using only vector primitives
/// (no assets, no emoji font — those render as tofu without a bundled font). Every glyph
/// is embossed for free: a soft drop-shadow below + the ink fill + a bright wet top-lip,
/// so it reads as a glossy vinyl sticker pressed INTO the candy cell rather than printed
/// flat on it. The dark-ink + white-rim stack is hue-free, so glyphs stay colorblind-safe
/// on any of the seven neon fills; only decorative <see cref="Details"/> (blush, jewels,
/// rainbow bands) carry hue, and never information.
///
/// Coordinate contract: <c>h = size*0.5</c>; a unit point (ux,uy)∈[-1,1] draws at
/// <c>c + (ux,uy)*h</c>; a unit radius ru draws at <c>ru*h</c>; stroke widths are given
/// as a fraction of <paramref name="size"/> (px).
/// </summary>
public static class GlyphArt
{
    /// <summary>
    /// Orchestrates the emboss passes. <paramref name="ink"/> is the mark colour; a LIGHT
    /// ink (chosen by the caller for dark tiles) flips to the inverse stack automatically.
    /// <paramref name="withDetails"/> adds the fixed-hue personality marks (skipped on small
    /// board cells for speed).
    /// </summary>
    public static void Draw(CanvasItem ci, SkinGlyph kind, Vector2 c, float size, Color ink, bool withDetails = true)
    {
        if (size <= 0f || kind == SkinGlyph.None) return;
        float h = size * 0.5f;
        float sd = Mathf.Max(size * 0.07f, 1.2f);
        float hd = Mathf.Max(size * 0.05f, 1.0f);

        // Bespoke stacks for the two glyphs that aren't a single embossable silhouette.
        if (kind == SkinGlyph.Rainbow) { DrawRainbow(ci, c, h, size, sd, withDetails); return; }
        if (kind == SkinGlyph.Skull)   { DrawSkull(ci, c, h, size, sd, hd, withDetails); return; }

        float inkLum = 0.299f * ink.R + 0.587f * ink.G + 0.114f * ink.B;
        bool light = inkLum > 0.5f;
        var down = new Vector2(0, 1f);
        var up = new Vector2(0, -1f);

        if (!light)
        {
            Paint(ci, kind, c + down * 1.8f * sd, h * 1.07f, new Color(0, 0, 0, ink.A * 0.28f));         // blur shadow
            Paint(ci, kind, c + down * sd, h * 1.04f, new Color(0, 0, 0, Mathf.Min(0.50f, ink.A * 0.55f))); // core shadow
            Paint(ci, kind, c + up * hd, h * 1.00f, new Color(1f, 0.98f, 0.92f, 0.50f));                  // top rim
            Paint(ci, kind, c, h, ink);                                                                    // fill
        }
        else
        {
            Paint(ci, kind, c + down * sd, h * 1.05f, new Color(0, 0, 0, 0.30f));            // drop shadow
            Paint(ci, kind, c, h * 1.10f, new Color(0.04f, 0.05f, 0.09f, 0.85f));            // fat dark outline
            Paint(ci, kind, c, h * 1.00f, ink);                                              // light fill
            Paint(ci, kind, c + up * hd, h * 0.96f, new Color(1, 1, 1, 0.55f));              // top lip
        }

        if (withDetails) Details(ci, kind, c, h, size);
    }

    // ---- Silhouettes (the embossable ink shape, re-drawn per pass) --------------

    private static void Paint(CanvasItem ci, SkinGlyph k, Vector2 c, float h, Color col)
    {
        switch (k)
        {
            case SkinGlyph.Smile:
                ci.DrawCircle(P(c, h, -0.40f, -0.22f), 0.17f * h, col);
                ci.DrawCircle(P(c, h, 0.40f, -0.22f), 0.17f * h, col);
                ci.DrawArc(P(c, h, 0f, -0.02f), 0.52f * h, 0.30f, Mathf.Pi - 0.30f, 16, col, Mathf.Max(2f, h * 0.22f));
                break;
            case SkinGlyph.Star:
                ci.DrawColoredPolygon(StarPoints(c, 0.98f * h, 5, 0.45f, -Mathf.Pi / 2f), col);
                break;
            case SkinGlyph.Sparkle:
                ci.DrawColoredPolygon(StarPoints(c, 0.95f * h, 4, 0.30f, -Mathf.Pi / 2f), col);
                break;
            case SkinGlyph.Heart:
                ci.DrawCircle(P(c, h, -0.32f, -0.18f), 0.36f * h, col);
                ci.DrawCircle(P(c, h, 0.32f, -0.18f), 0.36f * h, col);
                ci.DrawColoredPolygon(new[] { P(c, h, -0.66f, 0.02f), P(c, h, 0.66f, 0.02f), P(c, h, 0f, 0.74f) }, col);
                break;
            case SkinGlyph.Flower:
                for (int i = 0; i < 5; i++)
                {
                    float a = i * Mathf.Tau / 5f - Mathf.Pi / 2f;
                    ci.DrawCircle(P(c, h, Mathf.Cos(a) * 0.52f, Mathf.Sin(a) * 0.52f), 0.30f * h, col);
                }
                break;
            case SkinGlyph.Ghost:
                ci.DrawColoredPolygon(GhostBody(c, h), col);
                break;
            case SkinGlyph.Bolt:
                ci.DrawColoredPolygon(new[]
                {
                    P(c, h, 0.14f, -0.72f), P(c, h, -0.46f, 0.10f), P(c, h, -0.02f, 0.04f),
                    P(c, h, -0.14f, 0.72f), P(c, h, 0.46f, -0.06f), P(c, h, 0.04f, -0.02f),
                }, col);
                break;
            case SkinGlyph.Gem:
                ci.DrawColoredPolygon(new[]
                {
                    P(c, h, -0.34f, -0.42f), P(c, h, 0.34f, -0.42f), P(c, h, 0.60f, -0.14f),
                    P(c, h, 0f, 0.70f), P(c, h, -0.60f, -0.14f),
                }, col);
                break;
            case SkinGlyph.Crown:
                ci.DrawColoredPolygon(new[]
                {
                    P(c, h, -0.62f, 0.34f), P(c, h, 0.62f, 0.34f), P(c, h, 0.62f, 0.10f), P(c, h, 0.62f, -0.34f),
                    P(c, h, 0.30f, 0.02f), P(c, h, 0f, -0.50f), P(c, h, -0.30f, 0.02f), P(c, h, -0.62f, -0.34f), P(c, h, -0.62f, 0.10f),
                }, col);
                break;
            case SkinGlyph.Moon:
                ci.DrawColoredPolygon(CrescentPoints(c, h), col);
                break;
            case SkinGlyph.Cat:
                ci.DrawCircle(P(c, h, 0f, 0.05f), 0.62f * h, col);
                ci.DrawColoredPolygon(new[] { P(c, h, -0.60f, -0.10f), P(c, h, -0.20f, -0.28f), P(c, h, -0.50f, -0.72f) }, col);
                ci.DrawColoredPolygon(new[] { P(c, h, 0.60f, -0.10f), P(c, h, 0.20f, -0.28f), P(c, h, 0.50f, -0.72f) }, col);
                break;
            case SkinGlyph.Clover:
                for (int i = 0; i < 3; i++)
                {
                    float a = (-90f + i * 120f) * Mathf.Pi / 180f;
                    HeartLobe(ci, P(c, h, Mathf.Cos(a) * 0.34f, Mathf.Sin(a) * 0.34f), 0.50f * h, a + Mathf.Pi / 2f, col);
                }
                ci.DrawLine(P(c, h, 0f, 0.30f), P(c, h, 0f, 0.72f), col, Mathf.Max(1.5f, h * 0.12f));
                break;
            case SkinGlyph.Paw:
                ci.DrawCircle(P(c, h, 0f, 0.28f), 0.34f * h, col);
                ci.DrawCircle(P(c, h, -0.16f, 0.36f), 0.20f * h, col);
                ci.DrawCircle(P(c, h, 0.16f, 0.36f), 0.20f * h, col);
                ci.DrawCircle(P(c, h, -0.36f, -0.20f), 0.15f * h, col);
                ci.DrawCircle(P(c, h, -0.13f, -0.34f), 0.16f * h, col);
                ci.DrawCircle(P(c, h, 0.13f, -0.34f), 0.16f * h, col);
                ci.DrawCircle(P(c, h, 0.36f, -0.20f), 0.15f * h, col);
                break;
        }
    }

    // ---- Fixed-hue personality marks (drawn once, on top, never embossed) -------

    private static void Details(CanvasItem ci, SkinGlyph k, Vector2 c, float h, float size)
    {
        var white = new Color(1, 1, 1, 0.9f);
        var pink = new Color(1f, 0.45f, 0.55f, 0.36f);
        switch (k)
        {
            case SkinGlyph.Smile:
                ci.DrawCircle(P(c, h, -0.55f, 0.14f), 0.15f * h, pink);
                ci.DrawCircle(P(c, h, 0.55f, 0.14f), 0.15f * h, pink);
                ci.DrawCircle(P(c, h, -0.44f, -0.28f), 0.05f * h, white);
                ci.DrawCircle(P(c, h, 0.34f, -0.28f), 0.05f * h, white);
                break;
            case SkinGlyph.Star:
                ci.DrawColoredPolygon(StarPoints(P(c, h, -0.26f, -0.40f), 0.26f * h, 4, 0.28f, -Mathf.Pi / 2f), new Color(1, 1, 1, 0.85f));
                ci.DrawCircle(P(c, h, 0.30f, -0.06f), 0.06f * h, new Color(1, 1, 1, 0.7f));
                break;
            case SkinGlyph.Heart:
                ci.DrawArc(P(c, h, -0.26f, -0.20f), 0.20f * h, Mathf.Pi * 1.12f, Mathf.Pi * 1.75f, 8, new Color(1, 1, 1, 0.72f), size * 0.06f);
                ci.DrawCircle(P(c, h, -0.10f, -0.30f), 0.05f * h, white);
                break;
            case SkinGlyph.Sparkle:
                ci.DrawCircle(P(c, h, 0f, 0f), 0.10f * h, new Color(1, 1, 1, 0.7f));
                break;
            case SkinGlyph.Flower:
                ci.DrawCircle(P(c, h, 0f, 0f), 0.26f * h, new Color(1f, 0.85f, 0.32f, 0.9f));
                ci.DrawCircle(P(c, h, 0f, 0f), 0.09f * h, new Color(1, 1, 1, 0.85f));
                break;
            case SkinGlyph.Ghost:
                ci.DrawCircle(P(c, h, -0.24f, -0.14f), 0.13f * h, new Color(0.98f, 0.98f, 1f, 0.95f));
                ci.DrawCircle(P(c, h, 0.24f, -0.14f), 0.13f * h, new Color(0.98f, 0.98f, 1f, 0.95f));
                ci.DrawCircle(P(c, h, -0.22f, -0.10f), 0.055f * h, new Color(0.05f, 0.06f, 0.10f, 0.9f));
                ci.DrawCircle(P(c, h, 0.26f, -0.10f), 0.055f * h, new Color(0.05f, 0.06f, 0.10f, 0.9f));
                break;
            case SkinGlyph.Bolt:
                ci.DrawCircle(P(c, h, 0.14f, -0.72f), 0.05f * h, new Color(1, 1, 1, 0.8f));
                break;
            case SkinGlyph.Gem:
                ci.DrawColoredPolygon(new[] { P(c, h, -0.34f, -0.42f), P(c, h, 0.34f, -0.42f), P(c, h, 0f, -0.14f) }, new Color(1, 1, 1, 0.22f));
                ci.DrawLine(P(c, h, 0f, -0.14f), P(c, h, 0f, 0.70f), new Color(1, 1, 1, 0.30f), size * 0.03f);
                ci.DrawLine(P(c, h, -0.60f, -0.14f), P(c, h, 0.60f, -0.14f), new Color(1, 1, 1, 0.30f), size * 0.03f);
                ci.DrawCircle(P(c, h, -0.18f, -0.30f), 0.05f * h, white);
                break;
            case SkinGlyph.Crown:
                ci.DrawCircle(P(c, h, 0f, -0.52f), 0.10f * h, new Color(1f, 0.85f, 0.32f, 0.95f));
                ci.DrawCircle(P(c, h, -0.62f, -0.36f), 0.09f * h, new Color(1f, 0.85f, 0.32f, 0.95f));
                ci.DrawCircle(P(c, h, 0.62f, -0.36f), 0.09f * h, new Color(1f, 0.85f, 0.32f, 0.95f));
                ci.DrawCircle(P(c, h, 0f, 0.22f), 0.07f * h, new Color(1f, 0.36f, 0.46f, 0.95f));
                ci.DrawCircle(P(c, h, -0.34f, 0.24f), 0.06f * h, new Color(0.20f, 0.85f, 1f, 0.95f));
                ci.DrawCircle(P(c, h, 0.34f, 0.24f), 0.06f * h, new Color(0.20f, 0.85f, 1f, 0.95f));
                break;
            case SkinGlyph.Moon:
                ci.DrawCircle(P(c, h, -0.15f, -0.10f), 0.06f * h, new Color(0, 0, 0, 0.18f));
                ci.DrawCircle(P(c, h, -0.05f, 0.20f), 0.05f * h, new Color(0, 0, 0, 0.18f));
                ci.DrawColoredPolygon(StarPoints(P(c, h, 0.45f, -0.35f), 0.12f * h, 4, 0.30f, 0f), new Color(1f, 0.98f, 0.85f, 0.9f));
                ci.DrawColoredPolygon(StarPoints(P(c, h, 0.50f, 0.30f), 0.09f * h, 4, 0.30f, 0f), new Color(1f, 0.98f, 0.85f, 0.9f));
                break;
            case SkinGlyph.Cat:
                ci.DrawCircle(P(c, h, -0.26f, 0.0f), 0.14f * h, new Color(0.98f, 0.98f, 1f, 0.95f));
                ci.DrawCircle(P(c, h, 0.26f, 0.0f), 0.14f * h, new Color(0.98f, 0.98f, 1f, 0.95f));
                ci.DrawColoredPolygon(new[] { P(c, h, -0.06f, 0.14f), P(c, h, 0.06f, 0.14f), P(c, h, 0f, 0.22f) }, new Color(1f, 0.45f, 0.55f, 0.9f));
                for (int s = -1; s <= 1; s += 2)
                {
                    ci.DrawLine(P(c, h, 0.12f * s, 0.20f), P(c, h, 0.55f * s, 0.13f), new Color(0.05f, 0.06f, 0.10f, 0.8f), size * 0.03f);
                    ci.DrawLine(P(c, h, 0.12f * s, 0.26f), P(c, h, 0.55f * s, 0.27f), new Color(0.05f, 0.06f, 0.10f, 0.8f), size * 0.03f);
                }
                break;
            case SkinGlyph.Clover:
                ci.DrawCircle(P(c, h, 0f, 0f), 0.09f * h, new Color(0.36f, 0.95f, 0.56f, 0.7f));
                break;
            case SkinGlyph.Paw:
                ci.DrawCircle(P(c, h, -0.08f, 0.16f), 0.06f * h, new Color(1, 1, 1, 0.6f));
                break;
        }
    }

    // ---- Bespoke glyphs ---------------------------------------------------------

    private static void DrawRainbow(CanvasItem ci, Vector2 c, float h, float size, float sd, bool withDetails)
    {
        var arcC = P(c, h, 0f, 0.30f);
        // grounding shadow under the widest band
        ci.DrawArc(arcC + new Vector2(0, sd), 0.60f * h, Mathf.Pi, Mathf.Tau, 24, new Color(0, 0, 0, 0.30f), size * 0.11f);
        (float r, Color col)[] bands =
        {
            (0.60f, new Color(1f, 0.36f, 0.46f)), (0.48f, new Color(1f, 0.85f, 0.32f)),
            (0.36f, new Color(0.36f, 0.95f, 0.56f)), (0.24f, new Color(0.20f, 0.85f, 1f)),
        };
        foreach (var (r, col) in bands)
            ci.DrawArc(arcC, r * h, Mathf.Pi, Mathf.Tau, 24, new Color(col.R, col.G, col.B, 0.92f), size * 0.11f);
        ci.DrawArc(arcC, 0.64f * h, Mathf.Pi, Mathf.Tau, 24, new Color(1, 1, 1, 0.4f), size * 0.04f);
        if (withDetails)
            foreach (int s in new[] { -1, 1 })
            {
                var pc = P(c, h, 0.55f * s, 0.34f);
                ci.DrawCircle(pc + new Vector2(-0.13f * h, 0), 0.13f * h, new Color(1, 1, 1, 0.92f));
                ci.DrawCircle(pc, 0.16f * h, new Color(1, 1, 1, 0.92f));
                ci.DrawCircle(pc + new Vector2(0.13f * h, 0), 0.13f * h, new Color(1, 1, 1, 0.92f));
            }
    }

    private static void DrawSkull(CanvasItem ci, Vector2 c, float h, float size, float sd, float hd, bool withDetails)
    {
        var bone = new Color(0.94f, 0.94f, 0.90f, 0.96f);
        var ink = new Color(0.05f, 0.06f, 0.10f, 0.9f);
        // drop shadow + dark outline + bone fill + top lip
        SkullBody(ci, c + new Vector2(0, sd), h * 1.05f, new Color(0, 0, 0, 0.30f));
        SkullBody(ci, c, h * 1.10f, ink);
        SkullBody(ci, c, h, bone);
        SkullBody(ci, c + new Vector2(0, -hd), h * 0.94f, new Color(1, 1, 1, 0.45f));
        // dark features
        ci.DrawCircle(P(c, h, -0.24f, -0.10f), 0.16f * h, ink);
        ci.DrawCircle(P(c, h, 0.24f, -0.10f), 0.16f * h, ink);
        ci.DrawColoredPolygon(new[] { P(c, h, -0.07f, 0.06f), P(c, h, 0.07f, 0.06f), P(c, h, 0f, 0.24f) }, ink);
        for (int i = -1; i <= 1; i++)
            ci.DrawLine(P(c, h, 0.12f * i, 0.30f), P(c, h, 0.12f * i, 0.52f), ink, Mathf.Max(1.5f, size * 0.05f));
        if (withDetails)
        {
            ci.DrawCircle(P(c, h, -0.20f, -0.16f), 0.04f * h, new Color(1, 1, 1, 0.7f));
            ci.DrawCircle(P(c, h, 0.28f, -0.16f), 0.04f * h, new Color(1, 1, 1, 0.7f));
        }
    }

    private static void SkullBody(CanvasItem ci, Vector2 c, float h, Color col)
    {
        ci.DrawCircle(P(c, h, 0f, -0.10f), 0.56f * h, col);
        ci.DrawColoredPolygon(new[] { P(c, h, -0.30f, 0.28f), P(c, h, 0.30f, 0.28f), P(c, h, 0.24f, 0.62f), P(c, h, -0.24f, 0.62f) }, col);
    }

    // ---- Geometry helpers -------------------------------------------------------

    private static Vector2 P(Vector2 c, float h, float ux, float uy) => c + new Vector2(ux * h, uy * h);

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

    // A rotatable candy heart (two lobes + apex), used by Clover.
    private static void HeartLobe(CanvasItem ci, Vector2 c, float s, float rot, Color col)
    {
        Vector2 R(float ux, float uy)
        {
            float x = ux * s, y = uy * s;
            return c + new Vector2(x * Mathf.Cos(rot) - y * Mathf.Sin(rot), x * Mathf.Sin(rot) + y * Mathf.Cos(rot));
        }
        ci.DrawCircle(R(-0.34f, -0.16f), 0.34f * s, col);
        ci.DrawCircle(R(0.34f, -0.16f), 0.34f * s, col);
        ci.DrawColoredPolygon(new[] { R(-0.66f, 0.0f), R(0.66f, 0.0f), R(0f, 0.72f) }, col);
    }

    private static Vector2[] GhostBody(Vector2 c, float h)
    {
        var pts = new System.Collections.Generic.List<Vector2>();
        for (int i = 0; i <= 10; i++)            // domed head, PI → 0
        {
            float a = Mathf.Pi - i * Mathf.Pi / 10f;
            pts.Add(P(c, h, Mathf.Cos(a) * 0.60f, -0.12f + Mathf.Sin(a) * 0.60f));
        }
        // wavy skirt: right side down, scallop to the left
        pts.Add(P(c, h, 0.60f, 0.42f));
        float[] xs = { 0.45f, 0.30f, 0.15f, 0.0f, -0.15f, -0.30f, -0.45f };
        for (int i = 0; i < xs.Length; i++)
            pts.Add(P(c, h, xs[i], (i % 2 == 0) ? 0.50f : 0.30f));
        pts.Add(P(c, h, -0.60f, 0.42f));
        return pts.ToArray();
    }

    // Left-facing crescent lune (outer convex arc + inner concave cut).
    private static Vector2[] CrescentPoints(Vector2 c, float h)
    {
        const float Ro = 0.62f, Ri = 0.60f, Cx = 0.46f;
        var pts = new System.Collections.Generic.List<Vector2>();
        for (int i = 0; i < 14; i++)             // outer arc 65.6° → 294.4°
        {
            float a = Mathf.DegToRad(Mathf.Lerp(65.6f, 294.4f, i / 13f));
            pts.Add(P(c, h, Mathf.Cos(a) * Ro, Mathf.Sin(a) * Ro));
        }
        for (int i = 0; i < 10; i++)             // inner arc 250.2° → 109.8° (center offset +x)
        {
            float a = Mathf.DegToRad(Mathf.Lerp(250.2f, 109.8f, i / 9f));
            pts.Add(P(c, h, Cx + Mathf.Cos(a) * Ri, Mathf.Sin(a) * Ri));
        }
        return pts.ToArray();
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
        if (s <= 0f) return;                     // 0×0 guard — never divide h by zero
        DrawRect(new Rect2(Vector2.Zero, Size), _face);
        if (_kind != SkinGlyph.None) GlyphArt.Draw(this, _kind, Size / 2f, s * 0.78f, Ink);
    }
}
