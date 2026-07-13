using Godot;
using Blockfall.Theme;

namespace Blockfall.UI;

/// <summary>
/// The persistent animated backdrop, on its own CanvasLayer at -1 so it lives
/// behind every screen and survives SceneRouter swaps (screens fade their own
/// modulate; the backdrop provides continuity). During gameplay it dims itself
/// so ambient motion never competes with the falling pieces. Honors the
/// reduced-motion setting by freezing all drift.
/// </summary>
public partial class Background : CanvasLayer
{
    private ShaderMaterial? _mat;
    private ColorRect _rect = null!;
    private Tween? _pulseTween;
    private Tween? _dimTween;

    public override void _Ready()
    {
        Layer = -1;

        _rect = new ColorRect { Color = Palette.Background };
        _rect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _rect.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_rect);

        if (ResourceLoader.Exists("res://shaders/background.gdshader"))
        {
            var shader = ResourceLoader.Load<Shader>("res://shaders/background.gdshader");
            _mat = new ShaderMaterial { Shader = shader };
            _rect.Material = _mat;
            _rect.Resized += () => _mat.SetShaderParameter("size", _rect.Size);
        }

        ApplyThemeColors();
        ApplyMotionSetting();
    }

    /// <summary>Re-read the backdrop gradient from the palette (equipping a store theme).</summary>
    public void ApplyThemeColors()
    {
        _rect.Color = Palette.Background;
        _mat?.SetShaderParameter("top_color", Palette.Background);
        _mat?.SetShaderParameter("bottom_color", Palette.BgBottom);
    }

    /// <summary>Re-read the reduced-motion preference (call when settings change).</summary>
    public void ApplyMotionSetting()
        => _mat?.SetShaderParameter("motion", Motion.Reduced ? 0f : 1f);

    /// <summary>Dim the backdrop for gameplay; restore it on menus.</summary>
    public void SetGameplayDim(bool on)
    {
        if (_mat is null) return;
        float target = on ? 1f : 0f;
        _dimTween?.Kill();
        _dimTween = CreateTween();
        // TweenProperty returns null if the shader parameter can't be resolved (e.g. the
        // shader failed to compile on this GPU, or a headless/null render backend). Fall
        // back to setting it directly so a backdrop hiccup never NREs and crashes the screen.
        var tweener = _dimTween.TweenProperty(_mat, "shader_parameter/dim", target, 0.5f);
        if (tweener is null) { _mat.SetShaderParameter("dim", target); return; }
        tweener.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
    }

    /// <summary>One-shot ambient accent wash (level-up, big moments).</summary>
    public void Pulse(Color color, float strength = 0.5f)
    {
        if (_mat is null || Motion.Reduced) return;
        _pulseTween?.Kill();
        _mat.SetShaderParameter("pulse_color", color);
        _mat.SetShaderParameter("pulse", strength);
        _pulseTween = CreateTween();
        var tweener = _pulseTween.TweenProperty(_mat, "shader_parameter/pulse", 0f, 0.7f);
        if (tweener is null) return; // shader param unavailable — the static set above already applied
        tweener.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
    }
}
