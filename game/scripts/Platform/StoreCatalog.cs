using Godot;
using Blockfall.Theme;

namespace Blockfall.Platform;

public enum StoreItemKind
{
    Theme,       // permanent cosmetic (block palette + backdrop)
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
            Id = "theme_sunset_drive", ProductId = "com.blockfall.theme.sunset", Kind = StoreItemKind.Theme,
            Name = "SUNSET DRIVE", Blurb = "WARM DUSK PINKS OVER A PURPLE HORIZON.",
            PriceLabel = "US$1.99",
            Theme = new BlockTheme("theme_sunset_drive",
                I: new Color(0.30f, 0.89f, 0.89f), O: new Color(1.00f, 0.82f, 0.30f),
                T: new Color(1.00f, 0.48f, 0.78f), S: new Color(0.50f, 0.90f, 0.39f),
                Z: new Color(1.00f, 0.36f, 0.36f), J: new Color(0.49f, 0.55f, 1.00f),
                L: new Color(1.00f, 0.58f, 0.25f),
                BgTop: new Color(0.094f, 0.039f, 0.118f), BgBottom: new Color(0.200f, 0.063f, 0.180f)),
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
                BgTop: new Color(0.016f, 0.078f, 0.055f), BgBottom: new Color(0.043f, 0.165f, 0.118f)),
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
                BgTop: new Color(0.039f, 0.039f, 0.055f), BgBottom: new Color(0.102f, 0.102f, 0.141f)),
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
