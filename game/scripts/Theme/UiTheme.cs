using Godot;

namespace Blockfall.Theme;

/// <summary>
/// The single shared <see cref="Theme"/> for every Control in the game, built in
/// code from the design tokens. Theme propagation only flows down Control trees
/// and our screen roots hang under Node2D parents, so every root Control must go
/// through <see cref="ApplyTo"/> (screens, overlays, touch pads — no exceptions,
/// or that subtree silently reverts to the stock gray theme).
///
/// Type variations (set via <c>Control.ThemeTypeVariation</c>):
///   Button:  "PrimaryButton" (accent-filled), "GhostButton" (outline),
///            "ChipButton" (pill toggle), "CardButton" (glass card row)
///   Label:   "TitleLabel", "HeadlineLabel", "StatValueLabel",
///            "SectionLabel", "DimLabel", and the scale-aware settings set
///            "SectionHeaderLabel"/"OptionLabel"/"OptionValue"/"OptionHint"
///   Panel/PanelContainer: "Card" (glass card surface)
/// Typography rule: Orbitron only via the *Title/Headline/StatValue* variations;
/// everything else is Rajdhani, uppercase.
/// </summary>
public static class UiTheme
{
    public static Godot.Theme Shared { get; private set; } = null!;

    // ---- "LIT GLASS" surface tokens (ADDITIVE — the Palette.Glass* set is untouched) ----
    // Measured against the background midpoint (#0A0B14 → #16182E), the shipped glass
    // recipe puts a card body at 1.168:1 and its border at 1.300:1 — i.e. the card is the
    // same luminance as the sky, so at 50% screen brightness the surface disappears and
    // only the text and the 4px accent bar survive. These raise the surface plane to
    // 1.635:1 (body) and 3.092:1 (border, clearing the WCAG 3:1 non-text UI bar) while
    // keeping the gradient direction (top brighter) and the bg-hue tint.
    // Text on the lit card still improves: TextPrimary 11.35:1, TextSecondary 5.41:1.
    // Field-tuning floor: SurfaceBorder alpha may drop to 0.28 (2.48:1) if a device reads
    // harsh — below that the original problem is back.
    // Consumers (a token with none is a lie about what the screen renders): the "Card"
    // PanelContainer variation in BuildPanels uses all four verbatim; MainMenu.Card uses
    // Top/Bottom RGB-tinted toward each card's identity colour.
    public static readonly Color SurfaceTop = new(0.72f, 0.76f, 1f, 0.22f);
    public static readonly Color SurfaceBottom = new(0.60f, 0.64f, 0.95f, 0.07f);
    public static readonly Color SurfaceBorder = new(1f, 1f, 1f, 0.34f);
    public static readonly Color SurfaceHighlight = new(1f, 1f, 1f, 0.22f);

    /// <summary>Accessibility text scale applied to every themed font size (1.0 = design size).</summary>
    private static float _scale = 1f;

    /// <summary>Scale a design font size by the current accessibility text scale.</summary>
    private static int Fs(int size) => Mathf.Max(1, Mathf.RoundToInt(size * _scale));

    public static void ApplyTo(Control root) => root.Theme = Shared;

    /// <summary>Call once from Bootstrap, after <see cref="Fonts.Init"/>.</summary>
    public static void Init() => Init(1f);

    /// <summary>Build the shared theme at the given text scale.</summary>
    public static void Init(float scale)
    {
        _scale = scale;
        // Reuse the SAME Theme instance across rebuilds so a live text-size change
        // propagates to every Control already pointing at Shared (no tree walk).
        var t = Shared ??= new Godot.Theme();
        t.DefaultFont = Fonts.Ui;
        t.DefaultFontSize = Fs(18);

        BuildButton(t);
        BuildLabels(t);
        BuildLineEdit(t);
        BuildSlider(t);
        BuildCheckBox(t);
        BuildPanels(t);
    }

    /// <summary>Re-apply the theme at a new accessibility text scale (live). Clamped to a sane range.</summary>
    public static void SetScale(float scale) => Init(Mathf.Clamp(scale, 0.8f, 1.6f));

