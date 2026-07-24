using Godot;

namespace Blockfall.Theme;

/// <summary>
/// A purchasable cosmetic skin: the seven piece colors plus the backdrop
/// gradient, and an optional <see cref="SkinGlyph"/> stamped on every block
/// (emoji-like skins). Themes deliberately do NOT touch the UI accent system
/// (buttons, glass, focus colors) — those textures are baked once at startup —
/// and the colorblind palette always overrides piece colors regardless of the
/// equipped theme (accessibility beats cosmetics).
/// </summary>
public sealed record BlockTheme(
    string Id,
    Color I, Color O, Color T, Color S, Color Z, Color J, Color L,
    Color BgTop, Color BgBottom,
    SkinGlyph Glyph = SkinGlyph.None);
