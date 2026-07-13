using Godot;
using System;

namespace Blockfall.Theme;

/// <summary>
/// The motion system: standard durations/easings and tween helpers, all gated by
/// the reduced-motion setting in ONE place (reduced = short plain fades, no
/// scale punches, no pulsing). Policy: never touch Engine.TimeScale (Godot 4.3
/// tweens can't ignore it), and tweens that outlive a node must be created on a
/// persistent owner — helpers here bind to the animated node itself, so they die
/// silently (and safely) with it.
/// </summary>
public static class Motion
{
    // Standard durations (seconds).
    public const float Fast = 0.12f;
    public const float Enter = 0.22f;
    public const float Emphasis = 0.26f;

    /// <summary>Reduced-motion preference (settings). Checked live by every helper.</summary>
    public static bool Reduced { get; set; }

    // ---- Screen / element entrances ---------------------------------------

    /// <summary>
    /// Staggered entrance for a screen's items: fade + 24px slide-up, cubic-out.
    /// Total stagger is budgeted at 200ms regardless of item count, so the last
    /// element never lags the screen. Reduced motion: single quick fade, no slide.
    /// Callers invoke this from _Ready, BEFORE the parent containers have run
    /// their first sort pass — at that point every child's Position is still
    /// (0,0), and a tween keyed on it would leave the whole screen stacked in
    /// the top-left corner. So the position capture is deferred one frame, after
    /// layout; only the fade-out state is applied immediately (no flash).
    /// </summary>
    public static void EnterStagger(Control[] items, float initialDelay = 0f)
    {
        if (items.Length == 0) return;
        foreach (var c in items) c.Modulate = new Color(1, 1, 1, 0);
        float step = Mathf.Min(0.04f, 0.2f / items.Length);

        Callable.From(() =>
        {
            for (int i = 0; i < items.Length; i++)
            {
                var c = items[i];
                if (!GodotObject.IsInstanceValid(c)) continue;
                // We pre-set every item to alpha 0 above; if one isn't ready to
                // animate this frame, restore it to visible rather than leave it
                // stuck transparent (a blank screen is worse than a skipped fade).
                if (!c.IsInsideTree()) { c.Modulate = Colors.White; continue; }
                float delay = initialDelay + step * i;
                if (Reduced)
                {
                    var tw = c.CreateTween();
                    tw.TweenInterval(initialDelay);
                    tw.TweenProperty(c, "modulate:a", 1f, Fast);
                    continue;
                }
                var target = c.Position; // now the real, laid-out position
                var t = c.CreateTween().SetParallel();
                t.TweenProperty(c, "modulate:a", 1f, Enter).SetDelay(delay);
                t.TweenProperty(c, "position", target, Enter)
                    .From(target + new Vector2(0, 24)).SetDelay(delay)
                    .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
            }
        }).CallDeferred();
    }

    /// <summary>Pop-in for cards/overlays: fade + scale 0.94 → 1.0 back-out around the center.</summary>
    public static void PopIn(Control c, float duration = 0.18f)
    {
        c.Modulate = new Color(1, 1, 1, 0);
        var tw = c.CreateTween().SetParallel();
        tw.TweenProperty(c, "modulate:a", 1f, Reduced ? Fast : duration);
        if (Reduced) return;
        CenterPivot(c);
        tw.TweenProperty(c, "scale", Vector2.One, duration)
            .From(new Vector2(0.94f, 0.94f))
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    // ---- Button feel ---------------------------------------------------------

    /// <summary>
    /// Attach press/hover feel to a button: press = quick 0.96 squeeze (strong,
    /// touch-first), release/hover = back-out settle. Keeps the pivot centered
    /// through container relayouts.
    /// </summary>
    public static void BindButtonFeel(BaseButton b)
    {
        b.Resized += () => b.PivotOffset = b.Size / 2f;
        b.ButtonDown += () =>
        {
            if (Reduced || !GodotObject.IsInstanceValid(b)) return;
            b.PivotOffset = b.Size / 2f;
            var tw = b.CreateTween();
            tw.TweenProperty(b, "scale", new Vector2(0.96f, 0.96f), 0.05f)
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        };
        b.ButtonUp += () =>
        {
            if (Reduced || !GodotObject.IsInstanceValid(b)) return;
            var tw = b.CreateTween();
            tw.TweenProperty(b, "scale", Vector2.One, 0.15f)
                .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        };
        b.MouseEntered += () =>
        {
            if (Reduced || !GodotObject.IsInstanceValid(b) || b.ButtonPressed) return;
            b.PivotOffset = b.Size / 2f;
            var tw = b.CreateTween();
            tw.TweenProperty(b, "scale", new Vector2(1.02f, 1.02f), 0.12f)
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        };
        b.MouseExited += () =>
        {
            if (!GodotObject.IsInstanceValid(b)) return;
            var tw = b.CreateTween();
            tw.TweenProperty(b, "scale", Vector2.One, 0.12f)
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        };
    }

    // ---- Value animation ---------------------------------------------------

    /// <summary>
    /// Animated count-up on a label (results headline). 800ms ease-out; reduced
    /// motion snaps straight to the final text. Give the label a fixed minimum
    /// width — Orbitron digits reflow containers otherwise.
    /// </summary>
    public static void CountUp(Label label, double to, Func<double, string> fmt, float duration = 0.8f)
    {
        if (Reduced || to <= 0)
        {
            label.Text = fmt(to);
            return;
        }
        var tw = label.CreateTween();
        tw.TweenMethod(Callable.From<double>(v => label.Text = fmt(v)), 0.0, to, duration)
            .SetTrans(Tween.TransitionType.Quint).SetEase(Tween.EaseType.Out);
    }

    /// <summary>Soft looping glow pulse on a CanvasItem's modulate (logo, badges). No-op when reduced.</summary>
    public static void PulseLoop(CanvasItem c, float lowAlpha = 0.75f, float period = 1.6f)
    {
        if (Reduced) return;
        var tw = c.CreateTween().SetLoops();
        tw.TweenProperty(c, "modulate:a", lowAlpha, period / 2f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tw.TweenProperty(c, "modulate:a", 1f, period / 2f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    /// <summary>One-shot attention punch: scale 1.0 → peak → settle (new-best badge).</summary>
    public static void Punch(Control c, float peak = 1.12f, float duration = 0.32f)
    {
        if (Reduced) return;
        if (c.Size == Vector2.Zero)
        {
            // Called from _Ready, before first layout: the pivot would sit at the
            // top-left and the punch would visibly anchor on the corner. Wait a frame.
            Callable.From(() => { if (GodotObject.IsInstanceValid(c) && c.IsInsideTree()) Punch(c, peak, duration); })
                .CallDeferred();
            return;
        }
        CenterPivot(c);
        var tw = c.CreateTween();
        tw.TweenProperty(c, "scale", new Vector2(peak, peak), duration * 0.35f)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(c, "scale", Vector2.One, duration * 0.65f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    private const string PivotBoundMeta = "_motion_pivot_bound";

    private static void CenterPivot(Control c)
    {
        c.PivotOffset = c.Size / 2f;
        // Subscribe the keep-centered handler once per control — PopIn/Punch run
        // repeatedly (every pause open, every expand) and would otherwise stack
        // one lambda per call on the Resized event forever.
        if (c.HasMeta(PivotBoundMeta)) return;
        c.SetMeta(PivotBoundMeta, true);
        c.Resized += () => c.PivotOffset = c.Size / 2f;
    }
}
