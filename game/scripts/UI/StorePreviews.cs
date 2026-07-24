using Godot;
using Blockfall.Gameplay;
using Blockfall.Theme;

namespace Blockfall.UI;

/// <summary>
/// A live skin preview: a strip of real gems rendered through the shared
/// <see cref="BlockRender"/> with the theme's actual material + glyph + edge tint, breathing
/// on the same shimmer clock the board uses — so what you see in the shop is exactly what you
/// wear. Renders explicit theme colours (not the equipped palette), so it previews any skin
/// without disturbing the live board behind the store. Reduced motion → a static poster frame.
/// </summary>
public partial class ThemePreview : Control
{
    private readonly BlockTheme _theme;
    private readonly float _cell;
    private readonly Color[] _fills;
    private float _shimmer;

    /// <summary>When set, frames the strip with an accent halo (the equipped skin).</summary>
    public bool Selected { get; init; }

    public ThemePreview(BlockTheme theme, float cell = 30f)
    {
        _theme = theme;
        _cell = cell;
        _fills = new[] { theme.I, theme.T, theme.S, theme.Z, theme.J, theme.L };
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(_fills.Length * (cell + 4f), cell + 2f);
    }

    public override void _Process(double delta)
    {
        if (Motion.Reduced || !IsVisibleInTree()) return;
        _shimmer += (float)delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Size.X <= 0f) return;
        bool reduced = Motion.Reduced;
        for (int i = 0; i < _fills.Length; i++)
        {
            var rect = new Rect2(i * (_cell + 4f), 1f, _cell, _cell);
            var fill = _fills[i];
            var emissive = Palette.Emissive(fill, 2.0f);
            BlockRender.DrawCell(this, rect, _cell, fill, emissive, 1f, _theme.Material, _theme.EdgeTint,
                                 _theme.Glyph, _shimmer, i, drawGlyph: true, reduced: reduced);
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
