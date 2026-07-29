using Godot;

namespace Blockfall.Theme;

/// <summary>
/// The physical "finish" a skin gives its blocks — orthogonal to hue, so two skins
/// with the same palette can feel like different objects. Modulates luminance/specular
/// only (never the piece hue), so the Okabe–Ito colorblind fills survive every finish.
/// Append-only: values are persisted implicitly via the equipped <see cref="BlockTheme"/>.
/// </summary>
public enum CellMaterial { Gel, Pearl, Metallic, Holographic, Frosted, Gemstone, Starfield, Wood, NeonTube, Pelt, Scale, Chitin }

/// <summary>
/// How many colours a skin actually spends on the Block Fit board. The 7-hue rainbow is the
/// only plan the falling game may use (there, hue is INFORMATION — next/hold queues read by
/// colour), so this axis exists purely for Block Fit, where a piece's colour carries nothing.
/// It is what makes two skins tell apart at arm's length instead of being hue permutations.
/// Colorblind mode overrides every plan back to Okabe–Ito (accessibility beats cosmetics).
/// Append-only — persisted implicitly through the equipped <see cref="BlockTheme"/>.
/// </summary>
public enum ColorPlan { Rainbow7, Trio, Duo, Mono, BoardGradient }

/// <summary>
/// A purchasable cosmetic skin: the seven piece colors plus the backdrop
/// gradient, an optional <see cref="SkinGlyph"/> stamped on every block, and a
/// <see cref="CellMaterial"/> finish. Themes deliberately do NOT touch the UI accent
/// system (buttons, glass, focus colors) — those textures are baked once at startup —
/// and the colorblind palette always overrides piece colors regardless of the
/// equipped theme (accessibility beats cosmetics). <paramref name="EdgeTint"/> is an
/// optional iridescent rim colour (used by dark/holographic skins; default = none).
///
/// <paramref name="Signature"/> is the burst FX this skin was DESIGNED with — a themed set
/// (colour + material + glyph + celebration) sold as one thing instead of four unrelated
/// toggles. Equipping such a skin also equips its burst, so the set lands whole; it is a
/// convenience, never a lock — the burst stays a separate free catalog row the player can
/// change back at any time. Persisted state is unchanged (only the theme id is saved), and
/// the parameter is appended LAST so all existing themes compile untouched.
/// </summary>
public sealed record BlockTheme(
    string Id,
    Color I, Color O, Color T, Color S, Color Z, Color J, Color L,
    Color BgTop, Color BgBottom,
    SkinGlyph Glyph = SkinGlyph.None,
    CellMaterial Material = CellMaterial.Gel,
    Color EdgeTint = default,
    ColorPlan Plan = ColorPlan.Rainbow7,
    BurstArtifact? Signature = null);
