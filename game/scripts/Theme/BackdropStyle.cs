namespace Blockfall.Theme;

/// <summary>
/// The procedural scene the animated backdrop paints behind every screen AND behind the play
/// field. This is the cosmetic axis with the largest screen area in the game, and the only one
/// the player sees before they press anything.
///
/// <para>Deliberately ORTHOGONAL to colour: a style contributes motion and form only, and takes
/// its palette from the equipped skin (<see cref="BlockTheme.BgTop"/>/<see cref="BlockTheme.BgBottom"/>
/// plus the resolved UI accents). So N styles × M skins reads as N·M distinct scenes instead of
/// N wallpapers — which is why the store sells them as a separate shelf rather than baking one
/// into each skin. A skin may still declare its own <see cref="BlockTheme.Scene"/> as its
/// signature (the set lands whole, exactly like <see cref="BurstArtifact"/>), and the player can
/// override it from the BACKDROP shelf afterwards.</para>
///
/// <para>Values map 1:1 onto the <c>pattern</c> uniform in <c>res://shaders/background.gdshader</c>
/// — the int IS the contract, so this enum is APPEND-ONLY (the equipped style is persisted as an
/// id string, but the shader reads the ordinal).</para>
/// </summary>
public enum BackdropStyle
{
    /// <summary>The shipped NEON FLUX look: three slow aurora blobs + a dot grid + specks.
    /// Index 0 so an absent/unknown save value lands on exactly what shipped.</summary>
    Aurora,
    Nebula,
    Starfield,
    Grid,
    Rays,
    Waves,
    Bokeh,
    Rain,
    Embers,
    Circuit,
}

/// <summary>Maps store item ids to the backdrop the shader renders. Mirrors
/// <see cref="BurstArtifacts"/> — same shape, same reasons (the save file stores an id, the
/// renderer wants a value, and a themed set declares its scene by value).</summary>
public static class Backdrops
{
    public const string DefaultId = "scene_aurora";

    public static BackdropStyle FromId(string? id) => id switch
    {
        "scene_nebula" => BackdropStyle.Nebula,
        "scene_starfield" => BackdropStyle.Starfield,
        "scene_grid" => BackdropStyle.Grid,
        "scene_rays" => BackdropStyle.Rays,
        "scene_waves" => BackdropStyle.Waves,
        "scene_bokeh" => BackdropStyle.Bokeh,
        "scene_rain" => BackdropStyle.Rain,
        "scene_embers" => BackdropStyle.Embers,
        "scene_circuit" => BackdropStyle.Circuit,
        _ => BackdropStyle.Aurora,
    };

    public static string ToId(BackdropStyle s) => s switch
    {
        BackdropStyle.Nebula => "scene_nebula",
        BackdropStyle.Starfield => "scene_starfield",
        BackdropStyle.Grid => "scene_grid",
        BackdropStyle.Rays => "scene_rays",
        BackdropStyle.Waves => "scene_waves",
        BackdropStyle.Bokeh => "scene_bokeh",
        BackdropStyle.Rain => "scene_rain",
        BackdropStyle.Embers => "scene_embers",
        BackdropStyle.Circuit => "scene_circuit",
        _ => DefaultId,
    };
}
