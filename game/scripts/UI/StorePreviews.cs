using Godot;
using Blockfall.Core;
using Blockfall.Gameplay;
using Blockfall.Theme;

namespace Blockfall.UI;

/// <summary>
/// A live skin preview: a 3×3 slice of a real board rendered through the shared
/// <see cref="BlockRender"/> with the theme's actual material + glyph + edge tint + colour
/// plan, breathing on the same shimmer clock the board uses — so what you see in the shop is
/// exactly what you wear. Renders explicit theme colours (not the equipped palette), so it
/// previews any skin without disturbing the live board behind the store.
///
/// Two deliberate choices, both fixing a shop-vs-play mismatch:
///   • CELL 38px, not 24–30. BlockRender sheds layers below hard size guards (glow &lt;16,
///     overlay &lt;14, rim &lt;16) — the old strip fell under them, so a metallic or frosted
///     skin previewed as a flat tile. 38 clears every material guard. Only the glyph DETAIL
///     guard (26) is deliberately left in play: the stamp is a watermark here, not the sell.
///   • A GRID, not a strip. A <see cref="ColorPlan"/> (Mono / Duo / Trio / BoardGradient)
///     lives in the RELATIONSHIP between neighbouring cells; six isolated swatches literally
///     cannot show it.
/// Colours come from <see cref="Palette.PlanFill"/>, which shows the theme's OWN hues even under
/// colorblind mode — the board stays Okabe–Ito, the showcase stays shoppable (see PlanFill's note).
/// Reduced motion → a static poster frame (shimmer frozen, no _Process work).
/// </summary>
public partial class ThemePreview : Control
{
    private const float Cell = 38f;
    private const int Span = 3;

    // An L-tromino plus two loose cells — enough adjacency for a plan to read, enough gaps
    // for the empty-cell outline (identical to the game board) to frame it.
    private static readonly (int R, int C, PieceType T)[] Layout =
    {
        (0, 0, PieceType.I), (1, 0, PieceType.O), (1, 1, PieceType.T),
        (0, 2, PieceType.S), (2, 1, PieceType.Z),
    };

    /// <summary>The diagonal range actually occupied by <see cref="Layout"/> (max row+col). A
    /// positional plan normalises against THIS, not the 10-wide play field: with the board
    /// denominator (2·9 = 18) this card's largest diagonal (3) reached mix 0.167, so DICHROIC —
    /// the one US$2.99 skin whose entire pitch is "cyan at one corner, violet at the other" —
    /// previewed with not a single violet pixel. Ramping across the card's own range makes the
    /// swatch the WHOLE gradient: first cell pure A, last cell pure B, exactly what is being sold.
    /// Declared after <see cref="Layout"/> on purpose — static initialisers run in textual order.</summary>
    private static readonly float LayoutDiagRange = ComputeDiagRange();

    private static float ComputeDiagRange()
    {
        int max = 1;
        foreach (var (r, c, _) in Layout) if (r + c > max) max = r + c;
        return max;
    }

    private readonly BlockTheme _theme;
    private float _shimmer;

    /// <summary>When set, frames the tile with an accent halo (the equipped skin).</summary>
    public bool Selected { get; init; }

    public ThemePreview(BlockTheme theme)
    {
        _theme = theme;
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(Span * Cell + 8f, Span * Cell + 8f);
    }

    public override void _Process(double delta)
    {
        if (Motion.Reduced || !IsVisibleInTree()) return;
        // Nine material stacks × a 40-row catalog is a lot to animate off-screen; only tick
        // when actually scrolled into view (same guard ArtifactPreview uses).
        if (!GetGlobalRect().Intersects(GetViewportRect())) return;
        _shimmer += (float)delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Size.X <= 0f) return;
        bool reduced = Motion.Reduced;
        var origin = new Vector2(4f, 4f);

        // Empty cells first — the same faint outline the Block Fit board draws.
        for (int r = 0; r < Span; r++)
            for (int c = 0; c < Span; c++)
            {
                bool filled = false;
                foreach (var (lr, lc, _) in Layout) if (lr == r && lc == c) { filled = true; break; }
                if (filled) continue;
                DrawRect(new Rect2(origin + new Vector2(c * Cell, r * Cell) + new Vector2(1, 1),
                                   new Vector2(Cell - 2, Cell - 2)),
                         new Color(1, 1, 1, 0.045f), filled: false, width: 1f);
            }

