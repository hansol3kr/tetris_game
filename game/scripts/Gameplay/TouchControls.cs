using Godot;
using System;
using Blockfall.Theme;

namespace Blockfall.Gameplay;

/// <summary>
/// On-screen thumb controls for phones/tablets: translucent circular glass
/// buttons with a bright accent press state (press is the strong state — there
/// is no hover on glass). Only shown when a touchscreen is present. Directional
/// + soft-drop buttons fire press AND release so DAS/ARR works the same as
/// physical keys; rotate / hard drop / hold are single taps.
/// Layout: movement cluster bottom-left, action cluster bottom-right.
/// </summary>
public partial class TouchControls : Control
{
    public event Action? LeftPressed, LeftReleased, RightPressed, RightReleased, SoftPressed, SoftReleased;
    public event Action? HardDrop, RotateCw, RotateCcw, Hold;

    public override void _Ready()
    {
        UiTheme.ApplyTo(this); // hangs off a Node2D controller — no theme inheritance
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // Movement cluster (bottom-left).
        var left = MakeButton("◀", 96, () => LeftPressed?.Invoke(), () => LeftReleased?.Invoke());
        var right = MakeButton("▶", 96, () => RightPressed?.Invoke(), () => RightReleased?.Invoke());
        var soft = MakeButton("▼", 96, () => SoftPressed?.Invoke(), () => SoftReleased?.Invoke());
        PlaceBottom(left, new Vector2(28, -234));
        PlaceBottom(right, new Vector2(196, -234));
        PlaceBottom(soft, new Vector2(112, -122));

        // Action cluster (bottom-right).
        var hard = MakeButton("⤓", 108, () => HardDrop?.Invoke(), null);
        var rcw = MakeButton("↻", 96, () => RotateCw?.Invoke(), null);
        var rccw = MakeButton("↺", 96, () => RotateCcw?.Invoke(), null);
        var hold = MakeButton("H", 84, () => Hold?.Invoke(), null);
        PlaceBottomRight(rccw, new Vector2(-292, -234));
        PlaceBottomRight(rcw, new Vector2(-124, -234));
        PlaceBottomRight(hard, new Vector2(-214, -128));
        PlaceBottomRight(hold, new Vector2(-124, -356));
    }

    private Button MakeButton(string glyph, int size, Action? onDown, Action? onUp)
    {
        var b = new Button
        {
            Text = glyph,
            CustomMinimumSize = new Vector2(size, size),
            Modulate = new Color(1, 1, 1, 0.82f),
        };

        // Circular glass: baked circle textures stretched over the (square) button.
        b.AddThemeStyleboxOverride("normal", CircleStyle(
            TextureFactory.Circle(96, new Color(0.72f, 0.76f, 1f, 0.07f), new Color(1, 1, 1, 0.16f), 1.5f)));
        b.AddThemeStyleboxOverride("hover", CircleStyle(
            TextureFactory.Circle(96, new Color(0.72f, 0.76f, 1f, 0.10f), new Color(1, 1, 1, 0.22f), 1.5f)));
        b.AddThemeStyleboxOverride("pressed", CircleStyle(
            TextureFactory.Circle(96, new Color(Palette.Accent.R, Palette.Accent.G, Palette.Accent.B, 0.30f),
                                       new Color(Palette.Accent.R, Palette.Accent.G, Palette.Accent.B, 0.95f), 2f)));
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

        b.AddThemeFontSizeOverride("font_size", (int)(size * 0.42f));
        b.AddThemeColorOverride("font_color", Palette.TextPrimary);
        b.AddThemeColorOverride("font_pressed_color", Colors.White);

        Motion.BindButtonFeel(b);
        if (onDown != null) b.ButtonDown += () => onDown();
        if (onUp != null) b.ButtonUp += () => onUp();
        return b;
    }

    private static StyleBoxTexture CircleStyle(Texture2D tex) => new()
    {
        Texture = tex,
        // No 9-patch margins: buttons are square, so the circle stretches 1:1.
    };

    private void PlaceBottom(Button b, Vector2 offset)
    {
        b.SetAnchorsPreset(LayoutPreset.BottomLeft);
        b.Position = offset;
        AddChild(b);
    }

    private void PlaceBottomRight(Button b, Vector2 offset)
    {
        b.SetAnchorsPreset(LayoutPreset.BottomRight);
        b.Position = offset;
        AddChild(b);
    }

    public static bool ShouldShow()
        => DisplayServer.IsTouchscreenAvailable() || OS.HasFeature("mobile") || MobilePreview.Enabled;
}
