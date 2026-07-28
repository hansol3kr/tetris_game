using Godot;
using Blockfall.Core;

namespace Blockfall.Theme;

/// <summary>
/// The single shared renderer for one Block Fit gem cell — the anti-drift seam that
/// Block Fit, the CPU-versus board, and the store previews all draw through, so a
/// skin's material can never look different in the shop than in play.
///
/// A cell is up to five stacked layers, drawn in order (mirrors the solo
/// <c>BoardView.DrawCell</c> stack, then adds a per-<see cref="CellMaterial"/> finish):
///   1. GLOW    — a soft emissive bloom seed under the cell (bloom without HDR)
///   2. BODY    — the tinted gel/frost/gem tile
///   3. OVERLAY — the finish (brushed streaks, holo iridescence, frost sheen, star specks)
///   4. GLOSS   — the white wet-sheen specular (stays white on any hue)
///   5. GLYPH   — the embossed skin stamp
///
/// Everything is asset-free (baked textures + immediate-mode) and reads
/// <see cref="Palette.ForPiece"/>, so the accessibility colorblind palette overrides
/// hue for free. Size guards shed the expensive layers on tiny tray cells.
/// </summary>
public static class BlockRender
{
    /// <summary>Per-material layer strengths. Gel reproduces the locked BoardView look.</summary>
    private readonly struct MatSpec
    {
        public readonly float GlowA, GlossA, OverlayA;
        public MatSpec(float glow, float gloss, float overlay) { GlowA = glow; GlossA = gloss; OverlayA = overlay; }
    }

    private static MatSpec Spec(CellMaterial m) => m switch
    {
        CellMaterial.Pearl       => new MatSpec(0.22f, 0.62f, 0f),
        CellMaterial.Metallic    => new MatSpec(0.14f, 0.30f, 0.90f),
        CellMaterial.Holographic => new MatSpec(0.20f, 0.40f, 0.34f),
        CellMaterial.Frosted     => new MatSpec(0.16f, 0.14f, 0.16f),
        CellMaterial.Gemstone    => new MatSpec(0.30f, 0.30f, 0f),
        CellMaterial.Starfield   => new MatSpec(0.28f, 0.44f, 0f),
        // Wood: almost no bloom, no specular — matte timber is the whole point.
        CellMaterial.Wood        => new MatSpec(0.06f, 0.18f, 0.85f),
        // NeonTube: hollow glass. Strong bloom, a little sheen, zero overlay (one draw).
        CellMaterial.NeonTube    => new MatSpec(0.46f, 0.30f, 0.00f),
        _                        => new MatSpec(0.32f, 0.50f, 0f), // Gel
    };

    /// <summary>The base body texture for a material (frosted/gem swap out the gel tile).</summary>
    private static ImageTexture BodyTex(int px, CellMaterial m) => m switch
    {
        CellMaterial.Frosted  => TextureFactory.CellFrost(px),
        CellMaterial.Gemstone => TextureFactory.CellGem(px),
        CellMaterial.Wood     => TextureFactory.CellWood(px),
        CellMaterial.NeonTube => TextureFactory.CellTube(px),
        _                     => TextureFactory.Cell(px),
    };

    /// <summary>Dark vs light glyph ink chosen by the piece fill's luminance, so a stamp
    /// reads on both bright neon and near-black (Obsidian) cells.</summary>
    public static Color GlyphInk(Color fill, float alpha)
    {
        float lum = 0.299f * fill.R + 0.587f * fill.G + 0.114f * fill.B;
        return lum > 0.45f
            ? new Color(0.05f, 0.06f, 0.10f, 0.82f * alpha)
            : new Color(0.96f, 0.97f, 1.00f, 0.90f * alpha);
    }