        foreach (var (r, c, type) in Layout)
        {
            var rect = new Rect2(origin + new Vector2(c * Cell, r * Cell) + new Vector2(1, 1),
                                 new Vector2(Cell - 2, Cell - 2));
            // The plan is resolved against THIS theme, not the equipped palette — the shop
            // must never mutate what the board behind it is wearing. Normalised to the CARD's
            // diagonal range so a positional plan shows its full A→B sweep at 3×3.
            var fill = Palette.PlanFill(_theme, type, r, c, LayoutDiagRange);
            BlockRender.DrawCell(this, rect, Cell, fill, Palette.Emissive(fill, 2.0f), 1f,
                                 _theme.Material, _theme.EdgeTint, _theme.Glyph, _shimmer, r + c,
                                 drawGlyph: true, reduced: reduced);
        }
        if (Selected) DrawSelectionHalo(this, Size);
    }

    /// <summary>An accent frame marking the equipped cosmetic — drawn INSIDE the bounds so it
    /// survives ClipContents. Shared by both previews.</summary>
    internal static void DrawSelectionHalo(CanvasItem ci, Vector2 size)
    {
        var a = Palette.Accent;
        ci.DrawRect(new Rect2(1.5f, 1.5f, size.X - 3f, size.Y - 3f), new Color(a.R, a.G, a.B, 0.85f), filled: false, width: 2f);
        ci.DrawRect(new Rect2(3.5f, 3.5f, size.X - 7f, size.Y - 7f), new Color(a.R, a.G, a.B, 0.22f), filled: false, width: 1f);
    }
}

/// <summary>
/// A live artifact preview: a mini board that auto-loops the REAL line-clear burst through the
/// shared <see cref="BurstEngine"/>, so browsing the shop shows each artifact's signature instead
/// of a flat icon. Same engine as gameplay ⇒ preview and play cannot drift. Local additive child
/// for the glow; never touches the global background pulse. Reduced motion → a static poster.
/// </summary>
public partial class ArtifactPreview : Control
{
    private readonly BurstArtifact _art;
    private readonly BurstEngine _burst = new();
    private AdditiveFxLayer _add = null!;
    private float _fireTimer = 0.15f;   // first burst shortly after it appears
    private const float Loop = 1.6f;
    private const int N = 5;
    private const float Cell = 16f;

    private static readonly Color[] Demo =
    {
        new(1f, 0.45f, 0.55f), new(1f, 0.85f, 0.35f), new(0.45f, 0.96f, 0.62f),
        new(0.36f, 0.85f, 1f), new(0.78f, 0.48f, 1f),
    };

    /// <summary>When set, frames the card with an accent halo (the equipped artifact).</summary>
    public bool Selected { get; init; }

    public ArtifactPreview(BurstArtifact art)
    {
        _art = art;
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;
        CustomMinimumSize = new Vector2(150, 92);
    }

