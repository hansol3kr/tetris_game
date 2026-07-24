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
            // Candy dome: bright top, deep bottom, a glossy top sheen and a soft
            // bottom inner lip so the fill reads as an injection-molded gel button.
            var c = fill.Lerp(new Color(fill.R * 0.72f, fill.G * 0.72f, fill.B * 0.72f, fill.A), t);
            if (t < 0.46f) { float s = 1f - t / 0.46f; c = Over(c, new Color(1, 1, 1, 0.40f * s * s)); }
            if (t > 0.72f) { float s = (t - 0.72f) / 0.28f; c = Over(c, new Color(0, 0, 0, 0.16f * s)); }
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
        float radius = px * 0.22f;                 // chunky candy corner
        var half = new Vector2(px / 2f, px / 2f);
        return Bake(key, px, px, (x, y) =>
        {
            var p = new Vector2(x + 0.5f, y + 0.5f) - half;
            float sdf = RoundedBoxSdf(p, half, radius);
            float body = Feather(sdf, 1.2f);
            if (body <= 0f) return new Color(0, 0, 0, 0);

            // Body gradient: bright top, darker bottom (top-lit gel). Kept WHITE so
            // BoardView tints per piece; the wet white sheen is added on top by the
            // CellGloss overlay (a colored texture couldn't hold a white specular).
            float t = (y + 0.5f) / px;
            float v = Mathf.Lerp(1.00f, 0.80f, t);
            var c = new Color(v, v, v, 1f);

            // Inner pillow: darken the last ~20% toward every edge for a rounded,
            // extruded read; the top rim highlight below re-lights the top edge.
            float dEdge = -sdf;                       // px inside the shape
            float pillow = Mathf.Clamp(1f - dEdge / (px * 0.20f), 0f, 1f);
            float pm = Mathf.Lerp(1f, 0.88f, pillow);
            c = new Color(c.R * pm, c.G * pm, c.B * pm, 1f);

            // Crisp bottom bevel lip (bottom half, within ~2.5px of the edge).
            if (t > 0.5f && dEdge < 2.5f)
                c = Over(c, new Color(0, 0, 0, 0.22f * (1f - dEdge / 2.5f)));

            // Top rim highlight — lightens the top inner edge toward white.
            if (y < px * 0.20f)
                c = Over(c, new Color(1, 1, 1, 0.30f * (1f - y / (px * 0.20f))));

            return new Color(c.R, c.G, c.B, body);
        });
    }

    /// <summary>
    /// White wet-sheen overlay for a glossy gel cell: a soft top specular ellipse
    /// plus a small top-left glint, masked to the cell shape. Drawn over the tinted
    /// cell with a white modulate so the highlight stays white regardless of hue —
    /// this is what turns a flat tinted tile into a candy gem.
    /// </summary>
    public static ImageTexture CellGloss(int px)
    {
        px = Math.Clamp(px, 8, 128);
        string key = $"cellgloss:{px}";
        float radius = px * 0.22f;
        var half = new Vector2(px / 2f, px / 2f);
        return Bake(key, px, px, (x, y) =>
        {
            var p = new Vector2(x + 0.5f, y + 0.5f) - half;
            float body = Feather(RoundedBoxSdf(p, half, radius), 1.2f);
            if (body <= 0f) return new Color(0, 0, 0, 0);

            // Soft specular ellipse in the upper-left (single top-left light source).
            float ex = (x + 0.5f - 0.44f * px) / (0.34f * px);
            float ey = (y + 0.5f - 0.24f * px) / (0.20f * px);
            float spec = Mathf.Clamp(1f - (ex * ex + ey * ey), 0f, 1f);
            spec = spec * spec * 0.62f;

            // Sharp glint catch-light.
            float gx = x + 0.5f - 0.30f * px, gy = y + 0.5f - 0.26f * px;
            float gd = Mathf.Sqrt(gx * gx + gy * gy);
            float glint = Mathf.Clamp(1f - gd / (px * 0.09f), 0f, 1f);
            glint = glint * glint * 0.85f;

            float a = Mathf.Clamp(spec + glint, 0f, 1f) * body;
            return new Color(1f, 1f, 1f, a);
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

    // ---- Cell material overlays (the "finish" axis) -----------------------
    // All are WHITE/grayscale (tinted per-piece via DrawTextureRect modulate) except
    // HoloStrip, which bakes its own spectrum. Baked once per (kind,px) and cached —
    // only the EQUIPPED material's textures are ever built. See BlockRender for how
    // they layer on top of the base Cell to make a skin feel like a physical object.

    /// <summary>Deterministic hash in [0,1) for procedural speckle (frosted grain).</summary>
    private static float Hash(int x, int y)
    {
        int h = x * 374761393 + y * 668265263;
        h = (h ^ (h >> 13)) * 1274126177;
        return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
    }

    /// <summary>Metallic brushed sheen: a bright central specular column + fine vertical
    /// grain + a top metal rim. White overlay; the dark streaks come free from the
    /// darkened body showing through, so this holds no color (single modulate).</summary>
    public static ImageTexture CellBrushed(int px)
    {
        px = Math.Clamp(px, 8, 128);
        string key = $"brushed:{px}";
        float radius = px * 0.22f;
        var half = new Vector2(px / 2f, px / 2f);
        float freq = Mathf.Max(6f, px / 4f);
        return Bake(key, px, px, (x, y) =>
        {
            var p = new Vector2(x + 0.5f, y + 0.5f) - half;
            float mask = Feather(RoundedBoxSdf(p, half, radius), 1.2f);
            if (mask <= 0f) return new Color(0, 0, 0, 0);
            float xn = (x + 0.5f) / px - 0.5f;
            float band = Mathf.Clamp(1f - Mathf.Abs(xn) / 0.18f, 0f, 1f); band *= band * 0.55f;
            float grain = (0.5f + 0.5f * Mathf.Sin((x + 0.5f) * Mathf.Pi * freq / px) - 0.5f) * 0.12f;
            float top = Mathf.Clamp(1f - (y + 0.5f) / (px * 0.14f), 0f, 1f) * 0.25f;
            float a = Mathf.Clamp(band + grain + top, 0f, 1f) * mask;
            return new Color(1, 1, 1, a);
        });
    }

    /// <summary>Holographic spectrum strip, baked tall (px × 2·px) so a scrolling
    /// px-high window (via DrawTextureRectRegion) makes hue travel across the block —
    /// iridescent shimmer. Sampled inset, so the tinted body still frames it.</summary>
    public static ImageTexture HoloStrip(int px)
    {
        px = Math.Clamp(px, 8, 128);
        string key = $"holostrip:{px}";
        return Bake(key, px, px * 2, (x, y) =>
        {
            float d = (float)y / px;
            float hue = d + 0.15f * ((float)x / px);
            hue -= Mathf.Floor(hue);
            var col = Color.FromHsv(hue, 0.60f, 1.0f);
            float a = 0.75f + 0.25f * Mathf.Sin(d * Mathf.Tau * 3f);
            return new Color(col.R, col.G, col.B, a);
        });
    }

    /// <summary>Frosted matte body (replaces Cell for the Frosted material): softer
    /// corners, a speckled subsurface, a gentle rim, and NO sharp glint.</summary>
    public static ImageTexture CellFrost(int px)
    {
        px = Math.Clamp(px, 8, 128);
        string key = $"frost:{px}";
        float radius = px * 0.28f;
        var half = new Vector2(px / 2f, px / 2f);
        return Bake(key, px, px, (x, y) =>
        {
            var p = new Vector2(x + 0.5f, y + 0.5f) - half;
            float sdf = RoundedBoxSdf(p, half, radius);
            float mask = Feather(sdf, 1.5f);
            if (mask <= 0f) return new Color(0, 0, 0, 0);
            float t = (y + 0.5f) / px;
            float v = Mathf.Lerp(0.95f, 0.86f, t);
            v += (Hash(x, y) - 0.5f) * 0.06f;                 // fine matte speckle
            float dEdge = -sdf;
            v = Mathf.Lerp(v, v + 0.05f, Mathf.Clamp(1f - dEdge / (px * 0.14f), 0f, 1f) * 0.5f);
            v += 0.03f * Mathf.Clamp(1f - p.Length() / (px * 0.5f), 0f, 1f); // subsurface lift
            v = Mathf.Clamp(v, 0f, 1f);
            return new Color(v, v, v, mask);
        });
    }

    /// <summary>Broad, low-contrast diffuse sheen for the Frosted finish (no glint).</summary>
    public static ImageTexture CellFrostSheen(int px)
    {
        px = Math.Clamp(px, 8, 128);
        string key = $"frostsheen:{px}";
        float radius = px * 0.28f;
        var half = new Vector2(px / 2f, px / 2f);
        return Bake(key, px, px, (x, y) =>
        {
            var p = new Vector2(x + 0.5f, y + 0.5f) - half;
            float mask = Feather(RoundedBoxSdf(p, half, radius), 1.5f);
            if (mask <= 0f) return new Color(0, 0, 0, 0);
            float ex = ((x + 0.5f) - 0.5f * px) / (0.5f * px);
            float ey = ((y + 0.5f) - 0.30f * px) / (0.34f * px);
            float s = Mathf.Clamp(1f - (ex * ex + ey * ey), 0f, 1f);
            s = s * s * 0.5f;
            return new Color(1, 1, 1, s * mask);
        });
    }

    /// <summary>Faceted gemstone body: a bright central table + four pyramid facets with
    /// crisp seams and one hard corner glint. Grayscale (tinted per piece); the hard,
    /// un-feathered seams intentionally read as a jewel cut.</summary>
    public static ImageTexture CellGem(int px)
    {
        px = Math.Clamp(px, 8, 128);
        string key = $"gem:{px}";
        float radius = px * 0.14f;
        var half = new Vector2(px / 2f, px / 2f);
        float tableHalf = px * 0.22f;
        return Bake(key, px, px, (x, y) =>
        {
            var p = new Vector2(x + 0.5f, y + 0.5f) - half;
            float mask = Feather(RoundedBoxSdf(p, half, radius), 1.2f);
            if (mask <= 0f) return new Color(0, 0, 0, 0);
            float ax = Mathf.Abs(p.X), ay = Mathf.Abs(p.Y);
            float m;
            if (ax < tableHalf && ay < tableHalf) m = Mathf.Lerp(1.12f, 0.96f, (y + 0.5f) / px); // table
            else if (ax >= ay) m = p.X < 0 ? 0.86f : 0.76f;                                       // L/R facet
            else m = p.Y < 0 ? 1.02f : 0.66f;                                                      // top/bottom facet
            bool diagSeam = Mathf.Abs(ax - ay) < 0.9f;
            bool tableSeam = (Mathf.Abs(ax - tableHalf) < 0.9f && ay <= tableHalf)
                          || (Mathf.Abs(ay - tableHalf) < 0.9f && ax <= tableHalf);
            if (diagSeam || tableSeam) m *= 0.6f;
            float gx = x + 0.5f - 0.36f * px, gy = y + 0.5f - 0.30f * px;
            float glint = Mathf.Clamp(1f - Mathf.Sqrt(gx * gx + gy * gy) / (px * 0.07f), 0f, 1f);
            glint = glint * glint * 0.9f;
            float c = Mathf.Clamp(m, 0f, 1f) + glint;
            c = Mathf.Clamp(c, 0f, 1f);
            return new Color(c, c, c, mask);
        });
    }

    /// <summary>Bright inner-edge ring (white; tinted per skin) hugging the cell contour —
    /// the iridescent rim on Obsidian/holographic skins.</summary>
    public static ImageTexture CellRim(int px)
    {
        px = Math.Clamp(px, 8, 128);
        string key = $"cellrim:{px}";
        float radius = px * 0.22f;
        var half = new Vector2(px / 2f, px / 2f);
        return Bake(key, px, px, (x, y) =>
        {
            var p = new Vector2(x + 0.5f, y + 0.5f) - half;
            float sdf = RoundedBoxSdf(p, half, radius);
            float body = Feather(sdf, 1.2f);
            if (body <= 0f) return new Color(0, 0, 0, 0);
            float d = -sdf;                                    // px inside the shape
            float band = Mathf.Clamp(1f - Mathf.Abs(d - px * 0.07f) / (px * 0.07f), 0f, 1f);
            return new Color(1, 1, 1, band * 0.9f * body);
        });
    }

    /// <summary>Radial vignette frame (white; tinted dark at draw) — the Supernova blast
    /// darkens the screen edges around the white core. Baked once at 256.</summary>
    public static ImageTexture Vignette(int px = 256)
    {
        string key = $"vignette:{px}";
        var center = new Vector2(px / 2f, px / 2f);
        return Bake(key, px, px, (x, y) =>
        {
            float d = (new Vector2(x + 0.5f, y + 0.5f) - center).Length() / (px * 0.36f);
            float s = Mathf.Clamp((d - 0.6f) / 0.4f, 0f, 1f);
            s = s * s * (3f - 2f * s);                         // smoothstep
            return new Color(1, 1, 1, s);
        });
    }
}
