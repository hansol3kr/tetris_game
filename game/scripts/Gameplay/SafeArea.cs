using Godot;

namespace Blockfall.Gameplay;

/// <summary>
/// Device safe-area (notch / Dynamic Island / front camera cutout / home
/// indicator) expressed as logical insets. UI is kept inside these so nothing
/// important is clipped by the sensor housing or the home bar. Returns zero on
/// desktop / headless, so those platforms are unchanged.
/// </summary>
public static class SafeArea
{
    /// <summary>Logical (stretch-space) insets in pixels: (left, top, right, bottom).</summary>
    public static (float left, float top, float right, float bottom) Insets(Viewport vp)
    {
        if (DisplayServer.GetName() == "headless") return (0f, 0f, 0f, 0f);

        Vector2 logical = vp.GetVisibleRect().Size;               // stretched viewport
        Vector2I win = DisplayServer.WindowGetSize();             // physical window px
        if (win.X <= 0 || win.Y <= 0) return (0f, 0f, 0f, 0f);
        Rect2I safe = DisplayServer.GetDisplaySafeArea();         // physical safe rect
        if (safe.Size.X <= 0 || safe.Size.Y <= 0) return (0f, 0f, 0f, 0f);

        // Convert each physical inset to a fraction of the window, then to logical
        // pixels — scale-independent, so retina/point/pixel differences cancel out.
        float left = Mathf.Max(0, safe.Position.X) / win.X * logical.X;
        float top = Mathf.Max(0, safe.Position.Y) / win.Y * logical.Y;
        float right = Mathf.Max(0, win.X - safe.Position.X - safe.Size.X) / win.X * logical.X;
        float bottom = Mathf.Max(0, win.Y - safe.Position.Y - safe.Size.Y) / win.Y * logical.Y;

        // Real safe areas are small; clamp so a bad reading can never collapse the UI.
        left = Mathf.Min(left, logical.X * 0.12f);
        right = Mathf.Min(right, logical.X * 0.12f);
        top = Mathf.Min(top, logical.Y * 0.22f);
        bottom = Mathf.Min(bottom, logical.Y * 0.16f);
        return (left, top, right, bottom);
    }
}
