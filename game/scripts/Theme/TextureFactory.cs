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

    /// <summary>Chromatic brushed metal: a bright central specular column + fine vertical
    /// grain + a top rim, with a faint teal→violet anodized tint and an R/B split at the
    /// column edges (ported from the Figma "Chromatic metal" shader's gradient + RGB-split
    /// idea). Baked WITH colour; BlockRender draws it white-modulated so the chroma survives
    /// on top of the darkened body — the anodized-chrome read.</summary>
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
            float Band(float o) { float b = Mathf.Clamp(1f - Mathf.Abs(xn - o) / 0.18f, 0f, 1f); return b * b; }
            float band = Band(0f) * 0.55f;
            float grain = (Mathf.Sin((x + 0.5f) * Mathf.Pi * freq / px)) * 0.06f;
            float top = Mathf.Clamp(1f - (y + 0.5f) / (px * 0.14f), 0f, 1f) * 0.25f;
            float baseA = Mathf.Clamp(band + grain + top, 0f, 1f);
            // RGB split: red leans left of the column, blue right → chromatic edge.
            float split = 0.06f;
            float r = baseA + Band(-split) * 0.14f;
            float b = baseA + Band(split) * 0.14f;
            float g = baseA;
            // Faint anodized tint gradient (teal→violet) across the sheen.
            var tint = Color.FromHsv(0.52f + 0.16f * xn + 0.06f * ((y + 0.5f) / px), 0.30f, 1f);
            float chroma = band * 0.5f;
            float R = Mathf.Lerp(r, tint.R, chroma * tint.R);
            float G = Mathf.Lerp(g, tint.G, chroma * tint.G);
            float B = Mathf.Lerp(b, tint.B, chroma);
            float a = Mathf.Clamp(Mathf.Max(R, Mathf.Max(G, B)), 0f, 1f) * mask;
            return new Color(R, G, B, a);
        });
    }

    /// <summary>Holographic "oil-slick" iridescence strip, baked tall (px × 2·px) so a
    /// scrolling px-high window (DrawTextureRectRegion) makes the sheen travel across the
    /// block. Ported from the Figma "Mesh gradient" / "Gradient map" technique: instead of
    /// a flat hue ramp, the hue sweeps on two harmonics with a diagonal skew, saturation
    /// breathes, and the alpha rides soft luminance bands — the shifting soap-film read.</summary>
    public static ImageTexture HoloStrip(int px)
    {
        px = Math.Clamp(px, 8, 128);
        string key = $"holostrip:{px}";
        return Bake(key, px, px * 2, (x, y) =>
        {
            float d = (float)y / px;               // 0..2 along the strip
            float xn = (float)x / px;              // 0..1 across
            // Two-harmonic hue sweep + diagonal skew → the colours bleed like an oil film.
            float hue = d * 0.5f + 0.12f * Mathf.Sin(d * Mathf.Tau * 1.5f) + 0.15f * xn;
            hue -= Mathf.Floor(hue);
            float sat = Mathf.Clamp(0.42f + 0.26f * Mathf.Sin(d * Mathf.Tau + xn * 3f), 0f, 0.78f);
            var col = Color.FromHsv(hue, sat, 1.0f);
            // Soft moving luminance bands (two frequencies) — the wet shimmer.
            float a = 0.66f + 0.20f * Mathf.Sin(d * Mathf.Tau * 2.5f + xn * 2f)
                            + 0.12f * Mathf.Sin(d * Mathf.Tau * 5f);
            return new Color(col.R, col.G, col.B, Mathf.Clamp(a, 0f, 1f));
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

    /// <summary>Turned-timber body (replaces Cell for the Wood material): a vertical
    /// brightness ramp, a warm top-left bevel and a dark bottom-right chamfer, and NO
    /// specular gloss — wood is matte, which is exactly what makes it read as not-plastic
    /// next to the gel skins. Grayscale; tinted per piece via modulate.</summary>
    public static ImageTexture CellWood(int px)
    {
        px = Math.Clamp(px, 8, 128);
        string key = $"wood:{px}";
        float radius = px * 0.16f;
        var half = new Vector2(px / 2f, px / 2f);
        return Bake(key, px, px, (x, y) =>
        {
            var p = new Vector2(x + 0.5f, y + 0.5f) - half;
            float sdf = RoundedBoxSdf(p, half, radius);
            float body = Feather(sdf, 1.2f);
            if (body <= 0f) return new Color(0, 0, 0, 0);
            float t = (y + 0.5f) / px;
            float v = Mathf.Lerp(0.90f, 1.00f, t);
            float dEdge = -sdf;                                  // px inside the shape
            // Dark chamfer on the bottom-right two edges, warm bevel on the top-left two.
            if (dEdge < 2f && (p.X > 0f || p.Y > 0f) && Mathf.Abs(p.X) + Mathf.Abs(p.Y) > px * 0.18f)
                v *= Mathf.Lerp(0.72f, 1f, dEdge / 2f);
            if (dEdge < 2f && p.X <= 0f && p.Y <= 0f)
                v *= Mathf.Lerp(1.14f, 1f, dEdge / 2f);
            v = Mathf.Clamp(v, 0f, 1f);
            return new Color(v, v, v, body);
        });
    }

    /// <summary>Growth rings for the Wood finish: dark (RGB 0) alpha-only arcs so the grain
    /// reads on ANY hue the skin paints the cell — it is drawn as a normal-alpha overlay,
    /// not multiplied, so a pale oak and a deep walnut both keep their timber.</summary>
    public static ImageTexture CellWoodGrain(int px)
    {
        px = Math.Clamp(px, 8, 128);
        string key = $"woodgrain:{px}";
        float radius = px * 0.16f;
        var half = new Vector2(px / 2f, px / 2f);
        float cx = px * 1.7f * 0.5f, cy = px * 0.5f;             // off-canvas pith → near-parallel grain
        float period = px * 0.11f;
        return Bake(key, px, px, (x, y) =>
        {
            var p = new Vector2(x + 0.5f, y + 0.5f) - half;
            float mask = Feather(RoundedBoxSdf(p, half, radius), 1.2f);
            if (mask <= 0f) return new Color(0, 0, 0, 0);
            float dx = (x + 0.5f) - cx, dy = (y + 0.5f) - cy;
            float d = Mathf.Sqrt(dx * dx * 0.35f + dy * dy);
            float ring = d / period;
            ring -= Mathf.Floor(ring);
            float s = Mathf.Abs(ring - 0.5f) * 2f;
            s = Mathf.Clamp((s - 0.42f) / 0.08f, 0f, 1f);
            s = s * s * (3f - 2f * s);                            // smoothstep(0.42, 0.50)
            return new Color(0f, 0f, 0f, 0.30f * s * mask);
        });
    }

    /// <summary>Hollow arcade tube (replaces Cell for the NeonTube material): a nearly empty
    /// core with a bright glass wall, so the cell reads as bent neon rather than a filled
    /// tile. One draw call — cheaper than the gel body+gloss pair.</summary>
    public static ImageTexture CellTube(int px)
    {
        px = Math.Clamp(px, 8, 128);
        string key = $"tube:{px}";
        float radius = px * 0.24f;
        var half = new Vector2(px / 2f, px / 2f);
        return Bake(key, px, px, (x, y) =>
        {
            var p = new Vector2(x + 0.5f, y + 0.5f) - half;
            float sdf = RoundedBoxSdf(p, half, radius);
            float body = Feather(sdf, 1.2f);
            if (body <= 0f) return new Color(0, 0, 0, 0);
            float dEdge = -sdf;                                   // px inside the shape
            float wall = px * 0.18f * 0.5f;                       // outer 18% is the glass wall
            float k = Mathf.Clamp(1f - dEdge / Mathf.Max(1f, wall), 0f, 1f);
            k = k * k * (3f - 2f * k);                            // smoothstep toward the rim
            float a = Mathf.Lerp(0.15f, 0.95f, k);
            return new Color(1f, 1f, 1f, a * body);
        });
    }

    // ---- Animal-set finishes (Pelt / Scale / Chitin) -----------------------
    // Three anatomical SURFACES, never a creature: coat strands, overlapping scales, a hex
    // carapace. No eyes, no mouth, no species marking — a face is what would turn a texture
    // into a character, and a character is what carries someone else's IP. Each finish is one
    // grayscale body bake + one alpha-only overlay bake, so a set costs the same two draws a
    // Wood cell already costs, and none of them has a time term (nothing to gate under
    // reduced motion).

    /// <summary>Matte coat body (replaces Cell for the Pelt material): a soft vertical
    /// brightness ramp with a warm top-left bevel and a dark bottom-right chamfer, and a
    /// notably ROUNDER corner than timber — fur has no hard arris. Grayscale; tinted per
    /// piece via modulate.</summary>
    public static ImageTexture CellPelt(int px)
    {
        px = Math.Clamp(px, 8, 128);
        string key = $"pelt:{px}";
        float radius = px * 0.20f;                               // fur has no square corner
        var half = new Vector2(px / 2f, px / 2f);
        return Bake(key, px, px, (x, y) =>
        {
            var p = new Vector2(x + 0.5f, y + 0.5f) - half;
            float sdf = RoundedBoxSdf(p, half, radius);
            float body = Feather(sdf, 1.2f);
            if (body <= 0f) return new Color(0, 0, 0, 0);
            float t = (y + 0.5f) / px;
            float v = Mathf.Lerp(0.88f, 1.00f, t);
            float dEdge = -sdf;
            if (dEdge < 2f && (p.X > 0f || p.Y > 0f) && Mathf.Abs(p.X) + Mathf.Abs(p.Y) > px * 0.18f)
                v *= Mathf.Lerp(0.74f, 1f, dEdge / 2f);
            if (dEdge < 2f && p.X <= 0f && p.Y <= 0f)
                v *= Mathf.Lerp(1.12f, 1f, dEdge / 2f);
            v = Mathf.Clamp(v, 0f, 1f);
            return new Color(v, v, v, body);
        });
    }

    /// <summary>Coat strands for the Pelt finish: WHITE alpha-only streaks running at a fixed
    /// 45°, so the grain has a direction the eye can name ("brushed one way") instead of the
    /// concentric rings that make Wood read as timber. Drawn as a normal-alpha overlay, so the
    /// strands stay light on a cream coat and a cocoa one alike.</summary>
    public static ImageTexture CellFur(int px)
    {
        px = Math.Clamp(px, 8, 128);
        string key = $"fur:{px}";
        float radius = px * 0.20f;
        var half = new Vector2(px / 2f, px / 2f);
        float period = Mathf.Max(1.5f, px * 0.085f);
        const float Diag = 0.70710678f;
        return Bake(key, px, px, (x, y) =>
        {
            var p = new Vector2(x + 0.5f, y + 0.5f) - half;
            float mask = Feather(RoundedBoxSdf(p, half, radius), 1.2f);
            if (mask <= 0f) return new Color(0, 0, 0, 0);
            float phase = ((x + 0.5f) * Diag + (y + 0.5f) * Diag) / period;
            phase -= Mathf.Floor(phase);
            float s = Mathf.Abs(phase - 0.5f) * 2f;
            s = Mathf.Clamp((s - 0.55f) / 0.40f, 0f, 1f);
            s = s * s * (3f - 2f * s);                            // smoothstep(0.55, 0.95)
            return new Color(1f, 1f, 1f, 0.34f * s * mask);
        });
    }

    // One scale lobe grid, shared by the body and the edge overlay so the highlight lands on
    // the arc it is highlighting. Two lobes across, ~three rows down, odd rows offset by half
    // a step. The ROW pitch is deliberately far shorter than the lobe diameter (0.34 vs 0.60):
    // at an even pitch the lobes tile as packed spheres — bubble wrap, not skin. Only a heavy
    // vertical overlap leaves each lobe showing as a CRESCENT of its lower arc, which is the
    // read the finish is named for.
    private const float ScaleLobeR = 0.30f;      // × px
    private const float ScaleStepX = 0.50f;      // × px
    private const float ScaleStepY = 0.34f;      // × px — the overlap, hence the crescent

    private static Vector2 ScaleLobeCentre(int i, int j, float px)
        => new((i + ((j & 1) == 0 ? 0f : 0.5f)) * ScaleStepX * px, j * ScaleStepY * px);

    /// <summary>Overlapping-scale body (replaces Cell for the Scale material): a 2×2 lobe
    /// tessellation where each lobe is the lower crescent of a disc and the UPPER lobe wins the
    /// overlap, so the free edge the eye follows is always a downward arc. Shape, not hue —
    /// this is what still says "reptile skin" once colorblind mode has folded the palette.</summary>
    public static ImageTexture CellScale(int px)
    {
        px = Math.Clamp(px, 8, 128);
        string key = $"scale:{px}";
        float radius = px * 0.14f;
        var half = new Vector2(px / 2f, px / 2f);
        float R = px * ScaleLobeR;
        return Bake(key, px, px, (x, y) =>
        {
            var p = new Vector2(x + 0.5f, y + 0.5f) - half;
            float mask = Feather(RoundedBoxSdf(p, half, radius), 1.2f);
            if (mask <= 0f) return new Color(0, 0, 0, 0);
            var q = new Vector2(x + 0.5f, y + 0.5f);
            float v = 0.62f;                                       // gap between lobes = the seam
            // Bottom rows first, top rows last: the last write wins, so an upper lobe overlaps
            // the one below it and every visible boundary is a lobe's LOWER arc.
            for (int j = 4; j >= -1; j--)
                for (int i = -1; i <= 2; i++)
                {
                    var c = ScaleLobeCentre(i, j, px);
                    float d = (q - c).Length();
                    if (d > R) continue;
                    float dn = d / R;
                    float u = (q.Y - c.Y) / R;                     // -1 top … +1 bottom
                    float dome = 0.70f + 0.36f * (1f - dn * dn);
                    dome *= Mathf.Lerp(1.08f, 0.78f, Mathf.Clamp((u + 1f) * 0.5f, 0f, 1f));
                    if (R - d < 1.1f) dome *= 0.72f;               // crisp lobe rim
                    v = dome;
                }
            v = Mathf.Clamp(v, 0f, 1f);
            return new Color(v, v, v, mask);
        });
    }

    /// <summary>Wet crescent highlights for the Scale finish: a white alpha-only band hugging
    /// the lower arc of every lobe. This is the finish's SHAPE channel — with hue stripped the
    /// body ramp alone reads as a blur, and these arcs are what keep the overlap legible.</summary>
    public static ImageTexture CellScaleEdge(int px)
    {
        px = Math.Clamp(px, 8, 128);
        string key = $"scaleedge:{px}";
        float radius = px * 0.14f;
        var half = new Vector2(px / 2f, px / 2f);
        float R = px * ScaleLobeR;
        float w = Mathf.Max(1.2f, px * 0.06f);
        return Bake(key, px, px, (x, y) =>
        {
            var p = new Vector2(x + 0.5f, y + 0.5f) - half;
            float mask = Feather(RoundedBoxSdf(p, half, radius), 1.2f);
            if (mask <= 0f) return new Color(0, 0, 0, 0);
            var q = new Vector2(x + 0.5f, y + 0.5f);
            float best = 0f;
            for (int j = -1; j <= 4; j++)
                for (int i = -1; i <= 2; i++)
                {
                    var c = ScaleLobeCentre(i, j, px);
                    var d = q - c;
                    if (d.Y <= 0f) continue;                       // lower arc only
                    float band = Mathf.Abs(d.Length() - R);
                    if (band > w) continue;
                    float s = 1f - band / w;
                    s *= Mathf.Clamp(d.Y / Mathf.Max(1f, R * 0.55f), 0f, 1f);  // fade toward the sides
                    if (s > best) best = s;
                }
            if (best <= 0f) return new Color(0, 0, 0, 0);
            return new Color(1f, 1f, 1f, 0.50f * best * mask);
        });
    }

    /// <summary>Lacquered carapace body (replaces Cell for the Chitin material): a hard dome
    /// (curve exponent 1.6 — a stiffer shoulder than the gemstone cut) with a tight dark rim.
    /// Grayscale; tinted per piece via modulate.</summary>
    public static ImageTexture CellChitin(int px)
    {
        px = Math.Clamp(px, 8, 128);
        string key = $"chitin:{px}";
        float radius = px * 0.10f;
        var half = new Vector2(px / 2f, px / 2f);
        return Bake(key, px, px, (x, y) =>
        {
            var p = new Vector2(x + 0.5f, y + 0.5f) - half;
            float sdf = RoundedBoxSdf(p, half, radius);
            float body = Feather(sdf, 1.2f);
            if (body <= 0f) return new Color(0, 0, 0, 0);
            // Dome driven by the rounded-box SDF, not by a Chebyshev radius: max(|x|,|y|)
            // switches axis along the diagonals, which bakes a bright X across every cell.
            float inside = Mathf.Clamp(-sdf / (px * 0.44f), 0f, 1f);
            float dome = Mathf.Pow(inside, 1.6f);                  // hard shoulder, stiffer than a gem
            float v = 0.50f + 0.48f * dome;
            v *= Mathf.Lerp(1.06f, 0.90f, (y + 0.5f) / px);        // lit from above
            float dEdge = -sdf;
            if (dEdge < 1.6f) v *= Mathf.Lerp(0.64f, 1f, dEdge / 1.6f);
            v = Mathf.Clamp(v, 0f, 1f);
            return new Color(v, v, v, body);
        });
    }

    /// <summary>Carapace facets for the Chitin finish: a 3×3 hexagonal seam grid (dark,
    /// alpha-only, so it survives any tint) plus one tight upper-left specular hotspot. The
    /// hex lattice is a natural/geometric pattern in the public domain — deliberately NOT a
    /// species marking.</summary>
    public static ImageTexture CellFacet(int px)
    {
        px = Math.Clamp(px, 8, 128);
        string key = $"facet:{px}";
        float radius = px * 0.10f;
        var half = new Vector2(px / 2f, px / 2f);
        float step = Mathf.Max(3f, px * 0.329f);                   // ≈3 hexes across the cell
        float lineW = 1.2f / step;                                 // seam width in lattice units
        float sig = px * 0.10f;
        return Bake(key, px, px, (x, y) =>
        {
            var p = new Vector2(x + 0.5f, y + 0.5f) - half;
            float mask = Feather(RoundedBoxSdf(p, half, radius), 1.2f);
            if (mask <= 0f) return new Color(0, 0, 0, 0);

            // Hex lattice: nearest of two half-offset rectangular lattices, then the hexagonal
            // distance (0 at a cell centre, 0.5 at an edge).
            var uv = new Vector2((x + 0.5f) / step, (y + 0.5f) / step);
            var r = new Vector2(1f, 1.7320508f);
            var hh = r * 0.5f;
            var a = new Vector2(Mathf.PosMod(uv.X, r.X), Mathf.PosMod(uv.Y, r.Y)) - hh;
            var b = new Vector2(Mathf.PosMod(uv.X - hh.X, r.X), Mathf.PosMod(uv.Y - hh.Y, r.Y)) - hh;
            var gv = a.LengthSquared() < b.LengthSquared() ? a : b;
            var ag = new Vector2(Mathf.Abs(gv.X), Mathf.Abs(gv.Y));
            float hd = Mathf.Max(ag.Dot(new Vector2(0.5f, 0.8660254f)), ag.X);
            float seam = Mathf.Clamp((hd - (0.5f - lineW)) / Mathf.Max(0.0001f, lineW), 0f, 1f);
            seam = seam * seam * (3f - 2f * seam);

            float gx = x + 0.5f - 0.34f * px, gy = y + 0.5f - 0.28f * px;
            float hot = Mathf.Exp(-(gx * gx + gy * gy) / (2f * sig * sig));

            // Alphas are set against the 0.40 OverlayA this material draws at: at the nominal
            // 0.18 the seams land at 0.07 on screen and the lattice is simply not there.
            var col = Over(new Color(0f, 0f, 0f, 0.45f * seam), new Color(1f, 1f, 1f, 0.80f * hot));
            if (col.A <= 0.002f) return new Color(0, 0, 0, 0);
            return new Color(col.R, col.G, col.B, col.A * mask);
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

    // ---- Debris atlas (the line-clear "physical destruction" pass) ----------
    // Fifteen silhouettes, baked once at 40px and drawn rotated+tinted. Grayscale RGB is a
    // BEVEL luminance ramp (lit from up-left) — so a chunk still reads as a broken solid
    // when the hue is stripped (grayscale/colorblind check), and the shape channel carries
    // the "this shattered" information rather than colour.
    //
    // The atlas is APPEND-ONLY by convention: a shipped skin's debris must not change shape
    // under an owner, so a new material takes new indices rather than re-cutting an old one.

    // Deterministic wedge radii — a constant table, so a variant always bakes identically.
    private static readonly float[] ShardWedgeR =
    {
        0.44f, 0.33f, 0.47f, 0.36f,   // variant 0
        0.39f, 0.46f, 0.31f, 0.43f,   // variant 1
        0.48f, 0.34f, 0.41f, 0.37f,   // variant 2
        0.35f, 0.45f, 0.40f, 0.30f,   // variant 3
    };

    private static readonly Vector2 ShardLight = new Vector2(-0.55f, -0.83f).Normalized();

    /// <summary>Deterministic hex radii for the soft fur tuft (variant 10) — a constant table
    /// for the same reason <see cref="ShardWedgeR"/> is one: a variant must bake identically
    /// every run, so the shape channel cannot drift between sessions.</summary>
    private static readonly float[] ShardTuftR = { 0.42f, 0.33f, 0.39f, 0.30f, 0.40f, 0.35f };

    /// <summary>Deterministic radii for the second fur silhouette (variant 12) — a WISP, i.e.
    /// a flat lens of guard hairs rather than the round clump of variant 10. Squashed on the
    /// short axis at bake time, so under random rotation the two fur shapes never read as the
    /// same stamp twice. Exists because Pelt used to borrow the scale crescent for its second
    /// shape: a coat that sheds must never throw a hard plate.</summary>
    private static readonly float[] ShardWispR = { 0.40f, 0.22f, 0.29f, 0.37f, 0.21f, 0.31f, 0.26f };

    /// <summary>
    /// Carapace scute (variant 13): a broken shell PLATE — a domed outer rim on one side, a
    /// straight fracture chord on the other. Convex (so <see cref="PolySdf"/> stays exact) and
    /// deliberately chunkier than the generic wedges: a shell breaks into plates, not gravel.
    /// </summary>
    private static readonly Vector2[] ShardScute =
    {
        new(-0.46f,  0.06f), new(-0.34f, -0.24f), new(-0.06f, -0.38f), new(0.24f, -0.30f),
        new( 0.45f, -0.05f), new( 0.38f,  0.20f), new(-0.18f,  0.26f),
    };

    /// <summary>Carapace shell shard (variant 14): the scute's thin sibling — a ~3:1 sliver
    /// snapped off a rim. Same growth-ridge treatment, different proportion, so a Chitin burst
    /// throws plates AND slivers instead of one repeated stamp.</summary>
    private static readonly Vector2[] ShardShell =
    {
        new(-0.47f, -0.02f), new(-0.12f, -0.20f), new(0.30f, -0.16f),
        new( 0.47f,  0.04f), new( 0.02f,  0.16f),
    };

    /// <summary>
    /// One debris silhouette. <paramref name="variant"/> 0–3 = blobby wedges (gel/metal/gem
    /// chunks), 4–7 = splinters (wood/frost), 8–9 = ring arcs (NeonTube glass), 10 = a soft fur
    /// tuft and 12 = a fur wisp (Pelt — blurred edges, it does not shatter), 11 = a scale
    /// crescent (Scale ONLY), 13–14 = carapace plates, ridged and hard-edged (Chitin ONLY).
    /// Baked at px=40; drawn white-modulated so it takes any piece hue.
    /// </summary>
    public static ImageTexture CellShard(int px, int variant)
    {
        px = Math.Clamp(px, 8, 64);
        variant = ((variant % 15) + 15) % 15;
        string key = $"shard:{px}:{variant}";
        var half = new Vector2(px / 2f, px / 2f);

        Vector2[]? poly = null;
        if (variant <= 3)
        {
            poly = new Vector2[4];
            for (int i = 0; i < 4; i++)
            {
                float ang = variant * 0.7f + i * Mathf.Tau / 4f;
                float r = ShardWedgeR[variant * 4 + i];
                poly[i] = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
            }
        }
        else if (variant <= 7)
        {
            // Splinter: 2.4:1 long axis, rotated per variant.
            float ang = (variant - 4) * 0.9f;
            float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
            Vector2 R(float lx, float ly) => new(lx * ca - ly * sa, lx * sa + ly * ca);
            poly = new[] { R(0.46f, 0f), R(-0.30f, 0.155f), R(-0.30f, -0.155f) };
        }
        else if (variant == 10)
        {
            // Fur tuft: a jittered hexagon, feathered wide below so it reads as a soft clump
            // rather than a broken solid. Fur sheds; it does not fracture.
            poly = new Vector2[6];
            for (int i = 0; i < 6; i++)
            {
                float ang = i * Mathf.Tau / 6f + 0.3f;
                poly[i] = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * ShardTuftR[i];
            }
        }
        else if (variant == 12)
        {
            // Fur wisp: the same soft treatment squashed to a lens, so the clump and the wisp
            // stay distinguishable after the random draw-time rotation.
            poly = new Vector2[7];
            for (int i = 0; i < 7; i++)
            {
                float ang = i * Mathf.Tau / 7f + 0.42f;
                poly[i] = new Vector2(Mathf.Cos(ang) * 1.18f, Mathf.Sin(ang) * 0.70f) * ShardWispR[i];
            }
        }
        else if (variant == 13) poly = ShardScute;
        else if (variant == 14) poly = ShardShell;

        float arcSpan = variant switch
        {
            8 => Mathf.DegToRad(110f) * 0.5f,
            11 => Mathf.DegToRad(90f) * 0.5f,     // a short, deep crescent — one scale
            _ => Mathf.DegToRad(150f) * 0.5f,
        };
        float featherPx = variant switch { 10 => 3.0f, 12 => 3.4f, _ => 1.5f };
        // Carapace plates carry concentric GROWTH RIDGES. The iso-contours of a convex polygon
        // SDF are inset outlines, so banding on the SDF gives ridges that follow the broken
        // rim for free — a second shape-channel cue (survives grayscale) that the generic
        // wedges do not have, which is what separates the paid shell from the free gem.
        bool ridged = variant is 13 or 14;

        Vector2 centre = Vector2.Zero;
        if (poly is not null) { foreach (var v in poly) centre += v; centre /= poly.Length; }

        return Bake(key, px, px, (x, y) =>
        {
            var p = (new Vector2(x + 0.5f, y + 0.5f) - half) / px;   // unit space, |p| <= 0.5
            float sdf; Vector2 n;
            if (poly is not null) PolySdf(p, poly, centre, out sdf, out n);
            else ArcSdf(p, arcSpan, out sdf, out n);

            float a = Feather(sdf * px, featherPx);
            if (a <= 0f) return new Color(0, 0, 0, 0);
            float lum = 0.52f + 0.48f * Mathf.Clamp(n.Dot(ShardLight), 0f, 1f);
            if (ridged) lum *= 0.82f + 0.18f * Mathf.Cos(sdf * px * 0.9f);
            float u = (p.X + p.Y + 1f) * 0.5f;                       // 0 at top-left corner
            if (u < 0.22f) lum = 1f;                                 // gloss band
            return new Color(lum, lum, lum, a);
        });
    }

    /// <summary>Max-of-half-planes SDF for a convex polygon, plus the outward normal of the
    /// dominant edge (drives the bevel ramp). Exact inside; a slight underestimate outside
    /// corners, which only softens the 1.5px feather.</summary>
    private static void PolySdf(Vector2 p, Vector2[] v, Vector2 centre, out float sdf, out Vector2 n)
    {
        sdf = float.NegativeInfinity; n = new Vector2(0, -1);
        for (int i = 0; i < v.Length; i++)
        {
            var a = v[i]; var b = v[(i + 1) % v.Length];
            var e = b - a;
            if (e.LengthSquared() < 1e-9f) continue;
            var nn = new Vector2(e.Y, -e.X).Normalized();
            if (nn.Dot(a - centre) < 0f) nn = -nn;                   // force outward
            float d = (p - a).Dot(nn);
            if (d > sdf) { sdf = d; n = nn; }
        }
    }

    /// <summary>Ring-arc SDF (inner 0.62 / outer 0.94 of the half-extent), centred on -Y.</summary>
    private static void ArcSdf(Vector2 p, float halfSpan, out float sdf, out Vector2 n)
    {
        const float rIn = 0.62f * 0.5f, rOut = 0.94f * 0.5f;
        float rMid = (rIn + rOut) * 0.5f, rHalf = (rOut - rIn) * 0.5f;
        float d = p.Length();
        if (d < 1e-5f) { sdf = 1f; n = new Vector2(0, -1); return; }
        float radial = Mathf.Abs(d - rMid) - rHalf;
        float ang = Mathf.Atan2(p.Y, p.X) + Mathf.Pi / 2f;           // 0 = straight up
        ang = Mathf.Wrap(ang, -Mathf.Pi, Mathf.Pi);
        float angular = (Mathf.Abs(ang) - halfSpan) * d;
        if (angular > radial) { sdf = angular; n = new Vector2(-p.Y, p.X).Normalized() * Mathf.Sign(ang); }
        else { sdf = radial; n = p / d * Mathf.Sign(d - rMid); }
    }

    /// <summary>
    /// Anisotropic bloom kernel: a tight core, a wide skirt, and <paramref name="points"/>
    /// diffraction streaks — one quad replaces the "glow disc + polygon spikes" pair, so a
    /// particle gets a lens-flare read for the same single draw call. White; tinted at draw.
    /// </summary>
    public static ImageTexture BloomStar(int px, int points)
    {
        px = Math.Clamp(px, 16, 256);
        points = Math.Max(2, points);
        string key = $"bloomstar:{px}:{points}";
        var half = new Vector2(px / 2f, px / 2f);
        int axes = Math.Max(1, points / 2);
        return Bake(key, px, px, (x, y) =>
        {
            var p = (new Vector2(x + 0.5f, y + 0.5f) - half) / (px * 0.5f);
            float r = p.Length();
            float core = 0.70f * Mathf.Exp(-(r / 0.10f) * (r / 0.10f));
            float skirt = 0.30f * Mathf.Exp(-(r / 0.42f) * (r / 0.42f));
            float streak = 0f;
            for (int k = 0; k < axes; k++)
            {
                float a = k * Mathf.Pi / axes;
                float dperp = Mathf.Abs(-p.X * Mathf.Sin(a) + p.Y * Mathf.Cos(a));
                streak += Mathf.Exp(-(dperp / 0.020f) * (dperp / 0.020f));
            }
            streak *= Mathf.Exp(-r / 0.50f) * 0.55f;
            float alpha = Mathf.Clamp(core + skirt + streak, 0f, 1f);
            return new Color(1f, 1f, 1f, alpha);
        });
    }

    /// <summary>
    /// A baked shockwave annulus (sharp inside, soft outside) with the R/G/B channels sampled
    /// at 0.97/1.00/1.03 of the ring radius, so the wave carries chromatic dispersion for free.
    /// Replaces a 48-segment DrawArc with one textured quad.
    /// </summary>
    public static ImageTexture ShockRing(int px = 128)
    {
        px = Math.Clamp(px, 32, 256);
        string key = $"shockring:{px}";
        var half = new Vector2(px / 2f, px / 2f);

        static float Prof(float rn)
        {
            if (rn <= ShockRingCrest) return Mathf.Clamp(1f - (ShockRingCrest - rn) / 0.06f, 0f, 1f); // sharp inner wall
            float t = (rn - ShockRingCrest) / 0.10f;
            return Mathf.Exp(-t * t);                                            // soft outer bleed
        }

        return Bake(key, px, px, (x, y) =>
        {
            float rn = ((new Vector2(x + 0.5f, y + 0.5f) - half) / (px * 0.5f)).Length();
            float r = Prof(rn / 0.97f), g = Prof(rn), b = Prof(rn / 1.03f);
            float a = Mathf.Max(r, Mathf.Max(g, b));
            if (a <= 0.002f) return new Color(0, 0, 0, 0);
            return new Color(r / a, g / a, b / a, a);
        });
    }

    /// <summary>Normalized radius (0..1 of the half-extent) at which <see cref="ShockRing"/>
    /// puts its crest — callers size the quad by this so the ring lands on the wanted radius.</summary>
    public const float ShockRingCrest = 0.86f;
}
