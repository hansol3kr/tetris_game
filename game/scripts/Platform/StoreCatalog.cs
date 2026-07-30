using Godot;
using Blockfall.Theme;

namespace Blockfall.Platform;

/// <summary>What shelf an item sits on. Not persisted anywhere (the save stores ids), so the
/// order is free — but <see cref="UI.StoreScreen"/> derives its category tabs from this set, so
/// a new value is a new tab and needs a row builder to go with it.</summary>
public enum StoreItemKind
{
    Theme,       // permanent cosmetic (block palette + finish + optional glyph)
    Artifact,    // permanent cosmetic (Block Fit line-clear burst style)
    Backdrop,    // permanent cosmetic (the procedural scene behind every screen and the board)
    SoundPack,   // permanent cosmetic (placement timbre) — always free, see below
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
    /// <summary>For <see cref="StoreItemKind.SoundPack"/>: the index a store row would write to
    /// <c>GameSettings.SfxPack</c>. The setting stays the single source of truth for what the
    /// player hears — the catalog only merchandises it, exactly like a free skin.</summary>
    public int SoundPackIndex { get; init; }
    /// <summary>Marks a fresh drop: <see cref="UI.StoreScreen"/> floats these to the top of their
    /// section and tags them (THEMES, BURST FX and SOUND all honour it). Purely a merchandising
    /// flag — never affects ownership or price.
    /// <para>Clear it on the previous wave IN THE SAME EDIT that flags a new one. This was
    /// missed once: four v1.6 skins kept the flag, so the next wave landed at NEW-block
    /// positions 5-7 and the badge stopped meaning "look here". The rule of thumb is that
    /// the flagged set should never exceed one screen of rows (≈3-4 per section).</para></summary>
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
                BgTop: new Color(0.016f, 0.063f, 0.063f), BgBottom: new Color(0.043f, 0.133f, 0.118f),
                Accent: new Color(0.35f, 0.95f, 0.90f), Accent2: new Color(0.45f, 0.75f, 1.00f)),
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
                BgTop: new Color(0.016f, 0.039f, 0.098f), BgBottom: new Color(0.031f, 0.090f, 0.200f),
                Scene: BackdropStyle.Waves,
                Accent: new Color(0.30f, 0.85f, 1.00f), Accent2: new Color(0.38f, 0.62f, 1.00f)),
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
                BgTop: new Color(0.090f, 0.020f, 0.031f), BgBottom: new Color(0.180f, 0.043f, 0.063f),
                Scene: BackdropStyle.Embers,
                Accent: new Color(1.00f, 0.45f, 0.45f), Accent2: new Color(0.95f, 0.35f, 0.62f)),
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
                BgTop: new Color(0.063f, 0.020f, 0.100f), BgBottom: new Color(0.137f, 0.043f, 0.200f),
                Scene: BackdropStyle.Grid,
                Accent: new Color(1.00f, 0.35f, 0.75f), Accent2: new Color(0.30f, 0.90f, 1.00f)),
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
                BgTop: new Color(0.020f, 0.070f, 0.063f), BgBottom: new Color(0.047f, 0.129f, 0.110f),
                Accent: new Color(0.45f, 0.95f, 0.85f), Accent2: new Color(0.75f, 0.65f, 1.00f)),
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
                BgTop: new Color(0.090f, 0.050f, 0.020f), BgBottom: new Color(0.160f, 0.090f, 0.040f),
                Scene: BackdropStyle.Embers,
                Accent: new Color(1.00f, 0.58f, 0.22f), Accent2: new Color(1.00f, 0.42f, 0.30f)),
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
                Glyph: SkinGlyph.Smile, Scene: BackdropStyle.Bokeh,
                Accent: new Color(0.35f, 0.90f, 1.00f), Accent2: new Color(1.00f, 0.55f, 0.90f)),
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
                Glyph: SkinGlyph.Star, Scene: BackdropStyle.Starfield,
                Accent: new Color(0.55f, 0.85f, 1.00f), Accent2: new Color(0.75f, 0.60f, 1.00f)),
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
                Glyph: SkinGlyph.Heart, Scene: BackdropStyle.Bokeh,
                Accent: new Color(1.00f, 0.55f, 0.80f), Accent2: new Color(0.75f, 0.60f, 1.00f)),
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
                Glyph: SkinGlyph.Flower, Scene: BackdropStyle.Bokeh,
                Accent: new Color(0.50f, 0.95f, 0.70f), Accent2: new Color(0.85f, 0.60f, 1.00f)),
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
                Glyph: SkinGlyph.Bolt, Scene: BackdropStyle.Circuit,
                Accent: new Color(0.30f, 1.00f, 1.00f), Accent2: new Color(0.55f, 1.00f, 0.45f)),
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
                Material: CellMaterial.Pearl, Scene: BackdropStyle.Rays,
                Accent: new Color(1.00f, 0.55f, 0.80f), Accent2: new Color(0.70f, 0.50f, 1.00f)),
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
                Material: CellMaterial.Gemstone, Scene: BackdropStyle.Waves,
                Accent: new Color(0.30f, 0.92f, 0.78f), Accent2: new Color(0.45f, 0.70f, 1.00f)),
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
                Material: CellMaterial.Metallic, Scene: BackdropStyle.Circuit,
                Accent: new Color(0.85f, 0.88f, 0.95f), Accent2: new Color(0.62f, 0.67f, 0.80f)),
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
                Glyph: SkinGlyph.Heart, Material: CellMaterial.Pearl, Scene: BackdropStyle.Bokeh,
                Accent: new Color(1.00f, 0.70f, 0.92f), Accent2: new Color(0.60f, 0.90f, 1.00f)),
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
                Material: CellMaterial.Frosted, Scene: BackdropStyle.Starfield,
                Accent: new Color(0.60f, 0.85f, 1.00f), Accent2: new Color(0.80f, 0.75f, 1.00f)),
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
                Glyph: SkinGlyph.Star, Material: CellMaterial.Starfield, Scene: BackdropStyle.Starfield,
                Accent: new Color(0.60f, 0.70f, 1.00f), Accent2: new Color(0.75f, 0.55f, 1.00f)),
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
                Glyph: SkinGlyph.Bolt, Material: CellMaterial.Metallic, EdgeTint: new Color(0.25f, 0.95f, 0.85f, 0.7f),
                Scene: BackdropStyle.Circuit,
                Accent: new Color(0.25f, 0.95f, 0.85f), Accent2: new Color(0.55f, 0.75f, 0.92f)),
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
                Glyph: SkinGlyph.Gem, Material: CellMaterial.Holographic, Scene: BackdropStyle.Nebula,
                Accent: new Color(0.50f, 0.95f, 1.00f), Accent2: new Color(0.90f, 0.55f, 1.00f)),
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
                Glyph: SkinGlyph.Gem, Material: CellMaterial.Gemstone, EdgeTint: new Color(0.55f, 0.72f, 1.00f, 0.8f),
                Scene: BackdropStyle.Nebula,
                Accent: new Color(0.60f, 0.75f, 1.00f), Accent2: new Color(0.85f, 0.60f, 1.00f)),
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
                Glyph: SkinGlyph.Cat, Material: CellMaterial.Pearl, Scene: BackdropStyle.Bokeh,
                Accent: new Color(1.00f, 0.68f, 0.85f), Accent2: new Color(0.70f, 0.85f, 1.00f)),
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
                Glyph: SkinGlyph.Crown, Scene: BackdropStyle.Rays,
                Accent: new Color(0.80f, 0.55f, 1.00f), Accent2: new Color(0.55f, 0.62f, 1.00f)),
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
                Glyph: SkinGlyph.Moon, Scene: BackdropStyle.Starfield,
                Accent: new Color(0.60f, 0.72f, 1.00f), Accent2: new Color(0.70f, 0.55f, 1.00f)),
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
                Glyph: SkinGlyph.Clover, Scene: BackdropStyle.Bokeh,
                Accent: new Color(0.50f, 0.95f, 0.60f), Accent2: new Color(0.45f, 0.80f, 1.00f)),
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
                Glyph: SkinGlyph.Skull, Scene: BackdropStyle.Nebula,
                Accent: new Color(0.60f, 1.00f, 0.55f), Accent2: new Color(0.75f, 0.45f, 1.00f)),
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
                Glyph: SkinGlyph.Rainbow, Scene: BackdropStyle.Rays,
                Accent: new Color(0.40f, 0.90f, 1.00f), Accent2: new Color(1.00f, 0.50f, 0.85f)),
        },
        // ---- ColorPlan skins: the "how many colours" axis --------------------------------
        // Every skin above is a permutation of the same 7-hue rainbow, which is why they blur
        // together at arm's length. These four spend a DIFFERENT NUMBER of colours on the
        // board (three / one / two / a positional ramp) and pair it with a distinct material,
        // so they read as different objects from across the room. Legal only because Block Fit
        // attaches no rule meaning to a cell's hue — and colorblind mode overrides all of them
        // back to Okabe–Ito, where the MATERIAL becomes the identity channel instead.
        //
        // WAVE STATUS: shipped, no longer NEW. These four IsNew flags were cleared when the
        // ANIMAL SETS below landed — left standing, they had pushed that wave to positions
        // 5-7 of the NEW block, which is the exact failure the flag exists to prevent.
        // Whoever adds the next wave clears the animal sets' flags in the same edit.
        new()
        {
            Id = "theme_oak", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "OAK", Blurb = "THREE TIMBER TONES. MATTE GRAIN, NO GLOSS — IT BREAKS INTO SPLINTERS.",
            PriceLabel = "FREE",
            Theme = new BlockTheme("theme_oak",
                I: new Color(0.788f, 0.561f, 0.322f), O: new Color(0.890f, 0.729f, 0.522f),
                T: new Color(0.549f, 0.353f, 0.200f), S: new Color(0.890f, 0.729f, 0.522f),
                Z: new Color(0.549f, 0.353f, 0.200f), J: new Color(0.788f, 0.561f, 0.322f),
                L: new Color(0.890f, 0.729f, 0.522f),
                BgTop: new Color(0.055f, 0.040f, 0.028f), BgBottom: new Color(0.115f, 0.082f, 0.055f),
                Material: CellMaterial.Wood, Plan: ColorPlan.Trio, Scene: BackdropStyle.Bokeh,
                Accent: new Color(0.90f, 0.72f, 0.48f), Accent2: new Color(0.72f, 0.85f, 0.60f)),
        },
        new()
        {
            Id = "theme_sumi", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "SUMI", Blurb = "ONE INK, SEVEN SHADES. FROSTED STONE — THE QUIETEST BOARD IN THE GAME.",
            PriceLabel = "FREE",
            Theme = new BlockTheme("theme_sumi",
                I: new Color(0.847f, 0.863f, 0.902f), O: new Color(0.780f, 0.796f, 0.839f),
                T: new Color(0.714f, 0.729f, 0.776f), S: new Color(0.647f, 0.663f, 0.714f),
                Z: new Color(0.580f, 0.596f, 0.651f), J: new Color(0.514f, 0.529f, 0.588f),
                L: new Color(0.447f, 0.463f, 0.525f),
                BgTop: new Color(0.035f, 0.036f, 0.043f), BgBottom: new Color(0.082f, 0.086f, 0.102f),
                Material: CellMaterial.Frosted, Plan: ColorPlan.Mono, Scene: BackdropStyle.Rain,
                Accent: new Color(0.82f, 0.85f, 0.92f), Accent2: new Color(0.58f, 0.63f, 0.75f)),
        },
        new()
        {
            Id = "theme_vapor_tube", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "VAPOR TUBE", Blurb = "TWO GASES IN HOLLOW GLASS. IT SHATTERS INTO GLOWING ARCS.",
            PriceLabel = "FREE",
            Theme = new BlockTheme("theme_vapor_tube",
                I: new Color(0.247f, 0.910f, 1.000f), O: new Color(1.000f, 0.337f, 0.784f),
                T: new Color(0.247f, 0.910f, 1.000f), S: new Color(1.000f, 0.337f, 0.784f),
                Z: new Color(1.000f, 0.337f, 0.784f), J: new Color(0.247f, 0.910f, 1.000f),
                L: new Color(1.000f, 0.337f, 0.784f),
                BgTop: new Color(0.040f, 0.020f, 0.062f), BgBottom: new Color(0.090f, 0.043f, 0.130f),
                Material: CellMaterial.NeonTube, Plan: ColorPlan.Duo, Scene: BackdropStyle.Grid,
                Accent: new Color(0.25f, 0.91f, 1.00f), Accent2: new Color(1.00f, 0.34f, 0.78f)),
        },
        new()
        {
            Id = "theme_dichroic", ProductId = "com.blockfall.theme.dichroic", Kind = StoreItemKind.Theme,
            Name = "DICHROIC", Blurb = "THE BOARD ITSELF IS THE GRADIENT — CYAN AT ONE CORNER, VIOLET AT THE OTHER.",
            PriceLabel = "US$2.99",
            Theme = new BlockTheme("theme_dichroic",
                I: new Color(0.353f, 0.784f, 1.000f), O: new Color(0.620f, 0.640f, 1.000f),
                T: new Color(0.780f, 0.482f, 1.000f), S: new Color(0.500f, 0.700f, 1.000f),
                Z: new Color(0.700f, 0.560f, 1.000f), J: new Color(0.420f, 0.740f, 1.000f),
                L: new Color(0.640f, 0.620f, 1.000f),
                BgTop: new Color(0.030f, 0.030f, 0.070f), BgBottom: new Color(0.070f, 0.055f, 0.145f),
                Material: CellMaterial.Holographic, Plan: ColorPlan.BoardGradient,
                EdgeTint: new Color(1.000f, 0.851f, 0.941f, 0.35f),
                Scene: BackdropStyle.Rays,
                Accent: new Color(0.45f, 0.80f, 1.00f), Accent2: new Color(0.78f, 0.50f, 1.00f)),
        },
        // ---- The ANIMAL SETS: colour + material + glyph + burst, sold as one thing -------
        // The previous wave proved the material axis; these three prove the SET. A skin here is
        // not a palette with a sticker on it — it ships its own surface (CellMaterial), its own
        // stamp (SkinGlyph), its own debris physics (DebrisField.Phys), its own lock pitch
        // (AudioManager.MaterialPitch) and its own line-clear celebration (BlockTheme.Signature).
        //
        // Copyright posture, stated once for all three: every mark is ANATOMY or a natural
        // surface — a coat, overlapping scales, a hex carapace, a paw pad, a dorsal fin, a pair
        // of wing cases. No faces (see the faceless rule in GlyphArt), no species markings, no
        // proper nouns, no mascots. And none of the three is a 7-hue rainbow plan, so none of
        // them can collide with any official trade dress even by accident.
        new()
        {
            Id = "theme_pelt", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "PELT", Blurb = "BONE, FOX AND CHARCOAL. MATTE STRAND GRAIN — IT SHEDS, IT DOESN'T SHATTER.",
            PriceLabel = "FREE",
            // Anchors moved OUT of the timber band (hue ~30 deg, compressed mid-luma) that OAK
            // already owns. At arm's length luma rhythm reads before hue: OAK runs 0.531/0.327/0.131
            // (compressed mids), PELT now runs 0.776/0.138/0.073 (high-contrast three-step).
            // Worst cross-pair against OAK's anchors: sRGB 0.083 / dE76 9.6  ->  0.210 / dE76 24.5.
            // 9.6 is "only visible side by side"; 24.5 is a different object across the room.
            // PELT moved rather than OAK because OAK already ships and sits in inventories —
            // swapping a colour under an owner who bought it is an unannounced asset change.
            // Only I/T/S render under ColorPlan.Trio; the rest are data-consistency filler.
            Theme = new BlockTheme("theme_pelt",
                I: new Color(0.949f, 0.886f, 0.804f), O: new Color(0.859f, 0.643f, 0.478f),
                T: new Color(0.722f, 0.243f, 0.153f), S: new Color(0.310f, 0.290f, 0.345f),
                Z: new Color(0.545f, 0.184f, 0.125f), J: new Color(0.353f, 0.208f, 0.180f),
                L: new Color(0.867f, 0.412f, 0.239f),
                BgTop: new Color(0.055f, 0.038f, 0.030f), BgBottom: new Color(0.118f, 0.082f, 0.062f),
                Glyph: SkinGlyph.Paw, Material: CellMaterial.Pelt,
                EdgeTint: new Color(1.000f, 0.780f, 0.500f, 0.30f),
                Plan: ColorPlan.Trio, Signature: BurstArtifact.Fluff,
                Scene: BackdropStyle.Bokeh,
                Accent: new Color(0.95f, 0.75f, 0.55f), Accent2: new Color(0.87f, 0.45f, 0.28f)),
        },
        new()
        {
            // Named DORSAL, not for the snake it was first called: every other channel of this
            // set is a FISH (dorsal-fin stamp, water-column + ripple burst, wet scales, a
            // gradient that reads as one body under the surface), and a name that fought all
            // four made the set read as two half-ideas. DORSAL also puts the trio on one
            // naming axis — PELT / DORSAL / CARAPACE are all plain anatomy nouns, which is the
            // same copyright posture the glyphs already hold (anatomy, never a species mark).
            // The Id stays "theme_serpentine": it is the save-file key for everyone who
            // already equipped it, and a display name is not worth orphaning that.
            Id = "theme_serpentine", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "DORSAL", Blurb = "ONE BODY ACROSS THE WHOLE BOARD. WET OVERLAPPING SCALES.",
            PriceLabel = "FREE",
            Theme = new BlockTheme("theme_serpentine",
                I: new Color(0.267f, 0.855f, 0.788f), O: new Color(0.400f, 0.780f, 0.700f),
                T: new Color(0.180f, 0.235f, 0.545f), S: new Color(0.290f, 0.640f, 0.760f),
                Z: new Color(0.235f, 0.500f, 0.720f), J: new Color(0.200f, 0.380f, 0.640f),
                L: new Color(0.520f, 0.880f, 0.820f),
                BgTop: new Color(0.012f, 0.043f, 0.055f), BgBottom: new Color(0.027f, 0.086f, 0.110f),
                Glyph: SkinGlyph.Fin, Material: CellMaterial.Scale,
                EdgeTint: new Color(0.600f, 1.000f, 0.950f, 0.45f),
                Plan: ColorPlan.BoardGradient, Signature: BurstArtifact.Splash,
                Scene: BackdropStyle.Waves,
                Accent: new Color(0.35f, 0.90f, 0.85f), Accent2: new Color(0.30f, 0.58f, 0.85f)),
        },
        new()
        {
            Id = "theme_carapace", ProductId = "com.blockfall.theme.carapace", Kind = StoreItemKind.Theme,
            Name = "CARAPACE", Blurb = "ONE LACQUERED HUE, SEVEN RUNGS. THE HARDEST BOARD IN THE GAME.",
            PriceLabel = "US$2.99",
            Theme = new BlockTheme("theme_carapace",
                I: new Color(0.353f, 0.690f, 0.478f), O: new Color(0.500f, 0.750f, 0.400f),
                T: new Color(0.250f, 0.560f, 0.420f), S: new Color(0.180f, 0.440f, 0.330f),
                Z: new Color(0.600f, 0.700f, 0.250f), J: new Color(0.200f, 0.350f, 0.300f),
                L: new Color(0.420f, 0.620f, 0.280f),
                BgTop: new Color(0.020f, 0.035f, 0.028f), BgBottom: new Color(0.045f, 0.075f, 0.058f),
                Glyph: SkinGlyph.Elytra, Material: CellMaterial.Chitin,
                EdgeTint: new Color(0.550f, 1.000f, 0.850f, 0.75f),
                Plan: ColorPlan.Mono, Signature: BurstArtifact.Swarm,
                Scene: BackdropStyle.Circuit,
                Accent: new Color(0.45f, 0.90f, 0.62f), Accent2: new Color(0.68f, 0.87f, 0.32f)),
        },
        // ---- The SCENE SETS: skins built around a backdrop ------------------------------
        // The wave that follows the animal sets, and the first one authored AFTER the backdrop
        // became a cosmetic axis. Each of these is a skin designed backwards from a scene: the
        // gradient is the scene's sky, the piece colours are what reads against it, the material
        // is what the scene's light would do to a solid object, and the burst is what that object
        // does when it breaks. Equipping one changes the menu, the store, the board and the space
        // around the board at once — which is the whole point of the axis.
        //
        // All free, deliberately. MobilePlatform.PurchaseItem still completes without a billing
        // plugin (Platforms.cs), so every paid SKU is a grant-for-free bug waiting for the day a
        // real plugin lands; a fifteen-item wave is not the place to add fifteen more of them.
        // Pricing is publishing's call once billing is real — until then the shelf is honest.
        new()
        {
            Id = "theme_night_drive", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "NIGHT DRIVE", Blurb = "TWO GASES IN GLASS OVER A LIT FLOOR RUNNING TO THE HORIZON.",
            PriceLabel = "FREE", IsNew = true,
            Theme = new BlockTheme("theme_night_drive",
                I: new Color(0.216f, 0.949f, 1.000f), O: new Color(1.000f, 0.216f, 0.667f),
                T: new Color(0.216f, 0.949f, 1.000f), S: new Color(1.000f, 0.216f, 0.667f),
                Z: new Color(1.000f, 0.216f, 0.667f), J: new Color(0.216f, 0.949f, 1.000f),
                L: new Color(1.000f, 0.216f, 0.667f),
                BgTop: new Color(0.043f, 0.016f, 0.075f), BgBottom: new Color(0.098f, 0.035f, 0.153f),
                Glyph: SkinGlyph.Bolt, Material: CellMaterial.NeonTube,
                EdgeTint: new Color(1.000f, 0.400f, 0.850f, 0.55f),
                Plan: ColorPlan.Duo, Signature: BurstArtifact.Lightning,
                Scene: BackdropStyle.Grid,
                Accent: new Color(1.00f, 0.35f, 0.80f), Accent2: new Color(0.30f, 0.92f, 1.00f)),
        },
        new()
        {
            Id = "theme_deep_signal", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "DEEP SIGNAL", Blurb = "BRUSHED METAL ON A LIVE BOARD. THE TRACES ARE STILL CARRYING SOMETHING.",
            PriceLabel = "FREE", IsNew = true,
            Theme = new BlockTheme("theme_deep_signal",
                I: new Color(0.204f, 0.855f, 0.749f), O: new Color(0.478f, 0.878f, 0.298f),
                T: new Color(0.478f, 0.878f, 0.298f), S: new Color(0.145f, 0.420f, 0.478f),
                Z: new Color(0.145f, 0.420f, 0.478f), J: new Color(0.204f, 0.855f, 0.749f),
                L: new Color(0.478f, 0.878f, 0.298f),
                BgTop: new Color(0.008f, 0.043f, 0.047f), BgBottom: new Color(0.020f, 0.086f, 0.094f),
                Glyph: SkinGlyph.Gem, Material: CellMaterial.Metallic,
                EdgeTint: new Color(0.350f, 1.000f, 0.850f, 0.70f),
                Plan: ColorPlan.Trio, Signature: BurstArtifact.Glitch,
                Scene: BackdropStyle.Circuit,
                Accent: new Color(0.20f, 0.90f, 0.78f), Accent2: new Color(0.60f, 0.95f, 0.35f)),
        },
        new()
        {
            Id = "theme_hanami", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "HANAMI", Blurb = "PEARL BLOSSOMS UNDER DRIFTING LIGHTS. NOTHING HERE IS IN A HURRY.",
            PriceLabel = "FREE", IsNew = true,
            Theme = new BlockTheme("theme_hanami",
                I: new Color(0.600f, 0.900f, 1.000f), O: new Color(1.000f, 0.900f, 0.650f),
                T: new Color(1.000f, 0.700f, 0.900f), S: new Color(0.750f, 0.950f, 0.750f),
                Z: new Color(1.000f, 0.600f, 0.720f), J: new Color(0.720f, 0.780f, 1.000f),
                L: new Color(1.000f, 0.800f, 0.700f),
                BgTop: new Color(0.075f, 0.035f, 0.055f), BgBottom: new Color(0.145f, 0.075f, 0.110f),
                Glyph: SkinGlyph.Flower, Material: CellMaterial.Pearl,
                Signature: BurstArtifact.Petals,
                Scene: BackdropStyle.Bokeh,
                Accent: new Color(1.00f, 0.62f, 0.80f), Accent2: new Color(0.78f, 0.60f, 1.00f)),
        },
        new()
        {
            Id = "theme_monsoon", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "MONSOON", Blurb = "ONE STEEL INK, SEVEN RUNGS, AND RAIN THAT DOES NOT LET UP.",
            PriceLabel = "FREE",
            Theme = new BlockTheme("theme_monsoon",
                I: new Color(0.780f, 0.847f, 0.918f), O: new Color(0.714f, 0.784f, 0.859f),
                T: new Color(0.647f, 0.722f, 0.800f), S: new Color(0.580f, 0.659f, 0.741f),
                Z: new Color(0.510f, 0.596f, 0.682f), J: new Color(0.443f, 0.533f, 0.624f),
                L: new Color(0.376f, 0.471f, 0.565f),
                BgTop: new Color(0.016f, 0.027f, 0.043f), BgBottom: new Color(0.039f, 0.063f, 0.098f),
                Material: CellMaterial.Frosted,
                Plan: ColorPlan.Mono, Signature: BurstArtifact.Ink,
                Scene: BackdropStyle.Rain,
                Accent: new Color(0.52f, 0.72f, 0.95f), Accent2: new Color(0.45f, 0.52f, 0.88f)),
        },
        new()
        {
            Id = "theme_forge", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "FORGE", Blurb = "HOT METAL, COOLING METAL, AND SLAG. IT THROWS SPARKS WHEN IT BREAKS.",
            PriceLabel = "FREE",
            Theme = new BlockTheme("theme_forge",
                I: new Color(1.000f, 0.816f, 0.400f), O: new Color(0.878f, 0.318f, 0.098f),
                T: new Color(0.878f, 0.318f, 0.098f), S: new Color(0.310f, 0.267f, 0.278f),
                Z: new Color(0.310f, 0.267f, 0.278f), J: new Color(1.000f, 0.816f, 0.400f),
                L: new Color(0.878f, 0.318f, 0.098f),
                BgTop: new Color(0.062f, 0.031f, 0.016f), BgBottom: new Color(0.125f, 0.059f, 0.027f),
                Material: CellMaterial.Metallic,
                EdgeTint: new Color(1.000f, 0.550f, 0.180f, 0.60f),
                Plan: ColorPlan.Trio, Signature: BurstArtifact.Shards,
                Scene: BackdropStyle.Embers,
                Accent: new Color(1.00f, 0.55f, 0.20f), Accent2: new Color(1.00f, 0.30f, 0.18f)),
        },
        new()
        {
            Id = "theme_tundra", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "TUNDRA", Blurb = "ONE PALE ICE UNDER A HARD SKY. IT SHATTERS INTO SNOW.",
            PriceLabel = "FREE",
            Theme = new BlockTheme("theme_tundra",
                I: new Color(0.878f, 0.941f, 1.000f), O: new Color(0.816f, 0.886f, 0.965f),
                T: new Color(0.749f, 0.827f, 0.925f), S: new Color(0.682f, 0.769f, 0.882f),
                Z: new Color(0.612f, 0.710f, 0.839f), J: new Color(0.545f, 0.651f, 0.796f),
                L: new Color(0.478f, 0.592f, 0.753f),
                BgTop: new Color(0.020f, 0.031f, 0.055f), BgBottom: new Color(0.047f, 0.075f, 0.125f),
                Glyph: SkinGlyph.Moon, Material: CellMaterial.Frosted,
                Plan: ColorPlan.Mono, Signature: BurstArtifact.Frost,
                Scene: BackdropStyle.Starfield,
                Accent: new Color(0.62f, 0.85f, 1.00f), Accent2: new Color(0.78f, 0.78f, 1.00f)),
        },
        new()
        {
            Id = "theme_aether", ProductId = "", Kind = StoreItemKind.Theme,
            Name = "AETHER", Blurb = "THE BOARD IS ONE PANE OF GLASS HELD UP TO A NEBULA.",
            PriceLabel = "FREE",
            Theme = new BlockTheme("theme_aether",
                I: new Color(0.450f, 0.950f, 1.000f), O: new Color(0.700f, 0.700f, 1.000f),
                T: new Color(0.850f, 0.450f, 1.000f), S: new Color(0.550f, 0.800f, 1.000f),
                Z: new Color(0.750f, 0.600f, 1.000f), J: new Color(0.500f, 0.880f, 1.000f),
                L: new Color(0.800f, 0.520f, 1.000f),
                BgTop: new Color(0.035f, 0.020f, 0.075f), BgBottom: new Color(0.082f, 0.047f, 0.157f),
                Glyph: SkinGlyph.Sparkle, Material: CellMaterial.Holographic,
                EdgeTint: new Color(1.000f, 0.850f, 1.000f, 0.40f),
                Plan: ColorPlan.BoardGradient, Signature: BurstArtifact.PrismBloom,
                Scene: BackdropStyle.Nebula,
                Accent: new Color(0.85f, 0.50f, 1.00f), Accent2: new Color(0.40f, 0.92f, 1.00f)),
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
        // The three animal-set signatures. Free and separately equippable on purpose: a set
        // equips its own burst as a convenience, never as a lock.
        new()
        {
            Id = "artifact_fluff", ProductId = "", Kind = StoreItemKind.Artifact,
            Name = "FLUFF", Blurb = "SOFT TUFTS DRIFT UP AND SETTLE. NO FLASH, NO GLARE.",
            PriceLabel = "FREE",
        },
        new()
        {
            Id = "artifact_splash", ProductId = "", Kind = StoreItemKind.Artifact,
            Name = "SPLASH", Blurb = "A COLUMN OF WATER, TWO RIPPLES, THEN FALLING DROPS.",
            PriceLabel = "FREE",
        },
        new()
        {
            Id = "artifact_swarm", ProductId = "", Kind = StoreItemKind.Artifact,
            Name = "SWARM", Blurb = "THE SHELL CRACKS ON ONE RING AND SCATTERS.",
            PriceLabel = "FREE",
        },
        // The four SCENE-SET signatures. Same rule as every burst before them: a set equips its
        // own as a convenience, and it stays a free row the player can change back.
        new()
        {
            Id = "artifact_glitch", ProductId = "", Kind = StoreItemKind.Artifact,
            Name = "GLITCH", Blurb = "THE LINE TEARS SIDEWAYS IN RGB SLICES AND SNAPS BACK.",
            PriceLabel = "FREE", IsNew = true,
        },
        new()
        {
            Id = "artifact_petals", ProductId = "", Kind = StoreItemKind.Artifact,
            Name = "PETALS", Blurb = "BLOSSOMS LIFT OFF, TURN ONCE IN THE AIR, AND FALL.",
            PriceLabel = "FREE", IsNew = true,
        },
        new()
        {
            Id = "artifact_frost", ProductId = "", Kind = StoreItemKind.Artifact,
            Name = "FROST", Blurb = "CRYSTALS GROW ALONG THE LINE, THEN BURST INTO SNOW.",
            PriceLabel = "FREE", IsNew = true,
        },
        new()
        {
            Id = "artifact_ink", ProductId = "", Kind = StoreItemKind.Artifact,
            Name = "INK", Blurb = "A DARK BLOOM SPREADS, THROWS DROPLETS, AND RUNS DOWN.",
            PriceLabel = "FREE", IsNew = true,
        },
        // ---- Backdrop scenes -------------------------------------------------
        // The newest axis, and the one with the most screen area: the procedural scene behind
        // every menu AND behind the play field. A scene carries FORM AND MOTION only and takes
        // its palette from the equipped skin, so ten rows here multiply against every skin in
        // the shelf above instead of adding ten wallpapers. That is also why they are free:
        // their value is combinatorial, and a paywall on the combinatorial axis makes the skins
        // the player already owns worth less.
        //
        // Ids are the contract with Blockfall.Theme.Backdrops.FromId — an id typo here does not
        // fail the build, it silently equips Aurora.
        new()
        {
            Id = Backdrops.DefaultId, ProductId = "", Kind = StoreItemKind.Backdrop,
            Name = "AURORA", Blurb = "THE ORIGINAL DRIFTING LIGHTS. ALWAYS YOURS.",
            PriceLabel = "FREE",
        },
        new()
        {
            Id = "scene_nebula", ProductId = "", Kind = StoreItemKind.Backdrop,
            Name = "NEBULA", Blurb = "SLOW CLOUDS OF GAS, LIT FROM SOMEWHERE INSIDE.",
            PriceLabel = "FREE", IsNew = true,
        },
        new()
        {
            Id = "scene_starfield", ProductId = "", Kind = StoreItemKind.Backdrop,
            Name = "STARFIELD", Blurb = "THREE LAYERS OF STARS. THE NEAR ONES ARE IN A HURRY.",
            PriceLabel = "FREE", IsNew = true,
        },
        new()
        {
            Id = "scene_grid", ProductId = "", Kind = StoreItemKind.Backdrop,
            Name = "NIGHT GRID", Blurb = "A LIT FLOOR RUNNING OUT TO A BURNING HORIZON.",
            PriceLabel = "FREE", IsNew = true,
        },
        new()
        {
            Id = "scene_rays", ProductId = "", Kind = StoreItemKind.Backdrop,
            Name = "SUNSHAFT", Blurb = "A SLOW FAN OF LIGHT FROM SOMETHING ABOVE THE SCREEN.",
            PriceLabel = "FREE",
        },
        new()
        {
            Id = "scene_waves", ProductId = "", Kind = StoreItemKind.Backdrop,
            Name = "SWELL", Blurb = "FOUR LONG WAVES ROLLING UNDER THE BOARD.",
            PriceLabel = "FREE",
        },
        new()
        {
            Id = "scene_bokeh", ProductId = "", Kind = StoreItemKind.Backdrop,
            Name = "BOKEH", Blurb = "OUT-OF-FOCUS LIGHTS DRIFTING UP PAST THE CAMERA.",
            PriceLabel = "FREE",
        },
        new()
        {
            Id = "scene_rain", ProductId = "", Kind = StoreItemKind.Backdrop,
            Name = "DOWNPOUR", Blurb = "SLANTED RAIN AND A WET SHEEN POOLING AT THE BOTTOM.",
            PriceLabel = "FREE",
        },
        new()
        {
            Id = "scene_embers", ProductId = "", Kind = StoreItemKind.Backdrop,
            Name = "CINDERS", Blurb = "WARM SPARKS RISING OFF A FIRE YOU CANNOT SEE.",
            PriceLabel = "FREE",
        },
        new()
        {
            Id = "scene_circuit", ProductId = "", Kind = StoreItemKind.Backdrop,
            Name = "TRACE", Blurb = "COPPER RUNS AND TRAVELLING PULSES. THE SCREEN IS THE CHIP.",
            PriceLabel = "FREE",
        },
        // ---- Placement sound packs -----------------------------------------
        // Priced at nothing on purpose. MobilePlatform.PurchaseItem currently calls
        // onComplete(true) WITHOUT a billing plugin (Platforms.cs) — the day a real
        // plugin lands, every paid SKU behind that path is a grant-for-free bug, and
        // we are not stacking another SKU on top of that mine. Empty ProductId ⇒
        // owned by default (SaveManager.OwnsItem), so no purchase path is involved
        // at all. Equipping writes GameSettings.SfxPack — the SAME setting the
        // Settings › AUDIO picker owns, so the store is a second front door to one
        // value, never a second source of truth. StoreScreen's SOUND section renders
        // these (see StoreScreen.SoundRow); SoundPackIndex is what it writes.
        // None carries IsNew: the packs themselves have shipped in Settings since
        // v1.6 — only their shelf is new, and a badge on all five would say nothing.
        new()
        {
            Id = "sound_neon", ProductId = "", Kind = StoreItemKind.SoundPack,
            Name = "NEON", Blurb = "THE ORIGINAL LOCK TONE. ALWAYS YOURS.",
            PriceLabel = "FREE", SoundPackIndex = 0,
        },
        new()
        {
            Id = "sound_tap", ProductId = "", Kind = StoreItemKind.SoundPack,
            Name = "TAP", Blurb = "SOFT RUBBER DOME. QUIET DESK, LATE NIGHT.",
            PriceLabel = "FREE", SoundPackIndex = 1,
        },
        new()
        {
            Id = "sound_click", ProductId = "", Kind = StoreItemKind.SoundPack,
            Name = "CLICK", Blurb = "CLICKY SWITCH. TWO SNAPS PER PLACE.",
            PriceLabel = "FREE", SoundPackIndex = 2,
        },
        new()
        {
            Id = "sound_typebar", ProductId = "", Kind = StoreItemKind.SoundPack,
            Name = "TYPEBAR", Blurb = "STEEL HAMMER ON A PLATEN.",
            PriceLabel = "FREE", SoundPackIndex = 3,
        },
        new()
        {
            Id = "sound_wood", ProductId = "", Kind = StoreItemKind.SoundPack,
            Name = "WOOD", Blurb = "MALLET ON A WOODEN BAR. WARM AND DRY.",
            PriceLabel = "FREE", SoundPackIndex = 4,
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
