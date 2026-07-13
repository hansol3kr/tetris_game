using Godot;
using System;
using System.Collections.Generic;

namespace Blockfall.Theme;

/// <summary>
/// Bakes the small set of procedural textures the design system needs at startup:
/// rounded-gradient glass boxes (used as 9-patch styleboxes), board cells, soft
/// glow sprites, and circles. Baking once and drawing textured quads is far
/// cheaper than tessellating rounded StyleBoxFlats every frame, and it lets glass
/// panels have the vertical gradient + inner top highlight that StyleBoxFlat
/// can't express. All shapes are anti-aliased via a 1.5px SDF feather.
/// </summary>
public static class TextureFactory
{
    private static readonly Dictionary<string, ImageTexture> Cache = new();

    // ---- SDF helpers -------------------------------------------------------

    /// <summary>Signed distance from point p to a rounded box of half-size b, radius r (centered at origin).</summary>
    private static float RoundedBoxSdf(Vector2 p, Vector2 b, float r)
    {
        var q = new Vector2(Mathf.Abs(p.X), Mathf.Abs(p.Y)) - b + new Vector2(r, r);
        return new Vector2(Mathf.Max(q.X, 0), Mathf.Max(q.Y, 0)).Length()
               + Mathf.Min(Mathf.Max(q.X, q.Y), 0f) - r;
    }

    private static float Feather(float sdf, float px = 1.5f)
        => Mathf.Clamp(0.5f - sdf / px, 0f, 1f);

    private static Color Over(Color under, Color over)
    {
        float a = over.A + under.A * (1 - over.A);
        if (a <= 0.0001f) return new Color(0, 0, 0, 0);
        return new Color(
            (over.R * over.A + under.R * under.A * (1 - over.A)) / a,
            (over.G * over.A + under.G * under.A * (1 - over.A)) / a,
            (over.B * over.A + under.B * under.A * (1 - over.A)) / a,
            a);
    }

    private static ImageTexture Bake(string key, int w, int h, Func<int, int, Color> shade)
    {
        if (Cache.TryGetValue(key, out var hit)) return hit;
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                img.SetPixel(x, y, shade(x, y));
        var tex = ImageTexture.CreateFromImage(img);
        Cache[key] = tex;
        return tex;
    }

    // ---- Glass boxes (9-patch panels/buttons) -----------------------------

    /// <summary>
    /// A rounded box with a vertical gradient fill, border, and a 1px inner
    /// top-edge highlight — the glass recipe. Returned texture is meant for a
    /// 9-patch <see cref="StyleBoxTexture"/> (margins = radius + 2).
    /// </summary>
    public static ImageTexture GlassBox(int radius, Color top, Color bottom, Color border,
                                        float borderWidth = 1f, float highlightAlpha = 0.16f)
    {
        int size = Math.Max(radius * 2 + 18, 48);
        string key = $"glass:{radius}:{top}:{bottom}:{border}:{borderWidth}:{highlightAlpha}:{size}";
        var half = new Vector2(size / 2f, size / 2f);
        return Bake(key, size, size, (x, y) =>
        {
            var p = new Vector2(x + 0.5f, y + 0.5f) - half;
            float sdf = RoundedBoxSdf(p, half, radius);
            float body = Feather(sdf);
            if (body <= 0f) return new Color(0, 0, 0, 0);

            float t = (y + 0.5f) / size;
            var fill = top.Lerp(bottom, t);

            // Inner top-edge highlight: first ~1.5px inside the top of the shape.
            float insideTop = Mathf.Clamp((y + 0.5f) - (0.5f + borderWidth), 0, 2.5f);
            if (insideTop < 2.5f && sdf < -borderWidth)
            {
                float hl = (1f - insideTop / 2.5f) * highlightAlpha;
                fill = Over(fill, new Color(1, 1, 1, hl));
            }

            // Border ring: a feathered band hugging the inside of the edge.
            float ring = Mathf.Clamp(Feather(Mathf.Abs(sdf + borderWidth * 0.5f) - borderWidth * 0.5f), 0, 1);
            var col = Over(fill, new Color(border.R, border.G, border.B, border.A * ring));
            return new Color(col.R, col.G, col.B, col.A * body);
        });
    }