    /// <summary>
    /// Re-bake every stylebox at the CURRENT text scale. Call after <see cref="Palette.Accent"/>
    /// changes (i.e. after equipping a skin) so hovers, focus rings, chip toggles, primary-button
    /// fills, slider grabbers and caret colours all move to the new accent.
    ///
    /// <para>Why this is cheap and safe: <see cref="Init"/> mutates the ONE shared
    /// <see cref="Shared"/> instance in place, so every Control already pointing at it updates
    /// with no tree walk (the same mechanism the live TEXT SIZE slider rides). And
    /// <see cref="TextureFactory"/> keys its bake cache on the colours, so a new accent produces
    /// new entries rather than returning stale ones — the cost is ~20 baked 48×48 textures per
    /// distinct accent the player equips in a session, once each.</para>
    ///
    /// <para>Must NOT be <c>Init()</c> — that overload resets the scale to 1.0 and would silently
    /// undo the player's accessibility setting on every equip.</para>
    /// </summary>
    public static void Rebuild() => Init(_scale);

    // ---- Buttons -----------------------------------------------------------

    private static void BuildButton(Godot.Theme t)
    {
        // Base glass button. Pressed is the STRONG state (mobile-first feedback).
        var normal = TextureFactory.GlassStyle(Palette.RadiusM, Palette.GlassTop, Palette.GlassBottom, Palette.GlassBorder, 1f, 0.16f, 24, 13);
        var hover = TextureFactory.GlassStyle(Palette.RadiusM,
            Brighter(Palette.GlassTop, 1.6f), Brighter(Palette.GlassBottom, 1.6f),
            WithAlpha(Palette.Accent, 0.55f), 1.2f, 0.20f, 24, 13);
        var pressed = TextureFactory.GlassStyle(Palette.RadiusM,
            WithAlpha(Palette.Accent, 0.22f), WithAlpha(Palette.Accent, 0.10f),
            WithAlpha(Palette.Accent, 0.85f), 1.4f, 0.10f, 24, 13);
        var disabled = TextureFactory.GlassStyle(Palette.RadiusM,
            WithAlpha(Palette.GlassTop, 0.04f), WithAlpha(Palette.GlassBottom, 0.015f),
            WithAlpha(Palette.GlassBorder, 0.05f), 1f, 0.04f, 24, 13);
        var focus = TextureFactory.GlassStyle(Palette.RadiusM,
            new Color(0, 0, 0, 0), new Color(0, 0, 0, 0),
            WithAlpha(Palette.Accent, 0.8f), 1.6f, 0f, 24, 13);
        focus.DrawCenter = false;

        t.SetStylebox("normal", "Button", normal);
        t.SetStylebox("hover", "Button", hover);
        t.SetStylebox("pressed", "Button", pressed);
        t.SetStylebox("disabled", "Button", disabled);
        t.SetStylebox("focus", "Button", focus);
        t.SetFont("font", "Button", Fonts.Ui);
        t.SetFontSize("font_size", "Button", Fs(21));
        t.SetColor("font_color", "Button", Palette.TextPrimary);
        t.SetColor("font_hover_color", "Button", Colors.White);
        t.SetColor("font_pressed_color", "Button", Colors.White);
        t.SetColor("font_focus_color", "Button", Colors.White);
        t.SetColor("font_disabled_color", "Button", Palette.TextTertiary);
        t.SetColor("icon_normal_color", "Button", Palette.TextSecondary);
        t.SetColor("icon_hover_color", "Button", Palette.TextPrimary);
        t.SetColor("icon_pressed_color", "Button", Palette.TextPrimary);

        // Primary: accent-filled, dark ink. The one loud button per screen.
        t.SetTypeVariation("PrimaryButton", "Button");
        var pNormal = FilledStyle(Palette.Accent);
        var pHover = FilledStyle(Brighten(Palette.Accent, 0.12f));
        // Pressed = pushed in: slightly darker fill + the label sinks ~3px (content
        // margins shift while total height stays constant) for a tactile candy press.
        var pPressed = FilledStyle(Brighten(Palette.Accent, -0.08f));
        pPressed.ContentMarginTop = 16; pPressed.ContentMarginBottom = 10;
        t.SetStylebox("normal", "PrimaryButton", pNormal);
        t.SetStylebox("hover", "PrimaryButton", pHover);
        t.SetStylebox("pressed", "PrimaryButton", pPressed);
        t.SetColor("font_color", "PrimaryButton", Palette.InkOnAccent);
        t.SetColor("font_hover_color", "PrimaryButton", Palette.InkOnAccent);
        t.SetColor("font_pressed_color", "PrimaryButton", Palette.InkOnAccent);
        t.SetColor("font_focus_color", "PrimaryButton", Palette.InkOnAccent);
        t.SetFontSize("font_size", "PrimaryButton", Fs(22));

        // Ghost: quiet outline — secondary navigation.
        t.SetTypeVariation("GhostButton", "Button");
        var gNormal = TextureFactory.GlassStyle(Palette.RadiusM,
            new Color(0, 0, 0, 0), new Color(0, 0, 0, 0),
            WithAlpha(Colors.White, 0.14f), 1f, 0f, 24, 12);
        var gHover = TextureFactory.GlassStyle(Palette.RadiusM,
            WithAlpha(Palette.Accent, 0.06f), WithAlpha(Palette.Accent, 0.02f),
            WithAlpha(Palette.Accent, 0.55f), 1.2f, 0f, 24, 12);
        t.SetStylebox("normal", "GhostButton", gNormal);
        t.SetStylebox("hover", "GhostButton", gHover);
        t.SetColor("font_color", "GhostButton", Palette.TextSecondary);
        t.SetFontSize("font_size", "GhostButton", Fs(19));

        // Chip: pill toggle for modifiers. Toggled-on = pressed state.
        t.SetTypeVariation("ChipButton", "Button");
        const int pill = 19; // pill look at the 44px chip height
        var cNormal = TextureFactory.GlassStyle(pill, Palette.GlassTop, Palette.GlassBottom, Palette.GlassBorder, 1f, 0.10f, 16, 8);
        var cHover = TextureFactory.GlassStyle(pill, Brighter(Palette.GlassTop, 1.5f), Brighter(Palette.GlassBottom, 1.5f), WithAlpha(Palette.Accent, 0.4f), 1f, 0.12f, 16, 8);
        var cOn = TextureFactory.GlassStyle(pill, WithAlpha(Palette.Accent, 0.26f), WithAlpha(Palette.Accent, 0.14f), WithAlpha(Palette.Accent, 0.9f), 1.3f, 0.10f, 16, 8);
        t.SetStylebox("normal", "ChipButton", cNormal);
        t.SetStylebox("hover", "ChipButton", cHover);
        t.SetStylebox("pressed", "ChipButton", cOn);
        t.SetFontSize("font_size", "ChipButton", Fs(15));
        t.SetColor("font_color", "ChipButton", Palette.TextSecondary);
        t.SetColor("font_pressed_color", "ChipButton", Palette.Accent);
        t.SetColor("font_hover_color", "ChipButton", Palette.TextPrimary);

        // Card: mode-select rows/tiles — glass card that lights up on hover.
        t.SetTypeVariation("CardButton", "Button");
        var cardNormal = TextureFactory.GlassStyle(Palette.RadiusM, Palette.GlassTop, Palette.GlassBottom, Palette.GlassBorder, 1f, 0.16f, 18, 12);
        var cardHover = TextureFactory.GlassStyle(Palette.RadiusM,
            Brighter(Palette.GlassTop, 1.7f), Brighter(Palette.GlassBottom, 1.7f),
            WithAlpha(Palette.Accent, 0.6f), 1.3f, 0.2f, 18, 12);
        var cardPressed = TextureFactory.GlassStyle(Palette.RadiusM,
            WithAlpha(Palette.Accent, 0.18f), WithAlpha(Palette.Accent, 0.08f),
            WithAlpha(Palette.Accent, 0.9f), 1.4f, 0.12f, 18, 12);
        t.SetStylebox("normal", "CardButton", cardNormal);
        t.SetStylebox("hover", "CardButton", cardHover);
        t.SetStylebox("pressed", "CardButton", cardPressed);
    }

