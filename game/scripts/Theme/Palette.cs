using Godot;
using Blockfall.Core;

namespace Blockfall.Theme;

/// <summary>
/// Central design tokens for the "NEON FLUX" look: colors, text hierarchy,
/// glass-surface recipe, radii, and the neon piece palette (with an overbright
/// emissive variant per piece that seeds the HDR-2D bloom). One place to retune
/// the whole game. Colors are chosen for high contrast on the dark background
/// and to read distinctly for color-blind players.
/// </summary>
public static class Palette
{
    // ---- Background -------------------------------------------------------
    // Deep navy-violet; the animated shader interpolates Background -> BgBottom.
    // FIXED: equipping a cosmetic theme no longer retints these — a block-skin
    // change must never recolor the game screen, so the backdrop stays constant
    // (piece contrast reads identically under every skin). See ApplyTheme.
    public static Color Background { get; private set; } = DefaultBgTop;
    public static Color BgBottom { get; private set; } = DefaultBgBottom;
    private static readonly Color DefaultBgTop = new(0.039f, 0.043f, 0.078f);    // #0A0B14
    private static readonly Color DefaultBgBottom = new(0.086f, 0.094f, 0.180f); // #16182E
    public static readonly Color PanelBg = new(0.078f, 0.086f, 0.133f, 0.85f);

    // ---- Text hierarchy (fixed levels — never eyeball per screen) ---------
    public static readonly Color TextPrimary = new(0.93f, 0.95f, 1f);
    public static readonly Color TextSecondary = new(0.62f, 0.66f, 0.80f);
    public static readonly Color TextTertiary = new(0.42f, 0.45f, 0.58f);
    /// <summary>Legacy alias — same level as <see cref="TextSecondary"/>.</summary>
    public static readonly Color TextDim = new(0.62f, 0.66f, 0.80f);
    /// <summary>Dark ink for text on accent-filled (primary) buttons.</summary>
    public static readonly Color InkOnAccent = new(0.03f, 0.05f, 0.10f);

    // ---- Accents. Usage rules:
    //   Cyan   = the UI accent + all standard solo modes.
    //   Violet = versus identity + spin flourishes.
    //   Gold   = daily challenge + new-best + perfect clear. Nowhere else.
    //   Red    = danger / incoming garbage only.
    //   Green  = success ticks (finesse clean) only.
    public static readonly Color Accent = new(0.20f, 0.85f, 1f);        // cyan
    public static readonly Color AccentViolet = new(0.70f, 0.42f, 1f);
    public static readonly Color AccentGold = new(1f, 0.85f, 0.32f);
    public static readonly Color AccentRed = new(1f, 0.36f, 0.46f);
    public static readonly Color AccentGreen = new(0.36f, 0.95f, 0.56f);

    // ---- Glass surface recipe (fake glassmorphism — no backdrop blur).
    // Fills are tinted toward the bg hue so panels stay violet-navy, not gray.
    public static readonly Color GlassTop = new(0.72f, 0.76f, 1f, 0.085f);    // gradient top
    public static readonly Color GlassBottom = new(0.60f, 0.64f, 0.95f, 0.03f); // gradient bottom
    public static readonly Color GlassBorder = new(1f, 1f, 1f, 0.10f);
    public static readonly Color GlassHighlight = new(1f, 1f, 1f, 0.16f);     // 1px inner top edge

    // ---- Radii (fixed scale) ----------------------------------------------
    public const int RadiusS = 8;
    public const int RadiusM = 14;
    public const int RadiusL = 20;

    // ---- Board chrome ------------------------------------------------------
    public static readonly Color GridLine = new(1f, 1f, 1f, 0.04f);
    public static readonly Color GridBorder = new(0.35f, 0.55f, 0.95f, 0.5f);

    /// <summary>
    /// When true, pieces use the Okabe–Ito colorblind-safe palette (maximally
    /// distinct for deuteranopia/protanopia). Toggled from the settings screen and
    /// restored on launch from <c>GameSettings.ColorblindMode</c>.
    /// </summary>
    public static bool ColorblindMode { get; set; }

    /// <summary>The glyph the equipped skin stamps on each block (None = plain). Set by
    /// <see cref="ApplyTheme"/>; read by the Block Fit renderer per draw.</summary>
    public static SkinGlyph EquippedGlyph { get; private set; } = SkinGlyph.None;