    public override void _Ready()
    {
        _add = new AdditiveFxLayer(_burst, () => Cell, () => new Rect2()) { Position = Vector2.Zero };
        AddChild(_add);
        ArmDebris();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized) ArmDebris();
    }

    /// <summary>Give the debris pass this card's walls and a floor just inside the bottom edge.
    /// Without bounds the chunks would free-fall forever and the pool would never drain, so the
    /// field REFUSES to emit until armed — and a zero-sized card stays opted out.</summary>
    private void ArmDebris()
    {
        if (Size.X <= 2f || Size.Y <= 2f) { _burst.DebrisEnabled = false; return; }
        _burst.DebrisEnabled = true;
        _burst.SetDebrisBounds(new Rect2(Vector2.Zero, Size), Size.Y - 8f);
    }

    private Vector2 Origin => new((Size.X - N * Cell) / 2f, Size.Y * 0.5f - Cell * 0.5f);

    public override void _Process(double delta)
    {
        if (Motion.Reduced) return;                       // static poster; no animation
        // Only animate when actually on-screen (scrolled into view).
        if (!IsVisibleInTree() || !GetGlobalRect().Intersects(GetViewportRect())) return;

        float dt = (float)delta;
        _burst.Update(dt);
        _fireTimer -= dt;
        if (_fireTimer <= 0f)
        {
            _fireTimer = Loop;
            _burst.Clear();
            _burst.EmitLine(_art, rowLine: true, index: 0, Origin, Cell, N, budget: 0.55f,
                            k => Demo[k % Demo.Length]);
        }
        QueueRedraw();
        _add.QueueRedraw();
    }

    public override void _Draw()
    {
        if (Size.X <= 0f) return;
        // Mini board backdrop.
        var o = Origin;
        DrawRect(new Rect2(o - new Vector2(4, 4), new Vector2(N * Cell + 8, Cell + 8)), new Color(0.05f, 0.06f, 0.11f, 0.7f));

        if (Motion.Reduced) DrawPoster(o);
        else _burst.DrawNormal(this, Cell, new Rect2());
        // Note: the additive glow half lives on the _add child; the halo sits on top here.
        if (Selected) ThemePreview.DrawSelectionHalo(this, Size);
    }

    // A single frozen frame that still says "this is what the burst looks like".
    private void DrawPoster(Vector2 o)
    {
        var c = o + new Vector2(N * Cell / 2f, Cell / 2f);
        var accent = _art switch
        {
            BurstArtifact.Supernova => new Color(1f, 0.98f, 0.9f),
            BurstArtifact.Rainbow => Palette.AccentViolet,
            BurstArtifact.Confetti => Palette.AccentGreen,
            BurstArtifact.Shards => Palette.Accent,
            BurstArtifact.Aurora => new Color(0.3f, 0.9f, 0.8f),
            BurstArtifact.Lightning => Palette.Accent,
            BurstArtifact.BubblePop => Palette.Accent,
            BurstArtifact.PrismBloom => Palette.AccentViolet,
            BurstArtifact.Starfall => new Color(0.9f, 0.85f, 1f),
            BurstArtifact.Fireworks => Palette.AccentGold,
            BurstArtifact.Fluff => new Color(1.00f, 0.86f, 0.70f),
            BurstArtifact.Splash => new Color(0.55f, 0.95f, 1.00f),
            BurstArtifact.Swarm => new Color(0.75f, 1.00f, 0.90f),
            _ => new Color(1f, 0.95f, 0.6f),
        };
        DrawArc(c, Cell * 1.6f, 0f, Mathf.Tau, 32, new Color(accent.R, accent.G, accent.B, 0.7f), 2f);
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.Tau / 6f;
            DrawCircle(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Cell * 1.1f, 2.5f, accent);
        }
    }
}

/// <summary>
/// The shelf card for a placement sound pack: a bar-graph envelope of what that pack's lock cue
/// actually does over time (attack, decay, and whether it speaks once or twice), so five rows
/// that differ ONLY in timbre still look like five different things before you tap one.
///
/// Shape is the whole identity channel here — every card draws in the same colour, and the only
/// colour difference in the section is the equipped halo, which is also stated in text
/// ("EQUIPPED"). Nothing on this card needs hue to be read, which is the rule the palette work
/// is built on and the reason a "coloured waveform per pack" was not the design.
///
/// The envelopes are hand-drawn caricatures of AudioSynth's cues, not a render of them: reading
/// real samples would drag the synth (core/) into a view file for a 132×58 thumbnail. They are
/// honest about the two things a player can actually hear at a glance — how sharp the attack is
/// and how many hits there are — and <see cref="Ping"/> makes the card twitch on the same tap
/// that plays the sound, which is what ties the picture to the noise.
/// </summary>
public partial class SoundPreview : Control
{
    private const int Bars = 26;
    private const float PingSeconds = 0.45f;

    private readonly int _pack;
    /// <summary>1 → 0 after a preview tap. Accumulated dt only — view code never reads a clock.</summary>
    private float _ping;

