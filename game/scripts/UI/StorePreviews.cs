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
