using Godot;

namespace Blockfall.Theme;

/// <summary>
/// Loads and caches the two typeface families of the design system:
///   • Orbitron (variable weight) — logo, headings, big numerals, juice popups. ONLY those.
///   • Rajdhani — every other UI string (buttons, labels, stats), always uppercase.
/// Each variation keeps the engine's original fallback font in its chain so
/// symbol glyphs (★ ⏱ ▲ ◀ …) and any non-Latin text still render.
/// Degrades gracefully to the engine font if the assets are missing.
/// </summary>
public static class Fonts
{
    public static Font Display { get; private set; } = null!;      // Orbitron 800
    public static Font DisplayMedium { get; private set; } = null!; // Orbitron 600 (HUD numerals)
    public static Font Ui { get; private set; } = null!;            // Rajdhani SemiBold
    public static Font UiBold { get; private set; } = null!;        // Rajdhani Bold
    public static Font UiTracked { get; private set; } = null!;     // Rajdhani SemiBold, letterspaced (section labels)

    public static bool Loaded { get; private set; }

    /// <summary>Call once from Bootstrap before any UI is built.</summary>
    public static void Init()
    {
        var engineFallback = ThemeDB.FallbackFont;

        var orbitron = TryLoad("res://assets/fonts/Orbitron.ttf");
        var rajSemi = TryLoad("res://assets/fonts/Rajdhani-SemiBold.ttf");
        var rajBold = TryLoad("res://assets/fonts/Rajdhani-Bold.ttf");
        Loaded = orbitron is not null && rajSemi is not null;

        Display = Variation(orbitron, engineFallback, weight: 800);
        DisplayMedium = Variation(orbitron, engineFallback, weight: 600);
        Ui = Variation(rajSemi, engineFallback);
        UiBold = Variation(rajBold ?? rajSemi, engineFallback);
        UiTracked = Variation(rajSemi, engineFallback, glyphSpacing: 2);

        // Anything that still draws with the engine default (e.g. a missed
        // control) picks up the UI face instead of the stock font.
        if (rajSemi is not null)
            ThemeDB.FallbackFont = Ui;
    }

    private static FontFile? TryLoad(string path)
    {
        if (!ResourceLoader.Exists(path)) return null;
        return ResourceLoader.Load<FontFile>(path);
    }

    private static Font Variation(FontFile? baseFont, Font engineFallback, int weight = 0, int glyphSpacing = 0)
    {
        if (baseFont is null) return engineFallback;
        var v = new FontVariation { BaseFont = baseFont };
        if (weight > 0)
            v.VariationOpentype = new Godot.Collections.Dictionary { { "wght", weight } };
        if (glyphSpacing != 0)
            v.SetSpacing(TextServer.SpacingType.Glyph, glyphSpacing);
        // Keep the engine font for glyphs the family lacks (symbols, CJK).
        v.Fallbacks = new Godot.Collections.Array<Font> { engineFallback };
        return v;
    }
}