    /// <summary>The equipped skin's block finish (Gel = the default wet-candy look). Read
    /// per draw by <see cref="BlockRender"/>; orthogonal to hue so colorblind fills survive.</summary>
    public static CellMaterial EquippedMaterial { get; private set; } = CellMaterial.Gel;

    /// <summary>Optional iridescent rim colour for the equipped skin (alpha 0 = no rim).</summary>
    public static Color EquippedEdgeTint { get; private set; } = new(0, 0, 0, 0);

    /// <summary>The equipped skin's Block Fit colour plan. Read by <see cref="FitFill"/> only —
    /// the falling game always uses the 7-hue <see cref="ForPiece"/> path because there the
    /// piece hue is information (next/hold queues).</summary>
    public static ColorPlan EquippedPlan { get; private set; } = ColorPlan.Rainbow7;

    // Neon-pastel piece fills (the default "NEON FLUX" theme): hue identities
    // preserved, saturation/luminance pulled into one band so the set reads as a
    // family on the navy background. Mutable — retinted by cosmetic themes.
    private static Color I = new(0.36f, 0.93f, 1.00f); // cyan
    private static Color O = new(1.00f, 0.85f, 0.38f); // amber
    private static Color T = new(0.78f, 0.48f, 1.00f); // violet
    private static Color S = new(0.45f, 0.96f, 0.62f); // mint
    private static Color Z = new(1.00f, 0.45f, 0.55f); // coral
    private static Color J = new(0.45f, 0.62f, 1.00f); // azure
    private static Color L = new(1.00f, 0.64f, 0.36f); // tangerine
    private static readonly Color Garbage = new(0.42f, 0.45f, 0.55f);

    /// <summary>The default piece/backdrop colors as a theme (the free catalog entry).</summary>
    public static BlockTheme DefaultTheme => new(
        "theme_neon_flux",
        new Color(0.36f, 0.93f, 1.00f), new Color(1.00f, 0.85f, 0.38f), new Color(0.78f, 0.48f, 1.00f),
        new Color(0.45f, 0.96f, 0.62f), new Color(1.00f, 0.45f, 0.55f), new Color(0.45f, 0.62f, 1.00f),
        new Color(1.00f, 0.64f, 0.36f), DefaultBgTop, DefaultBgBottom);

    /// <summary>
    /// Equip a cosmetic theme (null = back to the default). Only the piece colors,
    /// glyph, material and edge tint retint — the screen background is deliberately
    /// LEFT UNCHANGED so switching block skins never shifts the play-field backdrop
    /// (piece contrast/legibility stays constant). Views read ForPiece per draw, so
    /// boards update on the next frame with no explicit backdrop refresh.
    /// </summary>
    public static void ApplyTheme(BlockTheme? t)
    {
        t ??= DefaultTheme;
        I = t.I; O = t.O; T = t.T; S = t.S; Z = t.Z; J = t.J; L = t.L;
        // Background intentionally NOT retinted — a block-skin change must not recolor
        // the game screen. BlockTheme.BgTop/BgBottom stay on the record (data compat)
        // but no longer drive the live backdrop.
        EquippedGlyph = t.Glyph;
        EquippedMaterial = t.Material;
        EquippedEdgeTint = t.EdgeTint;
        EquippedPlan = t.Plan;
    }

    // ---- Block Fit colour plans -------------------------------------------
    // A plan folds the 7 piece hues onto fewer colours (or onto a board-space gradient).
    // Legal ONLY on the Block Fit board, where a cell's hue carries no rule information.
    // Resolution is split in two: PlanSlot is a pure rule table (which piece slots to sample,
    // how to mix, how to scale luminance), and the two call sites supply the colours — the
    // live palette for gameplay, an arbitrary BlockTheme for a store preview. One rule table
    // ⇒ the shop can never show a plan the board renders differently.

    /// <summary>Board span the BoardGradient plan normalises against on the PLAY FIELD. Mirrors
    /// <c>BlockFitGame.Size</c> (10) — duplicated as a constant because Palette must not
    /// take a core dependency for a cosmetic ramp.</summary>
    public const int BoardSpan = 10;

    /// <summary>The diagonal range (0 … 2·(span−1)) a positional plan ramps across on a square
    /// field of <paramref name="span"/> cells. Callers pass THEIR OWN range: a 3×3 store card
    /// normalised against the 10-wide board denominator only ever reached mix 0.22, so the one
    /// gradient skin previewed as its first fifth and never showed the colour it is sold on.</summary>
    public static float DiagRange(int span) => 2f * (span - 1);

