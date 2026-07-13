using Godot;
using Blockfall.Platform;
using Blockfall.Audio;
using Blockfall.Theme;
using Blockfall.UI;

namespace Blockfall;

/// <summary>
/// Application entry point (attached to the root of Main.tscn). Wires up the
/// global services once — fonts, the shared UI theme, the animated backdrop,
/// the HDR-2D glow environment — then hands control to the scene router.
/// Everything is built in code so the project opens and runs without
/// hand-authored .tscn wiring.
/// </summary>
public partial class Bootstrap : Node2D
{
    public static Bootstrap Instance { get; private set; } = null!;

    public PlatformHub Platform { get; private set; } = null!;
    public SaveManager Save { get; private set; } = null!;
    public AudioManager Audio { get; private set; } = null!;
    public SceneRouter Router { get; private set; } = null!;
    public Background Bg { get; private set; } = null!;

    /// <summary>
    /// The viewport-filling Control that every screen is parented under. A screen
    /// added directly to this Node2D never receives a rect (its parent isn't a
    /// Control), so its full-rect anchors collapse to 0×0 and the whole UI clips
    /// to nothing. Screens go under this host so their anchors resolve normally.
    /// </summary>
    public Control ScreenHost { get; private set; } = null!;

    private Godot.Environment _env = null!;

    public override void _Ready()
    {
        Instance = this;
        Gameplay.MobilePreview.Init(); // desktop mobile-layout preview opt-in (--mobile-preview / env / F9)

        // Global services as children so they persist across scene swaps.
        Save = new SaveManager { Name = "SaveManager" };
        AddChild(Save); // SaveManager._Ready loads settings from disk here.

        // Design system: fonts first, then the theme built from them, then the
        // motion system's reduced-motion gate. Must precede any screen build.
        Fonts.Init();
        UiTheme.Init(Save.Settings.TextScale); // accessibility text scale, before any screen builds
        Motion.Reduced = Save.Settings.ReducedMotion;
        Blockfall.Core.Localization.Loc.Current = Save.Settings.Language; // localize before any screen builds
        Gameplay.KeyBinds.ApplyAll(Save.Settings); // repoint keyboard actions at custom bindings
        ApplyFullscreen(); // restore the saved desktop window mode

        // On mobile, default neon bloom OFF for a fresh install: HDR 2D + glow is a
        // performance/driver risk on the long tail of mobile GPUs, and the hand-drawn
        // glow underlays carry the look without it. Players can still enable it in
        // Settings; this only picks the first-run default, never overrides a choice.
        if (Save.FreshInstall && OS.HasFeature("mobile"))
            Save.Settings.GlowEnabled = false;

        // Restore the colorblind palette preference before any board renders,
        // then the purchased cosmetic theme (colorblind still wins per-piece).
        Palette.ColorblindMode = Save.Settings.ColorblindMode;
        Palette.ApplyTheme(StoreCatalog.ById(Save.EquippedThemeId)?.Theme);

        SetupEnvironment();
        ApplyGlowSetting();

        Bg = new Background { Name = "Background" };
        AddChild(Bg);

        // Screen host: sized to the viewport explicitly (anchors alone don't size a
        // Control whose parent isn't a Control) and kept in sync on window resize.
        // Screens draw on canvas layer 0, in front of the backdrop (layer -1).
        ScreenHost = new Control { Name = "ScreenHost", MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(ScreenHost);
        FitScreenHost();
        GetViewport().SizeChanged += FitScreenHost;

        Platform = new PlatformHub { Name = "PlatformHub" };
        AddChild(Platform);
        Platform.Initialize();
        Platform.SyncCloud(); // pull + merge + push the cloud save where supported (no-op otherwise)

        Audio = new AudioManager { Name = "AudioManager" };
        AddChild(Audio);

        Router = new SceneRouter { Name = "SceneRouter" };
        AddChild(Router);

        // First launch drops into the tutorial; afterwards, straight to the menu.
        Router.GoToStart();

        // Opt-in headless smoke test (--autoplay). Never attaches in a shipped build.
        if (Dev.AutoPlay.Requested)
            AddChild(new Dev.AutoPlay { Name = "AutoPlay" });
    }

    /// <summary>
    /// Bloom environment for the neon glow. 2D glow only works with HDR 2D on
    /// (see ApplyGlowSetting); the threshold sits above 1.0 so ordinary UI —
    /// white text, glass borders — never blooms, only the deliberately
    /// overbright emissive seeds (Palette.Emissive) do.
    /// </summary>
    private void SetupEnvironment()
    {
        _env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = Palette.Background,
            GlowEnabled = true,
            GlowIntensity = 1.0f,
            GlowStrength = 1.05f,
            GlowBloom = 0.0f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Additive,
            GlowHdrThreshold = 1.1f,
        };
        var we = new WorldEnvironment { Environment = _env, Name = "WorldEnvironment" };
        AddChild(we);
    }

    /// <summary>
    /// HDR 2D + WorldEnvironment glow (bloom) blanks the entire 2D canvas on some
    /// macOS/Metal drivers — the menu and backdrop vanish, leaving only the clear
    /// color. It's force-disabled there; the neon look is carried by the hand-drawn
    /// glow underlays in BoardView, which are designed to stand alone without bloom.
    /// </summary>
    public static bool GlowSupported => OS.GetName() != "macOS";

    /// <summary>
    /// Applies the player's glow setting: HDR 2D + bloom together. Turning both
    /// off is the low-end-device escape hatch — the hand-drawn glow underlays in
    /// BoardView are designed to stand alone without bloom. On platforms where the
    /// combo is unsafe (see <see cref="GlowSupported"/>) it stays off regardless.
    /// </summary>
    public void ApplyGlowSetting()
    {
        bool on = Save.Settings.GlowEnabled && GlowSupported;
        GetViewport().UseHdr2D = on;
        _env.GlowEnabled = on;
    }

    /// <summary>
    /// Apply the saved desktop window mode. No-op on mobile, which is always
    /// fullscreen and where switching modes is meaningless (and can misbehave).
    /// </summary>
    public void ApplyFullscreen()
    {
        if (OS.HasFeature("mobile")) return;
        DisplayServer.WindowSetMode(Save.Settings.Fullscreen
            ? DisplayServer.WindowMode.Fullscreen
            : DisplayServer.WindowMode.Windowed);
    }

    /// <summary>Flip fullscreen ↔ windowed, apply it live, and persist the choice.</summary>
    public void ToggleFullscreen()
    {
        if (OS.HasFeature("mobile")) return;
        Save.Settings.Fullscreen = !Save.Settings.Fullscreen;
        ApplyFullscreen();
        Save.SetSettings(Save.Settings); // mark dirty + flush
    }

    /// <summary>F11 toggles fullscreen from anywhere (desktop). Handled globally here.</summary>
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.F11 })
        {
            ToggleFullscreen();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Keep the screen host exactly covering the viewport (initial + on resize).</summary>
    private void FitScreenHost()
    {
        if (!GodotObject.IsInstanceValid(ScreenHost)) return;
        ScreenHost.Position = Vector2.Zero;
        ScreenHost.Size = GetViewport().GetVisibleRect().Size;
    }

    public override void _Notification(int what)
    {
        // Persist progress if the OS is about to suspend/close the app (mobile).
        if (what == NotificationWMCloseRequest || what == NotificationApplicationPaused)
            Save?.Flush();
    }
}