    /// <summary>
    /// Draw one filled cell with the full material stack, reading the equipped skin's colours
    /// from <see cref="Palette"/> (the BLOCK FIT path — respects the colorblind override).
    /// <paramref name="shimmer"/> is a monotonic time driving holo scroll / starfield twinkle /
    /// gloss breathe; <paramref name="cellPhase"/> (e.g. row+col) de-syncs neighbours so the
    /// board breathes as a wave. Pass shimmer unchanged under reduced motion (caller freezes it).
    ///
    /// The fill is resolved through <see cref="Palette.FitFill"/>, so the equipped skin's
    /// <see cref="ColorPlan"/> (Mono / Duo / Trio / BoardGradient) applies — and
    /// <paramref name="cellPhase"/> doubles as the board diagonal the gradient plan needs, which
    /// is why the plan needed no new call signature. This is legal ONLY because Block Fit
    /// attaches no rule meaning to a cell's hue; the falling game must keep using the explicit
    /// -colour overload below, since there the next/hold queues are read by colour.
    /// </summary>
    public static void DrawCell(CanvasItem ci, Rect2 rect, float cell, PieceType type, float alpha,
                                CellMaterial mat, SkinGlyph glyph, float shimmer, float cellPhase,
                                bool drawGlyph = true, bool reduced = false)
    {
        var fill = Palette.FitFill(type, cellPhase);
        DrawCell(ci, rect, cell, fill, Palette.EmissiveFor(type, fill), alpha, mat,
                 Palette.EquippedEdgeTint, glyph, shimmer, cellPhase, drawGlyph, reduced);
    }

    /// <summary>The material-stack core with EXPLICIT colours — lets the store previews render a
    /// not-yet-equipped theme's gems without touching the live global palette.</summary>
    public static void DrawCell(CanvasItem ci, Rect2 rect, float cell, Color color, Color emissive, float alpha,
                                CellMaterial mat, Color edge, SkinGlyph glyph, float shimmer, float cellPhase,
                                bool drawGlyph = true, bool reduced = false)
    {
        int px = Mathf.Clamp((int)cell, 8, 128);
        var spec = Spec(mat);
        var center = rect.GetCenter();
        float breathe = reduced ? 0.5f : 0.5f + 0.5f * Mathf.Sin(shimmer * 1.4f + cellPhase * 0.35f);

        // 1) GLOW — emissive bloom seed under the cell (skipped on small tray cells).
        if (spec.GlowA > 0.01f && cell >= 16f)
        {
            int gpx = Mathf.Clamp((int)(cell * 1.7f), 12, 192);
            float grow = cell * 0.22f;
            var glowRect = new Rect2(rect.Position - new Vector2(grow, grow), rect.Size + new Vector2(grow, grow) * 2f);
            float ga = spec.GlowA * alpha * (0.85f + 0.15f * breathe);
            ci.DrawTextureRect(TextureFactory.CellGlow(gpx), glowRect, false, new Color(emissive.R, emissive.G, emissive.B, ga));
        }

        // 2) BODY — the tinted tile. Metallic pre-darkens so its bright sheen band reads.
        float bodyMul = mat == CellMaterial.Metallic ? 0.86f : 1f;
        ci.DrawTextureRect(BodyTex(px, mat), rect, false,
                           new Color(color.R * bodyMul, color.G * bodyMul, color.B * bodyMul, color.A * alpha));

        // 3) OVERLAY — the finish.
        if (cell >= 14f)
        {
            switch (mat)
            {
                case CellMaterial.Metallic:
                    ci.DrawTextureRect(TextureFactory.CellBrushed(px), rect, false, new Color(1, 1, 1, spec.OverlayA * alpha));
                    break;
                case CellMaterial.Holographic:
                {
                    var holo = TextureFactory.HoloStrip(px);
                    float srcY = reduced ? px * 0.5f : Mathf.PosMod(shimmer * px * 0.15f + cellPhase * px * 0.07f, px);
                    var inner = rect.Grow(-cell * 0.12f);
                    ci.DrawTextureRectRegion(holo, inner, new Rect2(0, srcY, px, px), new Color(1, 1, 1, spec.OverlayA * alpha));
                    break;
                }
                case CellMaterial.Frosted:
                    ci.DrawTextureRect(TextureFactory.CellFrostSheen(px), rect, false, new Color(1, 1, 1, spec.OverlayA * alpha));
                    break;
                case CellMaterial.Starfield:
                    DrawStarfield(ci, rect, cell, shimmer, cellPhase, alpha, reduced);
                    break;
                case CellMaterial.Wood:
                    // Alpha-only dark grain: laid OVER the tint (not multiplied) so the rings
                    // read as timber on a pale oak and a deep walnut alike.
                    ci.DrawTextureRect(TextureFactory.CellWoodGrain(px), rect, false, new Color(1, 1, 1, spec.OverlayA * alpha));
                    break;
            }
        }

        // Iridescent rim (dark/holo skins declare an EdgeTint).
        if (edge.A > 0.01f && cell >= 16f)
        {
            float ring = 0.6f + 0.4f * breathe;
            ci.DrawTextureRect(TextureFactory.CellRim(px), rect, false, new Color(edge.R, edge.G, edge.B, edge.A * ring * alpha));
        }

        // 4) GLOSS — white wet sheen; breathes so the board isn't static.
        if (cell >= 12f && spec.GlossA > 0.01f)
        {
            float g = spec.GlossA * alpha * (0.85f + 0.15f * breathe);
            ci.DrawTextureRect(TextureFactory.CellGloss(px), rect, false, new Color(1, 1, 1, g));
        }

        // 5) GLYPH — embossed stamp (a shade smaller than the cell so the drop shadow stays in-cell).
        if (drawGlyph && glyph != SkinGlyph.None && cell >= 18f)
            GlyphArt.Draw(ci, glyph, center, cell * 0.60f, GlyphInk(color, alpha), withDetails: cell >= 26f);
    }

