using Godot;
using Blockfall.Core.BlockFit;
using Blockfall.Core.Localization;
using Blockfall.Theme;

namespace Blockfall.Gameplay;

/// <summary>
/// The FUSE read-out's behaviour, shared verbatim by both Block Fit screens (solo
/// <see cref="BlockFitController"/> and the duel <see cref="BlockFitVersusController"/>) — the
/// same reason <see cref="MergeLoupe"/> is shared: two copies of a feedback rule drift, one
/// class cannot. Each screen owns the Label's geometry; this owns what it SAYS.
///
/// WHY IT IS MORE THAN A NUMBER. Simulated runs sit at "5/5" for 85–92% of their length, so a
/// static counter is a sign the eye stops reading long before the count ever moves — the player
/// meets the budget for the first time at the moment it fails them. So every change announces
/// itself twice, on two independent channels:
///   • a transient "+1" / "-1" suffix — text, therefore alive under Reduced Motion, and it names
///     the direction rather than making the player diff two remembered numbers;
///   • a short brightness blink, which is pure attention-getting and is therefore gated off by
///     <see cref="Motion.Reduced"/>.
/// <see cref="Alert"/> fires the same blink with no value change, for a fuse REFUSED because the
/// budget is spent: the counter is the thing that explains the refusal, so it has to be the thing
/// that moves.
///
/// Colour is never the message: the count itself ("0/5") carries the state and the red at zero is
/// only a second, redundant channel.
/// </summary>
internal sealed class FuseMeter
{
    /// <summary>How long a change stays announced. Long enough to catch the eye after a
    /// placement's own celebration, short enough that the suffix is gone before the next drag.</summary>
    private const float FlashLife = 0.9f;

    private readonly Label _label;
    private int _shown = -1;    // last value painted (-1 = never; the first paint is not a "change")
    private int _delta;
    private float _flash;

    internal FuseMeter(Label label) => _label = label;

    /// <summary>Drive from _Process. Repaints only when the count actually moves — a theme colour
    /// override every frame is pure garbage churn.</summary>
    internal void Sync(int merges, float dt)
    {
        if (_shown != merges)
        {
            int prev = _shown;
            _shown = merges;
            if (prev >= 0) { _delta = merges - prev; _flash = FlashLife; }
            Paint();
        }

        if (_flash <= 0f) return;
        _flash -= dt;
        bool done = _flash <= 0f;
        _label.Modulate = done || Motion.Reduced
            ? Colors.White
            : new Color(1, 1, 1, 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(_flash * 14f)));
        if (done) { _delta = 0; Paint(); }
    }

    /// <summary>Draw the eye to the counter without changing it — the player just tried to fuse
    /// with an empty budget.</summary>
    internal void Alert() => _flash = FlashLife;

    private void Paint()
    {
        string text = Loc.T("FUSE {0}/{1}", _shown, BlockFitGame.MaxMerges);
        // ASCII sign on purpose: the baked font is code-generated and a typographic minus would
        // be the kind of glyph that silently renders as tofu on one platform.
        if (_delta != 0) text += _delta > 0 ? $"  +{_delta}" : $"  -{-_delta}";
        _label.Text = text;
        _label.AddThemeColorOverride("font_color", _shown > 0 ? Palette.TextSecondary : Palette.AccentRed);
    }
}