    /// <summary>Wrap a glass texture in a 9-patch stylebox with sane content margins.</summary>
    public static StyleBoxTexture GlassStyle(int radius, Color top, Color bottom, Color border,
                                             float borderWidth = 1f, float highlightAlpha = 0.16f,
                                             float marginH = 20, float marginV = 12)
    {
        var sb = new StyleBoxTexture { Texture = GlassBox(radius, top, bottom, border, borderWidth, highlightAlpha) };
        float m = radius + 3;
        sb.TextureMarginLeft = m; sb.TextureMarginRight = m;
        sb.TextureMarginTop = m; sb.TextureMarginBottom = m;
        sb.ContentMarginLeft = marginH; sb.ContentMarginRight = marginH;
        sb.ContentMarginTop = marginV; sb.ContentMarginBottom = marginV;
        return sb;
    }

    /// <summary>Solid accent-filled rounded box (primary buttons) with a soft top sheen.</summary>
    public static ImageTexture FillBox(int radius, Color fill)
    {
        int size = Math.Max(radius * 2 + 18, 48);
        string key = $"fill:{radius}:{fill}:{size}";
        var half = new Vector2(size / 2f, size / 2f);
        return Bake(key, size, size, (x, y) =>
        {
            var p = new Vector2(x + 0.5f, y + 0.5f) - half;
            float body = Feather(RoundedBoxSdf(p, half, radius));
            if (body <= 0f) return new Color(0, 0, 0, 0);
            float t = (y + 0.5f) / size;
            var c = fill.Lerp(new Color(fill.R * 0.82f, fill.G * 0.82f, fill.B * 0.82f, fill.A), t);
            if (t < 0.5f) c = Over(c, new Color(1, 1, 1, 0.14f * (1f - t * 2f)));
            return new Color(c.R, c.G, c.B, c.A * body);
        });
    }

    // ---- Board cell + glow --------------------------------------------------

    /// <summary>
    /// A white board cell: small corner radius, subtle vertical gradient
    /// (&lt;8% luminance delta), 1px top inner highlight. White so BoardView can
    /// tint per piece with the modulate parameter of DrawTextureRect.
    /// </summary>
    public static ImageTexture Cell(int px)
    {
        px = Math.Clamp(px, 8, 128);
        string key = $"cell:{px}";
        float radius = px * 0.11f;
        var half = new Vector2(px / 2f, px / 2f);
        return Bake(key, px, px, (x, y) =>
        {
            var p = new Vector2(x + 0.5f, y + 0.5f) - half;
            float body = Feather(RoundedBoxSdf(p, half, radius), 1.2f);
            if (body <= 0f) return new Color(0, 0, 0, 0);
            float t = (y + 0.5f) / px;
            float v = Mathf.Lerp(1.0f, 0.93f, t); // gentle two-tone
            var c = new Color(v, v, v, 1f);
            if (y < px * 0.16f) c = Over(c, new Color(1, 1, 1, 0.18f * (1f - y / (px * 0.16f))));
            return new Color(c.R, c.G, c.B, body);
        });
    }

    /// <summary>Soft rounded glow sprite (white, alpha falloff). Drawn under cells,
    /// modulated with the overbright emissive color to seed bloom.</summary>
    public static ImageTexture CellGlow(int px)
    {
        px = Math.Clamp(px, 8, 192);
        string key = $"cellglow:{px}";
        var half = new Vector2(px / 2f, px / 2f);
        float inner = px * 0.30f; // solid-ish core half-size
        return Bake(key, px, px, (x, y) =>
        {
            var p = new Vector2(x + 0.5f, y + 0.5f) - half;
            float sdf = RoundedBoxSdf(p, new Vector2(inner, inner), inner * 0.4f);
            float a = Mathf.Clamp(1f - sdf / (px * 0.22f), 0f, 1f);
            a = a * a * 0.55f; // quadratic falloff, subtle
            return new Color(1, 1, 1, a);
        });
    }