    // Deterministic star field per Starfield cell (unit pos, radius scale, hue tint index).
    private static readonly (Vector2 P, float R, int Hue)[] StarSpots =
    {
        (new(-0.30f, -0.24f), 1.15f, 0), (new(0.26f, 0.04f), 0.85f, 1), (new(0.04f, 0.30f), 1.0f, 2),
        (new(-0.14f, 0.18f), 0.6f, 0),   (new(0.32f, -0.30f), 0.7f, 1),
    };
    private static readonly Color[] StarTints =
    { new(1f, 1f, 1f), new(0.72f, 0.82f, 1f), new(0.85f, 0.72f, 1f) }; // white / blue / violet

    /// <summary>Nebula finish (Figma "Nebula" fill technique): a faint drifting colour cloud
    /// behind a small field of layered, twinkling, hue-varied stars — the brightest catches a
    /// cross-glint. Cheap: ~7 draws/cell, all immediate-mode.</summary>
    private static void DrawStarfield(CanvasItem ci, Rect2 rect, float cell, float shimmer, float cellPhase, float alpha, bool reduced)
    {
        var c = rect.GetCenter();
        float h = cell * 0.5f;

        // Soft nebula cloud — two offset translucent tints that breathe.
        float cloud = reduced ? 0.5f : 0.5f + 0.5f * Mathf.Sin(shimmer * 0.8f + cellPhase);
        ci.DrawCircle(c + new Vector2(-0.18f, 0.10f) * h, cell * 0.42f, new Color(0.45f, 0.35f, 0.85f, 0.10f * cloud * alpha));
        ci.DrawCircle(c + new Vector2(0.20f, -0.12f) * h, cell * 0.34f, new Color(0.30f, 0.55f, 0.95f, 0.09f * (1f - cloud) * alpha));

        for (int i = 0; i < StarSpots.Length; i++)
        {
            var (sp, rs, hue) = StarSpots[i];
            float ph = cellPhase * 1.7f + i * 2.1f;
            float tw = reduced ? 0.7f : 0.4f + 0.6f * Mathf.Sin(shimmer * 3.2f + ph);
            tw = Mathf.Clamp(tw, 0f, 1f);
            var tint = StarTints[hue];
            var pos = c + sp * h;
            float r = Mathf.Max(1f, cell * 0.045f) * rs * (0.6f + 0.4f * tw);
            ci.DrawCircle(pos, r, new Color(tint.R, tint.G, tint.B, 0.85f * tw * alpha));
            // Brightest star gets a diagonal cross-glint when it peaks.
            if (i == 0 && tw > 0.75f)
            {
                float g = r * 2.6f, ga = (tw - 0.75f) * 4f * alpha;
                ci.DrawLine(pos - new Vector2(g, 0), pos + new Vector2(g, 0), new Color(1, 1, 1, 0.5f * ga), 1f);
                ci.DrawLine(pos - new Vector2(0, g), pos + new Vector2(0, g), new Color(1, 1, 1, 0.5f * ga), 1f);
            }
        }
    }
}
