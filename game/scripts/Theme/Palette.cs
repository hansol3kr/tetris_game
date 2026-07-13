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
    // Both retint with the equipped cosmetic theme (see ApplyTheme).
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
    /// Equip a cosmetic theme (null = back to the default). Piece colors and the
    /// backdrop gradient retint; views read ForPiece per draw so boards update on
    /// the next frame — only the animated background needs an explicit refresh
    /// (<c>Bootstrap.Instance.Bg.ApplyThemeColors()</c>).
    /// </summary>
    public static void ApplyTheme(BlockTheme? t)
    {
        t ??= DefaultTheme;
        I = t.I; O = t.O; T = t.T; S = t.S; Z = t.Z; J = t.J; L = t.L;
        Background = t.BgTop;
        BgBottom = t.BgBottom;
    }

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
    public static Color Emissive(PieceType type)
    {
        var c = ForPiece(type);
        float boost = type switch
        {
            PieceType.O or PieceType.L or PieceType.Z => 1.9f, // warm — clip earlier
            PieceType.Garbage => 1.0f,                          // garbage never glows
            _ => 2.3f,
        };
        return new Color(c.R * boost, c.G * boost, c.B * boost, c.A);
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