    /// <summary>Filled anti-aliased circle (touch buttons, slider grabbers).</summary>
    public static ImageTexture Circle(int px, Color fill, Color border, float borderWidth = 0f)
    {
        string key = $"circle:{px}:{fill}:{border}:{borderWidth}";
        var half = new Vector2(px / 2f, px / 2f);
        float r = px / 2f - 1f;
        return Bake(key, px, px, (x, y) =>
        {
            var p = new Vector2(x + 0.5f, y + 0.5f) - half;
            float sdf = p.Length() - r;
            float body = Feather(sdf);
            if (body <= 0f) return new Color(0, 0, 0, 0);
            var c = fill;
            if (borderWidth > 0f)
            {
                float ring = Feather(Mathf.Abs(sdf + borderWidth * 0.5f) - borderWidth * 0.5f);
                c = Over(c, new Color(border.R, border.G, border.B, border.A * ring));
            }
            return new Color(c.R, c.G, c.B, c.A * body);
        });
    }

    /// <summary>Soft radial glow disc (white) — slider grabber halo, particles.</summary>
    public static ImageTexture GlowDisc(int px)
    {
        string key = $"glowdisc:{px}";
        var half = new Vector2(px / 2f, px / 2f);
        return Bake(key, px, px, (x, y) =>
        {
            var p = new Vector2(x + 0.5f, y + 0.5f) - half;
            float t = Mathf.Clamp(1f - p.Length() / (px * 0.5f), 0f, 1f);
            return new Color(1, 1, 1, t * t * 0.8f);
        });
    }

    /// <summary>Checkbox glyph: rounded square, optionally with an accent check mark.</summary>
    public static ImageTexture CheckIcon(int px, bool ticked)
    {
        string key = $"check:{px}:{ticked}";
        var half = new Vector2(px / 2f, px / 2f);
        float radius = px * 0.22f;
        // Check mark segment endpoints (in unit space).
        var a0 = new Vector2(0.28f, 0.52f) * px;
        var a1 = new Vector2(0.44f, 0.68f) * px;
        var a2 = new Vector2(0.72f, 0.34f) * px;
        float stroke = Mathf.Max(1.6f, px * 0.09f);
        return Bake(key, px, px, (x, y) =>
        {
            var p = new Vector2(x + 0.5f, y + 0.5f);
            float sdf = RoundedBoxSdf(p - half, half - new Vector2(1, 1), radius);
            float body = Feather(sdf);
            if (body <= 0f) return new Color(0, 0, 0, 0);

            Color c;
            if (ticked)
            {
                c = new Color(Palette.Accent.R, Palette.Accent.G, Palette.Accent.B, 0.92f);
                float d = Mathf.Min(SegDist(p, a0, a1), SegDist(p, a1, a2));
                float mark = Feather(d - stroke * 0.5f, 1.2f);
                c = Over(c, new Color(Palette.InkOnAccent.R, Palette.InkOnAccent.G, Palette.InkOnAccent.B, mark));
            }
            else
            {
                c = new Color(1, 1, 1, 0.06f);
                float ring = Feather(Mathf.Abs(sdf + 0.75f) - 0.75f);
                c = Over(c, new Color(1, 1, 1, 0.28f * ring));
            }
            return new Color(c.R, c.G, c.B, c.A * body);
        });
    }

    private static float SegDist(Vector2 p, Vector2 a, Vector2 b)
    {
        var pa = p - a; var ba = b - a;
        float h = Mathf.Clamp(pa.Dot(ba) / ba.Dot(ba), 0f, 1f);
        return (pa - ba * h).Length();
    }
}