    // ---- Labels --------------------------------------------------------------

    private static void BuildLabels(Godot.Theme t)
    {
        t.SetColor("font_color", "Label", Palette.TextPrimary);
        t.SetFont("font", "Label", Fonts.Ui);
        t.SetFontSize("font_size", "Label", Fs(18));

        t.SetTypeVariation("TitleLabel", "Label");
        t.SetFont("font", "TitleLabel", Fonts.Display);
        t.SetFontSize("font_size", "TitleLabel", Fs(44));
        t.SetColor("font_color", "TitleLabel", Palette.TextPrimary);

        t.SetTypeVariation("HeadlineLabel", "Label");
        t.SetFont("font", "HeadlineLabel", Fonts.Display);
        t.SetFontSize("font_size", "HeadlineLabel", Fs(58));
        t.SetColor("font_color", "HeadlineLabel", Palette.TextPrimary);

        t.SetTypeVariation("StatValueLabel", "Label");
        t.SetFont("font", "StatValueLabel", Fonts.DisplayMedium);
        t.SetFontSize("font_size", "StatValueLabel", Fs(28));
        t.SetColor("font_color", "StatValueLabel", Palette.TextPrimary);

        t.SetTypeVariation("SectionLabel", "Label");
        t.SetFont("font", "SectionLabel", Fonts.UiTracked);
        t.SetFontSize("font_size", "SectionLabel", Fs(14));
        t.SetColor("font_color", "SectionLabel", Palette.TextTertiary);

        t.SetTypeVariation("DimLabel", "Label");
        t.SetColor("font_color", "DimLabel", Palette.TextSecondary);
        t.SetFontSize("font_size", "DimLabel", Fs(16));

        // Settings option labels — routed through the theme (Fs) so they honor the
        // live TEXT SIZE accessibility slider; a per-node font_size override would
        // pin them and opt out of SetScale's in-place propagation. See SettingsScreen.
        t.SetTypeVariation("SectionHeaderLabel", "Label");
        t.SetFont("font", "SectionHeaderLabel", Fonts.UiTracked);
        t.SetFontSize("font_size", "SectionHeaderLabel", Fs(16));
        t.SetColor("font_color", "SectionHeaderLabel", Palette.TextSecondary);

        t.SetTypeVariation("OptionLabel", "Label");
        t.SetFontSize("font_size", "OptionLabel", Fs(18));
        t.SetColor("font_color", "OptionLabel", Palette.TextPrimary);

        t.SetTypeVariation("OptionValue", "Label");
        t.SetFont("font", "OptionValue", Fonts.UiBold);
        t.SetFontSize("font_size", "OptionValue", Fs(18));
        t.SetColor("font_color", "OptionValue", Palette.Accent);

        t.SetTypeVariation("OptionHint", "Label");
        t.SetFontSize("font_size", "OptionHint", Fs(14));
        t.SetColor("font_color", "OptionHint", Palette.TextSecondary);
    }