    private static readonly PieceType[] TrioAnchor = { PieceType.I, PieceType.T, PieceType.S };

    /// <summary>Pure plan rule: which piece slot(s) to sample, the A→B mix, and a luminance
    /// multiplier. Addressed by the board DIAGONAL (row+col) rather than (row,col), because
    /// that is the only positional term any plan needs and it is what every Block Fit draw
    /// site already passes as its shimmer phase — so the plan needs no new call signature.
    /// No colour access here, so gameplay and previews share one source of truth.
    /// <paramref name="diagRange"/> is the diagonal span the CALLER ramps across (see
    /// <see cref="DiagRange"/>) so one rule table serves a 10-wide board and a 3-wide card.</summary>
    private static (PieceType A, PieceType B, float Mix, float Lum) PlanSlot(ColorPlan plan, PieceType t, float diag, float diagRange)
        => plan switch
        {
            ColorPlan.Trio => (TrioAnchor[(int)t % 3], PieceType.I, 0f, 1f),
            ColorPlan.Duo => ((int)t % 2 == 0 ? PieceType.I : PieceType.Z, PieceType.I, 0f, 1f),
            // One hue, seven luminance rungs — the ink-wash read.
            ColorPlan.Mono => (PieceType.I, PieceType.I, 0f, 0.55f + 0.075f * Mathf.Clamp((int)t - 1, 0, 6)),
            ColorPlan.BoardGradient => (PieceType.I, PieceType.T,
                                        Mathf.Clamp(diag / Mathf.Max(1f, diagRange), 0f, 1f), 1f),
            _ => (t, t, 0f, 1f),
        };

    private static Color Scale(Color c, float k) => new(c.R * k, c.G * k, c.B * k, c.A);

    /// <summary>
    /// The Block Fit cell fill: the equipped skin's <see cref="ColorPlan"/> applied to the live
    /// palette. Colorblind mode short-circuits to <see cref="ForPiece"/> so the Okabe–Ito set is
    /// never folded away — a plan may cost identity, never accessibility.
    /// </summary>
    public static Color FitFill(PieceType type, int row, int col) => FitFill(type, row + col);

    /// <summary>Plan-resolved fill addressed by the board diagonal (row+col).</summary>
    public static Color FitFill(PieceType type, float diag)
    {
        if (ColorblindMode || EquippedPlan == ColorPlan.Rainbow7) return ForPiece(type);
        if (type is PieceType.Empty or PieceType.Garbage) return ForPiece(type);
        var (a, b, mix, lum) = PlanSlot(EquippedPlan, type, diag, DiagRange(BoardSpan));
        var col2 = mix <= 0f ? ForPiece(a) : ForPiece(a).Lerp(ForPiece(b), mix);
        return lum >= 0.999f ? col2 : Scale(col2, lum);
    }

    /// <summary>
    /// The SHOWCASE fill: the same plan rule table resolved against an arbitrary (not-equipped)
    /// theme's own colours — the store-preview path, which must never disturb the live palette.
    ///
    /// Deliberately does NOT fold to <see cref="ColorblindMode"/>, and that asymmetry is the point.
    /// The accessibility contract binds the PLAY FIELD, where a cell must tell apart from its
    /// neighbour: there <see cref="FitFill"/>/<see cref="ForPiece"/> still hard-override every skin
    /// to Okabe–Ito. A shop card is a product photo, not a play surface. Folding it too made all 31
    /// skins render the identical seven swatches, so a colourblind collector had nothing to look at
    /// when deciding what US$2.99 buys — the showcase stopped showing the goods. Board stays
    /// colour-safe, showcase shows the product; <c>StoreScreen</c> states the split in a standing
    /// note rather than leaving the player to discover it after paying.
    ///
    /// <paramref name="diagRange"/>: the caller's own diagonal span for positional plans — a 3×3
    /// card must pass ITS range, not the board's, or the ramp clips to its first fifth.
    /// </summary>
    public static Color PlanFill(BlockTheme theme, PieceType type, int row, int col, float diagRange)
    {
        // Twin of the FitFill guard: structural cells are never a plan slot. Without this the
        // fold would sample garbage/empty through the piece table and paint holes solid.
        if (type is PieceType.Empty or PieceType.Garbage) return OfTheme(theme, type);
        if (theme.Plan == ColorPlan.Rainbow7) return OfTheme(theme, type);
        var (a, b, mix, lum) = PlanSlot(theme.Plan, type, row + col, diagRange);
        var col2 = mix <= 0f ? OfTheme(theme, a) : OfTheme(theme, a).Lerp(OfTheme(theme, b), mix);
        return lum >= 0.999f ? col2 : Scale(col2, lum);
    }

