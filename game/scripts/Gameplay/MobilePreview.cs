using Godot;

namespace Blockfall.Gameplay;

/// <summary>
/// Desktop preview of the MOBILE build. Since the phone UI (portrait window,
/// on-screen touch pad) is shared between Android and iOS, this lets the mobile
/// layout be checked on a desktop with no device attached — the closest thing to
/// an iOS look-and-feel that runs off a Mac.
///
/// Turn it on with the <c>--mobile-preview</c> command-line flag, the
/// <c>BLOCKFALL_MOBILE_PREVIEW=1</c> environment variable, or the <b>F9</b> key
/// during a game. It only forces <see cref="TouchControls.ShouldShow"/> to true —
/// real mobile builds already report a touchscreen, so this is a no-op there.
/// </summary>
public static class MobilePreview
{
    /// <summary>When true, the on-screen touch controls appear on desktop too.</summary>
    public static bool Enabled { get; set; }

    /// <summary>Read the startup opt-in once (called from Bootstrap before any screen builds).</summary>
    public static void Init()
    {
        if (HasFlag(OS.GetCmdlineArgs()) || HasFlag(OS.GetCmdlineUserArgs())
            || OS.GetEnvironment("BLOCKFALL_MOBILE_PREVIEW") == "1")
        {
            Enabled = true;
        }
        if (Enabled)
            GD.Print("[MobilePreview] ON — desktop is emulating the phone layout (touch controls forced). Press F9 to toggle.");
    }

    private static bool HasFlag(string[] args)
    {
        foreach (var a in args)
            if (a == "--mobile-preview" || a == "--mobile") return true;
        return false;
    }
}
