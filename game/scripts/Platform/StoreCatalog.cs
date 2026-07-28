using Godot;
using Blockfall.Theme;

namespace Blockfall.Platform;

public enum StoreItemKind
{
    Theme,       // permanent cosmetic (block palette + backdrop + optional glyph)
    Artifact,    // permanent cosmetic (Block Fit line-clear burst style)
    BoosterPack, // consumable bundle (adds N uses)
    RemoveAds,   // permanent entitlement (mobile)
}

/// <summary>One entry in the store catalog.</summary>
public sealed class StoreItem
{
    public required string Id { get; init; }        // save-file key
    public required string ProductId { get; init; } // store product id (Play/App Store)
    public required StoreItemKind Kind { get; init; }
    public required string Name { get; init; }
    public required string Blurb { get; init; }
    /// <summary>Display placeholder — real localized prices come from the store SDK.</summary>
    public required string PriceLabel { get; init; }
    public BlockTheme? Theme { get; init; }
    public string BoosterId { get; init; } = "";
    public int BoosterCount { get; init; }
    /// <summary>Marks a fresh drop: <see cref="UI.StoreScreen"/> floats these to the top of their
    /// section and tags them. Purely a merchandising flag — never affects ownership or price.
    /// Clear it on the previous wave whenever a new one lands, or "NEW" stops meaning anything.</summary>
    public bool IsNew { get; init; }
}

/// <summary>
/// The in-game catalog. Product ids must match Play Console / App Store Connect
/// entries; PriceLabel is only a placeholder until the store SDK supplies real
/// localized prices. Rules encoded here: gold/violet stay reserved (themes only
/// retint pieces + backdrop), and boosters are SOLO-only consumables — the
/// revive is refused in Daily and never exists in versus, so purchases can't
/// buy competitive advantage.
/// </summary>
public static class StoreCatalog
{
    public const string DefaultThemeId = "theme_neon_flux";
    public const string BoosterSecondChance = "second_chance";