    // ---- LineEdit --------------------------------------------------------------

    private static void BuildLineEdit(Godot.Theme t)
    {
        var normal = TextureFactory.GlassStyle(Palette.RadiusS,
            new Color(0, 0, 0, 0.25f), new Color(0, 0, 0, 0.25f),
            Palette.GlassBorder, 1f, 0.06f, 14, 10);
        var focusSb = TextureFactory.GlassStyle(Palette.RadiusS,
            new Color(0, 0, 0, 0.28f), new Color(0, 0, 0, 0.28f),
            WithAlpha(Palette.Accent, 0.8f), 1.4f, 0.06f, 14, 10);
        t.SetStylebox("normal", "LineEdit", normal);
        t.SetStylebox("focus", "LineEdit", focusSb);
        t.SetFont("font", "LineEdit", Fonts.Ui);
        t.SetFontSize("font_size", "LineEdit", Fs(18));
        t.SetColor("font_color", "LineEdit", Palette.TextPrimary);
        t.SetColor("font_placeholder_color", "LineEdit", Palette.TextTertiary);
        t.SetColor("caret_color", "LineEdit", Palette.Accent);
        t.SetColor("selection_color", "LineEdit", WithAlpha(Palette.Accent, 0.35f));
    }

    // ---- HSlider ---------------------------------------------------------------

    private static void BuildSlider(Godot.Theme t)
    {
        var track = TextureFactory.GlassStyle(3, new Color(0, 0, 0, 0.35f), new Color(0, 0, 0, 0.35f),
            WithAlpha(Colors.White, 0.08f), 1f, 0f, 0, 0);
        track.TextureMarginLeft = 4; track.TextureMarginRight = 4;
        track.TextureMarginTop = 4; track.TextureMarginBottom = 4;

        var fill = new StyleBoxTexture { Texture = TextureFactory.FillBox(3, WithAlpha(Palette.Accent, 0.85f)) };
        fill.TextureMarginLeft = 4; fill.TextureMarginRight = 4;
        fill.TextureMarginTop = 4; fill.TextureMarginBottom = 4;

        t.SetStylebox("slider", "HSlider", track);
        t.SetStylebox("grabber_area", "HSlider", fill);
        t.SetStylebox("grabber_area_highlight", "HSlider", fill);
        t.SetIcon("grabber", "HSlider", TextureFactory.Circle(20, Palette.Accent, Colors.White, 0f));
        t.SetIcon("grabber_highlight", "HSlider", TextureFactory.Circle(24, Brighten(Palette.Accent, 0.15f), Colors.White, 1.5f));
        t.SetIcon("grabber_disabled", "HSlider", TextureFactory.Circle(20, Palette.TextTertiary, Colors.White, 0f));
    }