    /// <summary>When set, frames the card with an accent halo (the pack now in Settings).</summary>
    public bool Selected { get; init; }

    public SoundPreview(int pack)
    {
        _pack = pack;
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(132, 58);
    }

    public override void _Ready() => SetProcess(false); // idle until something actually plays

    /// <summary>Kick the "it just spoke" envelope. A no-op under reduced motion: the card is
    /// already drawn at full amplitude, so the still frame loses nothing.</summary>
    public void Ping()
    {
        if (Motion.Reduced) return;
        _ping = 1f;
        SetProcess(true);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _ping -= (float)delta / PingSeconds;
        if (_ping <= 0f) { _ping = 0f; SetProcess(false); }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Size.X <= 4f || Size.Y <= 4f) return;

        var pad = new Vector2(6f, 6f);
        float w = Size.X - pad.X * 2f;
        float h = Size.Y - pad.Y * 2f;
        float mid = pad.Y + h * 0.5f;
        float half = h * 0.5f;

        // Backdrop + baseline: the same dark plate the artifact card uses, so the two kinds of
        // cosmetic read as one shelf.
        DrawRect(new Rect2(pad, new Vector2(w, h)), new Color(0.05f, 0.06f, 0.11f, 0.7f));
        DrawLine(new Vector2(pad.X, mid), new Vector2(pad.X + w, mid), new Color(1, 1, 1, 0.10f), 1f);

        var tint = Selected ? Palette.Accent : Palette.TextSecondary;
        // The ping swells the bars ~30% and lifts the ink; a 0.45s decay is under Motion's
        // "one beat" budget and the card is static again before a second tap can land.
        float swell = 1f + 0.30f * _ping;
        float ink = Mathf.Lerp(0.62f, 1f, _ping);

        float step = w / Bars;
        float barW = Mathf.Max(2f, step - 2f);
        for (int i = 0; i < Bars; i++)
        {
            float t = (i + 0.5f) / Bars;
            float a = Mathf.Clamp(Mathf.Abs(Envelope(_pack, t)) * swell, 0f, 1f);
            float bh = Mathf.Max(2f, a * (half - 2f));
            DrawRect(new Rect2(pad.X + i * step + 1f, mid - bh, barW, bh * 2f),
                     new Color(tint.R, tint.G, tint.B, ink));
        }

        if (Selected) ThemePreview.DrawSelectionHalo(this, Size);
    }

    /// <summary>Signed amplitude of pack <paramref name="pack"/> at normalised time
    /// <paramref name="t"/> ∈ [0,1]. Index order matches <c>AudioManager.PackNames</c> /
    /// <c>StoreItem.SoundPackIndex</c>; an out-of-range pack falls back to the default cue
    /// rather than drawing an empty card.</summary>
    private static float Envelope(int pack, float t) => pack switch
    {
        // TAP — soft rubber dome: one low, quick, rounded hump, no ring at all.
        1 => Mathf.Exp(-9f * t) * Mathf.Sin(Mathf.Tau * 2.0f * t) * 1.05f,
        // CLICK — two snaps per place: the second hit is the whole point of the pack.
        2 => (Spike(t, 0.05f, 30f) + 0.85f * Spike(t, 0.34f, 30f)) * Mathf.Sin(Mathf.Tau * 18f * t),
        // TYPEBAR — steel on a platen: one hard strike plus a long thin ring-out.
        3 => (Mathf.Exp(-15f * t) + 0.20f * Mathf.Exp(-2.5f * t)) * Mathf.Sin(Mathf.Tau * 9f * t),
        // WOOD — mallet on a bar: warm, dry, mid decay, no top end.
        4 => Mathf.Exp(-5.5f * t) * Mathf.Sin(Mathf.Tau * 3.2f * t) * 1.1f,
        // NEON (0, the default) — a lit tone that rings a while.
        _ => Mathf.Exp(-3.2f * t) * Mathf.Sin(Mathf.Tau * 5.5f * t),
    };

    private static float Spike(float t, float at, float sharpness)
        => t < at ? Mathf.Exp(-sharpness * 3f * (at - t)) : Mathf.Exp(-sharpness * (t - at));
}