    public static readonly StoreItem[] Items =
    {
        new()
        {
            Id = DefaultThemeId, ProductId = "", Kind = StoreItemKind.Theme,
            Name = "NEON FLUX", Blurb = "THE ORIGINAL. ALWAYS YOURS.",
            PriceLabel = "FREE",
            Theme = Palette.DefaultTheme,
        },
        // Free skins — selectable from Settings › APPEARANCE (empty ProductId ⇒
        // owned by default; see SaveManager.OwnsItem). Retint blocks + backdrop.
        new()
        {
            Id = "theme_aurora", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "AURORA", Blurb = "COOL GREEN NIGHT, TEAL HORIZON.",
            PriceLabel = "FREE",
            Theme = new BlockTheme("theme_aurora",
                I: new Color(0.35f, 0.95f, 0.90f), O: new Color(0.95f, 0.90f, 0.45f),
                T: new Color(0.70f, 0.55f, 1.00f), S: new Color(0.45f, 0.95f, 0.60f),
                Z: new Color(1.00f, 0.45f, 0.55f), J: new Color(0.45f, 0.70f, 1.00f),
                L: new Color(1.00f, 0.70f, 0.40f),
                BgTop: new Color(0.016f, 0.063f, 0.063f), BgBottom: new Color(0.043f, 0.133f, 0.118f)),
        },
        new()
        {
            Id = "theme_tide", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "TIDE", Blurb = "DEEP OCEAN BLUES, COOL NEON.",
            PriceLabel = "FREE",
            Theme = new BlockTheme("theme_tide",
                I: new Color(0.35f, 0.90f, 1.00f), O: new Color(0.95f, 0.88f, 0.45f),
                T: new Color(0.65f, 0.60f, 1.00f), S: new Color(0.40f, 0.95f, 0.70f),
                Z: new Color(1.00f, 0.50f, 0.60f), J: new Color(0.40f, 0.65f, 1.00f),
                L: new Color(1.00f, 0.68f, 0.42f),
                BgTop: new Color(0.016f, 0.039f, 0.098f), BgBottom: new Color(0.031f, 0.090f, 0.200f)),
        },
        new()
        {
            Id = "theme_crimson", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "CRIMSON", Blurb = "DARK RED DUSK, WARM GLOW.",
            PriceLabel = "FREE",
            Theme = new BlockTheme("theme_crimson",
                I: new Color(0.40f, 0.85f, 1.00f), O: new Color(1.00f, 0.82f, 0.35f),
                T: new Color(0.90f, 0.50f, 0.95f), S: new Color(0.55f, 0.90f, 0.55f),
                Z: new Color(1.00f, 0.42f, 0.45f), J: new Color(0.50f, 0.60f, 1.00f),
                L: new Color(1.00f, 0.60f, 0.30f),
                BgTop: new Color(0.090f, 0.020f, 0.031f), BgBottom: new Color(0.180f, 0.043f, 0.063f)),
        },
        new()
        {
            Id = "theme_synthwave", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "SYNTHWAVE", Blurb = "PURPLE DUSK, MAGENTA & CYAN.",
            PriceLabel = "FREE",
            Theme = new BlockTheme("theme_synthwave",
                I: new Color(0.30f, 0.90f, 1.00f), O: new Color(1.00f, 0.35f, 0.75f),
                T: new Color(0.65f, 0.40f, 1.00f), S: new Color(0.35f, 0.95f, 0.85f),
                Z: new Color(1.00f, 0.30f, 0.55f), J: new Color(0.45f, 0.55f, 1.00f),
                L: new Color(1.00f, 0.60f, 0.35f),
                BgTop: new Color(0.063f, 0.020f, 0.100f), BgBottom: new Color(0.137f, 0.043f, 0.200f)),
        },
        new()
        {
            Id = "theme_mint", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "MINT", Blurb = "SOFT PASTEL, COOL MINT NIGHT.",
            PriceLabel = "FREE",
            Theme = new BlockTheme("theme_mint",
                I: new Color(0.45f, 0.95f, 0.85f), O: new Color(1.00f, 0.90f, 0.55f),
                T: new Color(0.75f, 0.65f, 1.00f), S: new Color(0.55f, 0.95f, 0.70f),
                Z: new Color(1.00f, 0.55f, 0.65f), J: new Color(0.55f, 0.75f, 1.00f),
                L: new Color(1.00f, 0.75f, 0.50f),
                BgTop: new Color(0.020f, 0.070f, 0.063f), BgBottom: new Color(0.047f, 0.129f, 0.110f)),
        },
        new()
        {
            Id = "theme_ember", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "EMBER", Blurb = "WARM AMBER COALS, GOLD GLOW.",
            PriceLabel = "FREE",
            Theme = new BlockTheme("theme_ember",
                I: new Color(0.50f, 0.85f, 1.00f), O: new Color(1.00f, 0.80f, 0.30f),
                T: new Color(1.00f, 0.55f, 0.75f), S: new Color(0.70f, 0.90f, 0.45f),
                Z: new Color(1.00f, 0.45f, 0.35f), J: new Color(0.60f, 0.60f, 1.00f),
                L: new Color(1.00f, 0.65f, 0.25f),
                BgTop: new Color(0.090f, 0.050f, 0.020f), BgBottom: new Color(0.160f, 0.090f, 0.040f)),
        },
        // Emoji-like glyph skins (free): vivid palettes that also stamp a cute mark on
        // every block. The glyph reads globally through Palette.EquippedGlyph.
        new()
        {
            Id = "theme_smiley", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "SMILEY POP", Blurb = "CANDY BRIGHTS WITH A HAPPY FACE ON EVERY BLOCK.",
            PriceLabel = "FREE",
            Theme = new BlockTheme("theme_smiley",
                I: new Color(0.25f, 0.90f, 1.00f), O: new Color(1.00f, 0.85f, 0.25f),
                T: new Color(0.85f, 0.45f, 1.00f), S: new Color(0.40f, 1.00f, 0.55f),
                Z: new Color(1.00f, 0.40f, 0.50f), J: new Color(0.40f, 0.60f, 1.00f),
                L: new Color(1.00f, 0.60f, 0.25f),
                BgTop: new Color(0.055f, 0.035f, 0.075f), BgBottom: new Color(0.120f, 0.070f, 0.140f),
                Glyph: SkinGlyph.Smile),
        },
        new()
        {
            Id = "theme_starlight", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "STARLIGHT", Blurb = "COSMIC NEON WITH A STAR ON EVERY BLOCK.",
            PriceLabel = "FREE",
            Theme = new BlockTheme("theme_starlight",
                I: new Color(0.45f, 0.85f, 1.00f), O: new Color(1.00f, 0.90f, 0.45f),
                T: new Color(0.70f, 0.50f, 1.00f), S: new Color(0.50f, 0.95f, 0.80f),
                Z: new Color(1.00f, 0.50f, 0.70f), J: new Color(0.50f, 0.65f, 1.00f),
                L: new Color(1.00f, 0.70f, 0.40f),
                BgTop: new Color(0.020f, 0.024f, 0.075f), BgBottom: new Color(0.050f, 0.055f, 0.160f),
                Glyph: SkinGlyph.Star),
        },
        new()
        {
            Id = "theme_lovecore", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "LOVECORE", Blurb = "ROSY POP WITH A HEART ON EVERY BLOCK.",
            PriceLabel = "FREE",
            Theme = new BlockTheme("theme_lovecore",
                I: new Color(0.55f, 0.90f, 1.00f), O: new Color(1.00f, 0.80f, 0.45f),
                T: new Color(1.00f, 0.50f, 0.85f), S: new Color(0.65f, 0.95f, 0.65f),
                Z: new Color(1.00f, 0.40f, 0.55f), J: new Color(0.65f, 0.60f, 1.00f),
                L: new Color(1.00f, 0.60f, 0.55f),
                BgTop: new Color(0.090f, 0.030f, 0.055f), BgBottom: new Color(0.170f, 0.055f, 0.100f),
                Glyph: SkinGlyph.Heart),
        },
        new()
        {
            Id = "theme_bloom", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "BLOOM", Blurb = "SPRING BRIGHTS WITH A FLOWER ON EVERY BLOCK.",
            PriceLabel = "FREE",
            Theme = new BlockTheme("theme_bloom",
                I: new Color(0.40f, 0.95f, 0.90f), O: new Color(1.00f, 0.85f, 0.35f),
                T: new Color(0.85f, 0.55f, 1.00f), S: new Color(0.45f, 1.00f, 0.60f),
                Z: new Color(1.00f, 0.45f, 0.60f), J: new Color(0.45f, 0.70f, 1.00f),
                L: new Color(1.00f, 0.65f, 0.30f),
                BgTop: new Color(0.025f, 0.065f, 0.045f), BgBottom: new Color(0.055f, 0.130f, 0.090f),
                Glyph: SkinGlyph.Flower),
        },
        new()
        {
            Id = "theme_voltage", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "VOLTAGE", Blurb = "ELECTRIC NEON WITH A BOLT ON EVERY BLOCK.",
            PriceLabel = "FREE",
            Theme = new BlockTheme("theme_voltage",
                I: new Color(0.30f, 1.00f, 1.00f), O: new Color(1.00f, 0.95f, 0.20f),
                T: new Color(0.80f, 0.40f, 1.00f), S: new Color(0.45f, 1.00f, 0.45f),
                Z: new Color(1.00f, 0.35f, 0.45f), J: new Color(0.35f, 0.65f, 1.00f),
                L: new Color(1.00f, 0.60f, 0.20f),
                BgTop: new Color(0.030f, 0.035f, 0.055f), BgBottom: new Color(0.070f, 0.080f, 0.120f),
                Glyph: SkinGlyph.Bolt),
        },
        new()
        {
            Id = "theme_sunset_drive", ProductId = "com.blockfall.theme.sunset", Kind = StoreItemKind.Theme,
            Name = "SUNSET DRIVE", Blurb = "WARM DUSK PINKS OVER A PURPLE HORIZON.",
            PriceLabel = "US$1.99",
            Theme = new BlockTheme("theme_sunset_drive",
                I: new Color(0.30f, 0.89f, 0.89f), O: new Color(1.00f, 0.82f, 0.30f),
                T: new Color(1.00f, 0.48f, 0.78f), S: new Color(0.50f, 0.90f, 0.39f),
                Z: new Color(1.00f, 0.36f, 0.36f), J: new Color(0.49f, 0.55f, 1.00f),
                L: new Color(1.00f, 0.58f, 0.25f),
                BgTop: new Color(0.094f, 0.039f, 0.118f), BgBottom: new Color(0.200f, 0.063f, 0.180f),
                Material: CellMaterial.Pearl),
        },
        new()
        {
            Id = "theme_deep_emerald", ProductId = "com.blockfall.theme.emerald", Kind = StoreItemKind.Theme,
            Name = "DEEP EMERALD", Blurb = "JEWEL TONES ON A DARK GREEN SEA.",
            PriceLabel = "US$1.99",
            Theme = new BlockTheme("theme_deep_emerald",
                I: new Color(0.27f, 0.91f, 0.78f), O: new Color(1.00f, 0.88f, 0.40f),
                T: new Color(0.65f, 0.55f, 0.98f), S: new Color(0.43f, 0.91f, 0.63f),
                Z: new Color(0.97f, 0.44f, 0.44f), J: new Color(0.38f, 0.65f, 0.98f),
                L: new Color(0.98f, 0.71f, 0.36f),
                BgTop: new Color(0.016f, 0.078f, 0.055f), BgBottom: new Color(0.043f, 0.165f, 0.118f),
                Material: CellMaterial.Gemstone),
        },
        new()
        {
            Id = "theme_mono_arcade", ProductId = "com.blockfall.theme.mono", Kind = StoreItemKind.Theme,
            Name = "MONO ARCADE", Blurb = "PURE GRAYSCALE. SHAPES ONLY. NO MERCY.",
            PriceLabel = "US$1.99",
            Theme = new BlockTheme("theme_mono_arcade",
                I: new Color(1.00f, 1.00f, 1.00f), O: new Color(0.89f, 0.89f, 0.92f),
                T: new Color(0.77f, 0.77f, 0.82f), S: new Color(0.65f, 0.65f, 0.71f),
                Z: new Color(0.53f, 0.53f, 0.60f), J: new Color(0.83f, 0.86f, 0.93f),
                L: new Color(0.60f, 0.65f, 0.75f),
                BgTop: new Color(0.039f, 0.039f, 0.055f), BgBottom: new Color(0.102f, 0.102f, 0.141f),
                Material: CellMaterial.Metallic),
        },
        // ---- Material skins: the "finish" axis (each feels like a different physical object) ----
        new()
        {
            Id = "theme_bubblegum", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "BUBBLEGUM", Blurb = "WET PASTEL GEL YOU COULD ALMOST CHEW, WITH A HEART ON EVERY BLOCK.",
            PriceLabel = "FREE",
            Theme = new BlockTheme("theme_bubblegum",
                I: new Color(0.55f, 0.95f, 1.00f), O: new Color(1.00f, 0.92f, 0.70f),
                T: new Color(1.00f, 0.70f, 0.95f), S: new Color(0.70f, 1.00f, 0.80f),
                Z: new Color(1.00f, 0.65f, 0.75f), J: new Color(0.70f, 0.80f, 1.00f),
                L: new Color(1.00f, 0.80f, 0.70f),
                BgTop: new Color(0.10f, 0.05f, 0.10f), BgBottom: new Color(0.20f, 0.10f, 0.20f),
                Glyph: SkinGlyph.Heart, Material: CellMaterial.Pearl),
        },
        new()
        {
            Id = "theme_glacier", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "GLACIER", Blurb = "MATTE FROSTED PEBBLES — THE QUIET, CALM PREMIUM.",
            PriceLabel = "FREE",
            Theme = new BlockTheme("theme_glacier",
                I: new Color(0.55f, 0.85f, 1.00f), O: new Color(0.90f, 0.90f, 0.75f),
                T: new Color(0.80f, 0.70f, 0.95f), S: new Color(0.60f, 0.95f, 0.85f),
                Z: new Color(1.00f, 0.75f, 0.80f), J: new Color(0.60f, 0.75f, 1.00f),
                L: new Color(0.95f, 0.82f, 0.70f),
                BgTop: new Color(0.02f, 0.05f, 0.09f), BgBottom: new Color(0.05f, 0.11f, 0.18f),
                Material: CellMaterial.Frosted),
        },
        new()
        {
            Id = "theme_galaxy", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "GALAXY", Blurb = "DEEP-SPACE JEWELS WITH STARS THAT TWINKLE ON EVERY BLOCK.",
            PriceLabel = "FREE",
            Theme = new BlockTheme("theme_galaxy",
                I: new Color(0.45f, 0.65f, 1.00f), O: new Color(1.00f, 0.85f, 0.45f),
                T: new Color(0.70f, 0.50f, 1.00f), S: new Color(0.45f, 0.90f, 0.75f),
                Z: new Color(1.00f, 0.50f, 0.70f), J: new Color(0.45f, 0.60f, 1.00f),
                L: new Color(1.00f, 0.65f, 0.45f),
                BgTop: new Color(0.03f, 0.02f, 0.09f), BgBottom: new Color(0.08f, 0.04f, 0.18f),
                Glyph: SkinGlyph.Star, Material: CellMaterial.Starfield),
        },
        new()
        {
            Id = "theme_circuit", ProductId = "com.blockfall.theme.circuit", Kind = StoreItemKind.Theme,
            Name = "CIRCUIT", Blurb = "BRUSHED-METAL CELLS WITH A LIVE TEAL EDGE AND A BOLT ON EVERY BLOCK.",
            PriceLabel = "US$1.99",
            Theme = new BlockTheme("theme_circuit",
                I: new Color(0.35f, 0.75f, 0.80f), O: new Color(0.85f, 0.80f, 0.55f),
                T: new Color(0.55f, 0.55f, 0.78f), S: new Color(0.45f, 0.80f, 0.65f),
                Z: new Color(0.85f, 0.55f, 0.55f), J: new Color(0.45f, 0.60f, 0.85f),
                L: new Color(0.85f, 0.70f, 0.50f),
                BgTop: new Color(0.02f, 0.05f, 0.06f), BgBottom: new Color(0.04f, 0.10f, 0.11f),
                Glyph: SkinGlyph.Bolt, Material: CellMaterial.Metallic, EdgeTint: new Color(0.25f, 0.95f, 0.85f, 0.7f)),
        },
        new()
        {
            Id = "theme_prism", ProductId = "com.blockfall.theme.prism", Kind = StoreItemKind.Theme,
            Name = "PRISM", Blurb = "NEON GLASS THAT BLEEDS IRIDESCENCE, WITH A GEM ON EVERY BLOCK.",
            PriceLabel = "US$2.99",
            Theme = new BlockTheme("theme_prism",
                I: new Color(0.40f, 0.95f, 1.00f), O: new Color(1.00f, 0.88f, 0.40f),
                T: new Color(0.80f, 0.50f, 1.00f), S: new Color(0.45f, 1.00f, 0.65f),
                Z: new Color(1.00f, 0.45f, 0.60f), J: new Color(0.45f, 0.65f, 1.00f),
                L: new Color(1.00f, 0.65f, 0.35f),
                BgTop: new Color(0.04f, 0.03f, 0.09f), BgBottom: new Color(0.10f, 0.06f, 0.18f),
                Glyph: SkinGlyph.Gem, Material: CellMaterial.Holographic),
        },
        new()
        {
            Id = "theme_obsidian", ProductId = "com.blockfall.theme.obsidian", Kind = StoreItemKind.Theme,
            Name = "OBSIDIAN", Blurb = "FACETED BLACK GLASS THAT BLEEDS RAINBOW AT EVERY EDGE.",
            PriceLabel = "US$2.99",
            Theme = new BlockTheme("theme_obsidian",
                I: new Color(0.20f, 0.35f, 0.50f), O: new Color(0.45f, 0.38f, 0.15f),
                T: new Color(0.35f, 0.20f, 0.45f), S: new Color(0.18f, 0.42f, 0.32f),
                Z: new Color(0.48f, 0.18f, 0.24f), J: new Color(0.18f, 0.26f, 0.48f),
                L: new Color(0.48f, 0.32f, 0.16f),
                BgTop: new Color(0.02f, 0.02f, 0.03f), BgBottom: new Color(0.05f, 0.05f, 0.07f),
                Glyph: SkinGlyph.Gem, Material: CellMaterial.Gemstone, EdgeTint: new Color(0.55f, 0.72f, 1.00f, 0.8f)),
        },
        // ---- Glyph skins showcasing the new embossed roster (free) ----
        new()
        {
            Id = "theme_meow", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "MEOW", Blurb = "CREAMY PASTELS WITH A KAWAII CAT ON EVERY BLOCK.",
            PriceLabel = "FREE",
            Theme = new BlockTheme("theme_meow",
                I: new Color(0.55f, 0.90f, 1.00f), O: new Color(1.00f, 0.85f, 0.55f),
                T: new Color(1.00f, 0.65f, 0.85f), S: new Color(0.65f, 0.95f, 0.70f),
                Z: new Color(1.00f, 0.55f, 0.60f), J: new Color(0.65f, 0.70f, 1.00f),
                L: new Color(1.00f, 0.72f, 0.55f),
                BgTop: new Color(0.09f, 0.05f, 0.06f), BgBottom: new Color(0.17f, 0.10f, 0.12f),
                Glyph: SkinGlyph.Cat, Material: CellMaterial.Pearl),
        },
        new()
        {
            Id = "theme_royale", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "ROYALE", Blurb = "REGAL PURPLE AND GOLD WITH A JEWELLED CROWN ON EVERY BLOCK.",
            PriceLabel = "FREE",
            Theme = new BlockTheme("theme_royale",
                I: new Color(0.45f, 0.80f, 1.00f), O: new Color(1.00f, 0.82f, 0.35f),
                T: new Color(0.75f, 0.50f, 1.00f), S: new Color(0.50f, 0.90f, 0.70f),
                Z: new Color(1.00f, 0.50f, 0.65f), J: new Color(0.55f, 0.60f, 1.00f),
                L: new Color(1.00f, 0.68f, 0.40f),
                BgTop: new Color(0.06f, 0.03f, 0.09f), BgBottom: new Color(0.13f, 0.07f, 0.18f),
                Glyph: SkinGlyph.Crown),
        },
        new()
        {
            Id = "theme_midnight", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "MIDNIGHT", Blurb = "DEEP VIOLET NIGHT WITH A CRESCENT MOON ON EVERY BLOCK.",
            PriceLabel = "FREE",
            Theme = new BlockTheme("theme_midnight",
                I: new Color(0.50f, 0.75f, 1.00f), O: new Color(0.95f, 0.88f, 0.55f),
                T: new Color(0.70f, 0.55f, 1.00f), S: new Color(0.55f, 0.90f, 0.80f),
                Z: new Color(1.00f, 0.55f, 0.70f), J: new Color(0.50f, 0.62f, 1.00f),
                L: new Color(1.00f, 0.72f, 0.45f),
                BgTop: new Color(0.02f, 0.02f, 0.08f), BgBottom: new Color(0.06f, 0.05f, 0.17f),
                Glyph: SkinGlyph.Moon),
        },
        new()
        {
            Id = "theme_lucky", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "LUCKY", Blurb = "FRESH GREENS WITH A LUCKY CLOVER ON EVERY BLOCK.",
            PriceLabel = "FREE",
            Theme = new BlockTheme("theme_lucky",
                I: new Color(0.45f, 0.90f, 0.95f), O: new Color(1.00f, 0.88f, 0.40f),
                T: new Color(0.70f, 0.60f, 1.00f), S: new Color(0.45f, 0.98f, 0.60f),
                Z: new Color(1.00f, 0.55f, 0.55f), J: new Color(0.45f, 0.72f, 1.00f),
                L: new Color(1.00f, 0.70f, 0.35f),
                BgTop: new Color(0.02f, 0.07f, 0.04f), BgBottom: new Color(0.05f, 0.14f, 0.09f),
                Glyph: SkinGlyph.Clover),
        },
        new()
        {
            Id = "theme_spooky", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "SPOOKY", Blurb = "HAUNTED GREEN AND PURPLE WITH A BONE-WHITE SKULL ON EVERY BLOCK.",
            PriceLabel = "FREE",
            Theme = new BlockTheme("theme_spooky",
                I: new Color(0.50f, 0.95f, 0.70f), O: new Color(1.00f, 0.70f, 0.25f),
                T: new Color(0.75f, 0.45f, 1.00f), S: new Color(0.55f, 1.00f, 0.50f),
                Z: new Color(1.00f, 0.45f, 0.45f), J: new Color(0.55f, 0.55f, 1.00f),
                L: new Color(1.00f, 0.60f, 0.25f),
                BgTop: new Color(0.04f, 0.05f, 0.03f), BgBottom: new Color(0.09f, 0.11f, 0.07f),
                Glyph: SkinGlyph.Skull),
        },
        new()
        {
            Id = "theme_pride", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "PRIDE", Blurb = "JOYFUL BRIGHTS WITH A RAINBOW ON EVERY BLOCK.",
            PriceLabel = "FREE",
            Theme = new BlockTheme("theme_pride",
                I: new Color(0.35f, 0.90f, 1.00f), O: new Color(1.00f, 0.85f, 0.30f),
                T: new Color(0.85f, 0.45f, 1.00f), S: new Color(0.45f, 1.00f, 0.55f),
                Z: new Color(1.00f, 0.40f, 0.50f), J: new Color(0.40f, 0.60f, 1.00f),
                L: new Color(1.00f, 0.60f, 0.25f),
                BgTop: new Color(0.05f, 0.04f, 0.07f), BgBottom: new Color(0.11f, 0.08f, 0.15f),
                Glyph: SkinGlyph.Rainbow),
        },
        // ---- ColorPlan skins: the "how many colours" axis --------------------------------
        // Every skin above is a permutation of the same 7-hue rainbow, which is why they blur
        // together at arm's length. These four spend a DIFFERENT NUMBER of colours on the
        // board (three / one / two / a positional ramp) and pair it with a distinct material,
        // so they read as different objects from across the room. Legal only because Block Fit
        // attaches no rule meaning to a cell's hue — and colorblind mode overrides all of them
        // back to Okabe–Ito, where the MATERIAL becomes the identity channel instead.
        new()
        {
            Id = "theme_oak", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "OAK", Blurb = "THREE TIMBER TONES. MATTE GRAIN, NO GLOSS — IT BREAKS INTO SPLINTERS.",
            PriceLabel = "FREE", IsNew = true,
            Theme = new BlockTheme("theme_oak",
                I: new Color(0.788f, 0.561f, 0.322f), O: new Color(0.890f, 0.729f, 0.522f),
                T: new Color(0.549f, 0.353f, 0.200f), S: new Color(0.890f, 0.729f, 0.522f),
                Z: new Color(0.549f, 0.353f, 0.200f), J: new Color(0.788f, 0.561f, 0.322f),
                L: new Color(0.890f, 0.729f, 0.522f),
                BgTop: new Color(0.055f, 0.040f, 0.028f), BgBottom: new Color(0.115f, 0.082f, 0.055f),
                Material: CellMaterial.Wood, Plan: ColorPlan.Trio),
        },
        new()
        {
            Id = "theme_sumi", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "SUMI", Blurb = "ONE INK, SEVEN SHADES. FROSTED STONE — THE QUIETEST BOARD IN THE GAME.",
            PriceLabel = "FREE", IsNew = true,
            Theme = new BlockTheme("theme_sumi",
                I: new Color(0.847f, 0.863f, 0.902f), O: new Color(0.780f, 0.796f, 0.839f),
                T: new Color(0.714f, 0.729f, 0.776f), S: new Color(0.647f, 0.663f, 0.714f),
                Z: new Color(0.580f, 0.596f, 0.651f), J: new Color(0.514f, 0.529f, 0.588f),
                L: new Color(0.447f, 0.463f, 0.525f),
                BgTop: new Color(0.035f, 0.036f, 0.043f), BgBottom: new Color(0.082f, 0.086f, 0.102f),
                Material: CellMaterial.Frosted, Plan: ColorPlan.Mono),
        },
        new()
        {
            Id = "theme_vapor_tube", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "VAPOR TUBE", Blurb = "TWO GASES IN HOLLOW GLASS. IT SHATTERS INTO GLOWING ARCS.",
            PriceLabel = "FREE", IsNew = true,
            Theme = new BlockTheme("theme_vapor_tube",
                I: new Color(0.247f, 0.910f, 1.000f), O: new Color(1.000f, 0.337f, 0.784f),
                T: new Color(0.247f, 0.910f, 1.000f), S: new Color(1.000f, 0.337f, 0.784f),
                Z: new Color(1.000f, 0.337f, 0.784f), J: new Color(0.247f, 0.910f, 1.000f),
                L: new Color(1.000f, 0.337f, 0.784f),
                BgTop: new Color(0.040f, 0.020f, 0.062f), BgBottom: new Color(0.090f, 0.043f, 0.130f),
                Material: CellMaterial.NeonTube, Plan: ColorPlan.Duo),
        },
        new()
        {
            Id = "theme_dichroic", ProductId = "com.blockfall.theme.dichroic", Kind = StoreItemKind.Theme,
            Name = "DICHROIC", Blurb = "THE BOARD ITSELF IS THE GRADIENT — CYAN AT ONE CORNER, VIOLET AT THE OTHER.",
            PriceLabel = "US$2.99", IsNew = true,
            Theme = new BlockTheme("theme_dichroic",
                I: new Color(0.353f, 0.784f, 1.000f), O: new Color(0.620f, 0.640f, 1.000f),
                T: new Color(0.780f, 0.482f, 1.000f), S: new Color(0.500f, 0.700f, 1.000f),
                Z: new Color(0.700f, 0.560f, 1.000f), J: new Color(0.420f, 0.740f, 1.000f),
                L: new Color(0.640f, 0.620f, 1.000f),
                BgTop: new Color(0.030f, 0.030f, 0.070f), BgBottom: new Color(0.070f, 0.055f, 0.145f),
                Material: CellMaterial.Holographic, Plan: ColorPlan.BoardGradient,
                EdgeTint: new Color(1.000f, 0.851f, 0.941f, 0.35f)),
        },
        // Burst-FX artifacts (free): the line-clear celebration Block Fit plays.
        // Cosmetic only — never touches scoring. "artifact_sparks" is the default.
        new()
        {
            Id = "artifact_sparks", ProductId = "", Kind = StoreItemKind.Artifact,
            Name = "SPARKLE", Blurb = "THE ORIGINAL GOLDEN SPARK BURST.",
            PriceLabel = "FREE",
        },
        new()
        {
            Id = "artifact_fireworks", ProductId = "", Kind = StoreItemKind.Artifact,
            Name = "FIREWORKS", Blurb = "MULTICOLOUR BURSTS AND BOOMING RINGS.",
            PriceLabel = "FREE",
        },
        new()
        {
            Id = "artifact_confetti", ProductId = "", Kind = StoreItemKind.Artifact,
            Name = "CONFETTI", Blurb = "A RAIN OF COLOURFUL PAPER BITS.",
            PriceLabel = "FREE",
        },
        new()
        {
            Id = "artifact_supernova", ProductId = "", Kind = StoreItemKind.Artifact,
            Name = "SUPERNOVA", Blurb = "A BLINDING FLASH AND A HUGE SHOCKWAVE.",
            PriceLabel = "FREE",
        },
        new()
        {
            Id = "artifact_shards", ProductId = "", Kind = StoreItemKind.Artifact,
            Name = "PRISM SHARDS", Blurb = "CHUNKY NEON SHARDS BLASTED OUTWARD.",
            PriceLabel = "FREE",
        },
        new()
        {
            Id = "artifact_rainbow", ProductId = "", Kind = StoreItemKind.Artifact,
            Name = "RAINBOW WAVE", Blurb = "A FULL-SPECTRUM SWEEP ALONG THE LINE.",
            PriceLabel = "FREE",
        },
        new()
        {
            Id = "artifact_aurora", ProductId = "", Kind = StoreItemKind.Artifact,
            Name = "AURORA", Blurb = "SOFT CURTAINS OF LIGHT RISE INSTEAD OF A BURST.",
            PriceLabel = "FREE",
        },
        new()
        {
            Id = "artifact_lightning", ProductId = "", Kind = StoreItemKind.Artifact,
            Name = "LIGHTNING", Blurb = "ELECTRIC ARCS SNAP ACROSS EACH CLEARED LINE.",
            PriceLabel = "FREE",
        },
        new()
        {
            Id = "artifact_bubblepop", ProductId = "", Kind = StoreItemKind.Artifact,
            Name = "BUBBLE POP", Blurb = "IRIDESCENT SOAP BUBBLES DRIFT UP AND POP.",
            PriceLabel = "FREE",
        },
        new()
        {
            Id = "artifact_prismbloom", ProductId = "", Kind = StoreItemKind.Artifact,
            Name = "PRISM BLOOM", Blurb = "CLEARS DISSOLVE INTO A RISING CLOUD OF RAINBOW LIGHT.",
            PriceLabel = "FREE",
        },
        new()
        {
            Id = "artifact_starfall", ProductId = "", Kind = StoreItemKind.Artifact,
            Name = "STARFALL", Blurb = "A SHOWER OF METEORS STREAKS ACROSS THE CLEAR.",
            PriceLabel = "FREE",
        },
        new()
        {
            Id = "booster_second_chance_3", ProductId = "com.blockfall.booster.secondchance3",
            Kind = StoreItemKind.BoosterPack,
            Name = "SECOND CHANCE ×3", Blurb = "TOP OUT, WIPE THE BOARD, KEEP THE RUN. SOLO MODES ONLY — REVIVED RUNS SET NO RECORDS.",
            PriceLabel = "US$0.99",
            BoosterId = BoosterSecondChance, BoosterCount = 3,
        },
        new()
        {
            Id = "remove_ads", ProductId = "com.blockfall.removeads", Kind = StoreItemKind.RemoveAds,
            Name = "REMOVE ADS", Blurb = "NO MORE INTERSTITIALS. FOREVER.",
            PriceLabel = "US$2.99",
        },
    };

    public static StoreItem? ById(string id)
    {
        foreach (var item in Items)
            if (item.Id == id) return item;
        return null;
    }
}