    // ---- CheckBox ---------------------------------------------------------------

    private static void BuildCheckBox(Godot.Theme t)
    {
        var empty = new StyleBoxEmpty();
        empty.ContentMarginLeft = 4; empty.ContentMarginRight = 4;
        empty.ContentMarginTop = 4; empty.ContentMarginBottom = 4;
        foreach (var state in new[] { "normal", "hover", "pressed", "disabled", "focus", "hover_pressed" })
            t.SetStylebox(state, "CheckBox", empty);
        t.SetIcon("checked", "CheckBox", TextureFactory.CheckIcon(26, true));
        t.SetIcon("unchecked", "CheckBox", TextureFactory.CheckIcon(26, false));
        t.SetIcon("checked_disabled", "CheckBox", TextureFactory.CheckIcon(26, true));
        t.SetIcon("unchecked_disabled", "CheckBox", TextureFactory.CheckIcon(26, false));
        t.SetColor("font_color", "CheckBox", Palette.TextPrimary);
        t.SetFontSize("font_size", "CheckBox", Fs(18));
    }

    // ---- Panels -------------------------------------------------------------------

    private static void BuildPanels(Godot.Theme t)
    {
        // Default Panel/PanelContainer: invisible (screens place explicit cards).
        t.SetStylebox("panel", "Panel", new StyleBoxEmpty());
        t.SetStylebox("panel", "PanelContainer", new StyleBoxEmpty());
        t.SetStylebox("panel", "ScrollContainer", new StyleBoxEmpty());

        // "Card": the glass surface for grouped content — and the ONE consumer of the LIT GLASS
        // tokens, which is the whole reason they exist. Before this, every card on every screen
        // (store rows, settings sections, results, the pause sheet over a live board) drew the
        // Palette.Glass* recipe at 1.168:1 body / 1.300:1 border, i.e. the same luminance as the
        // sky behind it: at half screen brightness the card boundary was simply not there and
        // grouped content read as loose text on the background. SurfaceTop/Bottom/Border raise
        // that to 1.635:1 / 3.092:1 (the WCAG 3:1 non-text UI bar), and SurfaceHighlight sets the
        // 1px inner top edge that keeps the surface reading as glass rather than a flat slab.
        // Palette.Glass* is untouched and still dresses buttons/chips — a card is a PLANE and
        // needs an edge; a button is already identified by its label and its press state.
        var card = TextureFactory.GlassStyle(Palette.RadiusL, SurfaceTop, SurfaceBottom,
            SurfaceBorder, 1f, SurfaceHighlight.A, 24, 20);
        t.SetTypeVariation("Card", "PanelContainer");
        t.SetStylebox("panel", "Card", card);
    }

    /// <summary>9-patch stylebox around an accent-filled rounded box texture.</summary>
    private static StyleBoxTexture FilledStyle(Color fill)
    {
        var sb = new StyleBoxTexture { Texture = TextureFactory.FillBox(Palette.RadiusM, fill) };
        float m = Palette.RadiusM + 3;
        sb.TextureMarginLeft = m; sb.TextureMarginRight = m;
        sb.TextureMarginTop = m; sb.TextureMarginBottom = m;
        sb.ContentMarginLeft = 24; sb.ContentMarginRight = 24;
        sb.ContentMarginTop = 13; sb.ContentMarginBottom = 13;
        return sb;
    }

    // ---- Small color helpers ---------------------------------------------------

    private static Color WithAlpha(Color c, float a) => new(c.R, c.G, c.B, a);
    private static Color Brighter(Color c, float alphaMul) => new(c.R, c.G, c.B, Mathf.Clamp(c.A * alphaMul, 0f, 1f));
    private static Color Brighten(Color c, float add)
        => new(Mathf.Min(1, c.R + add), Mathf.Min(1, c.G + add), Mathf.Min(1, c.B + add), c.A);
}