    private static Color OfTheme(BlockTheme t, PieceType type) => type switch
    {
        PieceType.I => t.I,
        PieceType.O => t.O,
        PieceType.T => t.T,
        PieceType.S => t.S,
        PieceType.Z => t.Z,
        PieceType.J => t.J,
        PieceType.L => t.L,
        PieceType.Garbage => Garbage,
        _ => new Color(0, 0, 0, 0),
    };

    // Colorblind-safe fills (Okabe–Ito). Kept bright so the neon glow still reads.
    private static readonly Color CbI = new(0.35f, 0.70f, 0.90f); // sky blue
    private static readonly Color CbO = new(0.95f, 0.90f, 0.25f); // yellow
    private static readonly Color CbT = new(0.80f, 0.60f, 0.70f); // reddish purple
    private static readonly Color CbS = new(0.00f, 0.62f, 0.50f); // bluish green
    private static readonly Color CbZ = new(0.84f, 0.37f, 0.00f); // vermillion
    private static readonly Color CbJ = new(0.00f, 0.45f, 0.70f); // blue
    private static readonly Color CbL = new(0.90f, 0.60f, 0.00f); // orange

    public static Color ForPiece(PieceType type) => ColorblindMode
        ? type switch
        {
            PieceType.I => CbI,
            PieceType.O => CbO,
            PieceType.T => CbT,
            PieceType.S => CbS,
            PieceType.Z => CbZ,
            PieceType.J => CbJ,
            PieceType.L => CbL,
            PieceType.Garbage => Garbage,
            _ => new Color(0, 0, 0, 0),
        }
        : type switch
        {
            PieceType.I => I,
            PieceType.O => O,
            PieceType.T => T,
            PieceType.S => S,
            PieceType.Z => Z,
            PieceType.J => J,
            PieceType.L => L,
            PieceType.Garbage => Garbage,
            _ => new Color(0, 0, 0, 0),
        };

    /// <summary>
    /// Overbright bloom-seed variant of a piece color. Values above 1.0 survive
    /// only with HDR 2D on; without it this clamps back to the base neon, so the
    /// no-bloom fallback still looks intentional. Warm hues get a smaller boost
    /// so they don't clip toward white and lose their identity.
    /// </summary>
    public static Color Emissive(PieceType type) => EmissiveFor(type, ForPiece(type));

    /// <summary>The same per-type bloom boost applied to an ALREADY-RESOLVED fill — needed
    /// once a <see cref="ColorPlan"/> can change a piece's hue out from under
    /// <see cref="ForPiece"/> (garbage still never glows).</summary>
    public static Color EmissiveFor(PieceType type, Color fill)
    {
        float boost = type switch
        {
            PieceType.O or PieceType.L or PieceType.Z => 1.9f, // warm — clip earlier
            PieceType.Garbage => 1.0f,                          // garbage never glows
            _ => 2.3f,
        };
        return new Color(fill.R * boost, fill.G * boost, fill.B * boost, fill.A);
    }

    /// <summary>Overbright variant of an arbitrary accent for bloom-seeded UI moments.</summary>
    public static Color Emissive(Color c, float boost = 2.0f)
        => new(c.R * boost, c.G * boost, c.B * boost, c.A);

    /// <summary>A dimmer, translucent variant for the ghost/landing preview.</summary>
    public static Color Ghost(PieceType type)
    {
        var c = ForPiece(type);
        return new Color(c.R, c.G, c.B, 0.22f);
    }

    /// <summary>Brighter inner highlight used to fake a glossy neon bevel.</summary>
    public static Color Highlight(PieceType type)
    {
        var c = ForPiece(type);
        return new Color(
            Mathf.Min(1f, c.R + 0.25f),
            Mathf.Min(1f, c.G + 0.25f),
            Mathf.Min(1f, c.B + 0.25f),
            0.9f);
    }

    /// <summary>The identity accent for a game mode's card (rule: gold = daily only).</summary>
    public static Color ModeAccent(GameModeId mode) => mode switch
    {
        GameModeId.Daily => AccentGold,
        _ => Accent,
    };
}
