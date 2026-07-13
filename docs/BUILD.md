# Blockfall — Setup, Build & Export Guide

This document covers everything needed to build, run, test, and export
**Blockfall** — the neon falling-block puzzle. It targets **Godot 4.3 (.NET/Mono
build)** with **C# on .NET 8**, and ships to **Steam (Windows/macOS/Linux)**,
**Android**, and **iOS**.

> Blockfall is an original brand and is **not affiliated with Tetris®**. Keep
> store metadata, achievement names, and marketing copy free of the Tetris
> trademark and any trade-dress that imitates it.

---

## Repository layout

```
tetris_game/                     # repo root
├─ Blockfall.sln                 # solution: Core + Core.Tests (NOT the Godot project)
├─ core/                         # Blockfall.Core — pure C# rules engine (net8.0)
├─ core.tests/                   # xUnit tests for the core (79 tests)
├─ game/                         # Godot 4.3 C# game project
│  ├─ Blockfall.csproj           # references ../core; presentation + platform layer
│  ├─ project.godot
│  ├─ export_presets.cfg         # starter presets for all 5 targets
│  └─ scripts/Platform/          # runtime store-backend selection
├─ docs/
└─ .github/workflows/ci.yml      # builds + tests the core on every push/PR
```

The **rules engine** (`Blockfall.Core`) has no engine dependency — it is plain
`net8.0` and can be built and tested with the .NET SDK alone. The **Godot game
project** (`game/Blockfall.csproj`) references it and is built/exported through
the Godot toolchain, not through `dotnet` directly.

---

## 1. Prerequisites

### 1.1 Common (all platforms)

| Tool | Version | Notes |
|------|---------|-------|
| **Godot Engine** | **4.3 — .NET/Mono build** | You must download the **".NET" / "Mono"** build, *not* the standard build. C# is unavailable in the standard build. |
| **.NET SDK** | **8.0.x** | Required by both the core library and Godot's C# tooling. `dotnet --version` should report `8.0.*`. |
| Git | any recent | |

Download Godot 4.3 .NET from <https://godotengine.org/download> (or the archive
at <https://godotengine.org/download/archive/>). On the command line the editor
binary is referred to below as `godot` — alias it to the actual executable, e.g.
`Godot_v4.3-stable_mono_linux.x86_64`.

Verify the toolchain:

```bash
dotnet --info          # must list an 8.0.x SDK
godot --version        # must report 4.3.x.mono
```

### 1.2 Per-platform (summarised; details in each export section)

- **Steam desktop:** the **GodotSteam GDExtension** (matching Godot 4.3) and a
  Steam **App ID** (`steam_appid.txt`). Export templates for Windows/macOS/Linux.
- **Android:** Android SDK + platform-tools, **JDK 17**, the Godot **Android
  build template**, a signing **keystore**, and the **AdMob** + **IAP** Godot
  Android plugins.
- **iOS:** **macOS + Xcode**, an Apple Developer account, provisioning
  profile/signing certs, and the **AdMob iOS** plugin. Export is only possible
  from macOS.

---

## 2. Running the core tests

The core engine is fully testable without Godot. From the repo root:

```bash
dotnet restore Blockfall.sln
dotnet build   Blockfall.sln --configuration Release
dotnet test    Blockfall.sln --configuration Release
```

You should see **131 passing tests** (SRS kicks, T-spin detection, scoring,
randomizer, board mechanics, attack/garbage, finesse solver). This is the same path CI runs (see §7). Run these
before any change to `core/` — they are the safety net for the rules engine.

To run a single test file/class:

```bash
dotnet test Blockfall.sln --filter "FullyQualifiedName~SpinDetectorTests"
```

> Note: `Blockfall.sln` deliberately contains **only** `Blockfall.Core` and
> `Blockfall.Core.Tests`. The Godot game project is not in the solution because
> it builds through the Godot toolchain. Do not add it here or CI will try to
> pull in the engine SDK.

---

## 3. Opening the project & running in the editor

1. Launch the **Godot 4.3 .NET** editor.
2. **Import** → select `game/project.godot`.
3. First open triggers a C# restore/build. If it doesn't, or if you see missing
   assembly errors, build once from the CLI:

   ```bash
   dotnet build game/Blockfall.csproj
   ```

   (Godot invokes MSBuild automatically, but a manual build surfaces C# errors
   with clearer output.)
4. Press **F5** (or the Play button) to run. The main scene is
   `res://scenes/Main.tscn`; `Bootstrap.cs` wires up the global services and the
   neon glow `WorldEnvironment`.

In the editor / on desktop dev builds, the platform layer falls back to
**`NullPlatform`** — everything unlocked, no ads, no store calls — so the game
runs fully without any native plugin installed (see §6).

Headless build check (no window, useful pre-commit):

```bash
godot --headless --path game --build-solutions --quit
```

---

## 4. Runtime platform selection (read before exporting)

Blockfall selects its store/monetization backend **at runtime**, not at compile
time. `PlatformHub.Initialize()` (`game/scripts/Platform/PlatformHub.cs`) picks
one of three `IPlatformServices` implementations using Godot **OS feature tags**:

```csharp
if (OS.HasFeature("mobile"))       // Android + iOS report this automatically
    _services = new MobilePlatform(...);   // AdMob interstitial/rewarded + remove-ads IAP
else if (OS.HasFeature("steam"))   // custom feature tag set by the desktop presets
    _services = new SteamPlatform();       // achievements/leaderboards, no ads, always premium
else
    _services = new NullPlatform();        // editor/desktop dev fallback, all unlocked
```

Consequences for exporting:

- **Android and iOS** builds automatically carry the built-in **`mobile`**
  feature — no configuration needed to get the mobile backend.
- **Steam desktop** builds must add the **custom feature tag `steam`**. This is
  set per preset via **Custom Features** in the export dialog (already present as
  `custom_features="steam"` in the three desktop presets in
  `export_presets.cfg`). Without it, a desktop build falls through to
  `NullPlatform`.

Because selection is at runtime, **one codebase** produces every target — there
are no per-platform `#define`s or separate compilation symbols to manage.

---

## 5. Per-platform export

All presets are pre-seeded in `game/export_presets.cfg`. Open **Godot → Project →
Export** to install templates, finish credentials/signing, and export. Replace
placeholder identifiers (`com.yourstudio.blockfall`, `Your Studio`) with your
real values before shipping.

Install matching **export templates** once via **Editor → Manage Export
Templates → Download and Install** (must match the exact Godot 4.3 build).

### 5.1 Steam desktop (Windows / macOS / Linux)

Presets `preset.0` (Windows), `preset.1` (macOS), `preset.2` (Linux). Each sets
`custom_features="steam"`.

**a) Install the GodotSteam GDExtension**

Blockfall's `SteamPlatform` talks to the Steam API through the **GodotSteam
GDExtension** (the `Steam` engine singleton), not native C# bindings — so it stays
loosely coupled and no-ops gracefully if the extension is absent.

1. Download the **GodotSteam GDExtension** build matching **Godot 4.3** from
   <https://godotsteam.com> (the *GDExtension* variant, so no custom engine is
   required).
2. Copy its `addons/godotsteam/` folder into `game/addons/godotsteam/`. The
   `.gdextension` file registers the `Steam` singleton on load.
3. Restart the editor. Confirm at runtime — the log prints `[Steam] initialized`
   on success, or a warning if the singleton is missing.

**b) Set the Steam App ID**

1. Put your Steamworks App ID in **`game/steam_appid.txt`** (a single line, e.g.
   `480` for the Spacewar test app during development). This file must sit next
   to the game executable at runtime; keep a copy in `game/` for editor testing
   and ensure it is placed beside the exported binary (or ship it via the Steam
   depot).
2. `SteamPlatform.Initialize()` calls `steamInit`; the App ID is read from
   `steam_appid.txt` (dev) or supplied by the Steam client when launched through
   Steam (production).
3. Configure achievement IDs in the Steamworks partner site to match the code:
   `ACH_FIRST_TETRIS`, `ACH_FIRST_TSPIN`, `ACH_PERFECT_CLEAR`, `ACH_COMBO_5`,
   `ACH_MARATHON_CLEAR`, plus leaderboards named `LB_<MODE>`.

**c) Export**

Verify **Custom Features** contains `steam` in each desktop preset (already set),
then:

```bash
# Windows
godot --headless --path game --export-release "Windows Desktop (Steam)" build/windows/Blockfall.exe
# macOS   (codesign/notarize separately for distribution outside Steam)
godot --headless --path game --export-release "macOS (Steam)" build/macos/Blockfall.zip
# Linux
godot --headless --path game --export-release "Linux (Steam)" build/linux/Blockfall.x86_64
```

Place `steam_appid.txt` next to each exported binary for local testing. For
release, upload the build to a **Steam depot** via `steamcmd` / the Steamworks
SDK content builder. Steam is the **paid, ad-free** SKU — `SteamPlatform` reports
`IsPremium = true`, so no ad code paths ever run.

> macOS note: for distribution *outside* Steam you must codesign + notarize with
> an Apple Developer ID. Inside Steam, the built-in signing in the preset is
> sufficient for most cases.

> Windows note: both Windows presets set `application/modify_resources=false`.
> Godot embeds the exe icon + version/company strings via **rcedit**, which on a
> non-Windows build host must run through **wine** — absent here, so Godot only
> printed a `rcedit을 실행하지 못했습니다` warning and produced the exe with the
> engine's default icon anyway. Disabling resource modification makes that
> outcome explicit and keeps the build log clean; the exe is otherwise identical.
> To ship a Windows build with the proper Blockfall icon + version metadata,
> either (a) build the Windows preset **on a Windows machine**, or (b) install
> `wine` + drop `rcedit.exe` somewhere and point the editor settings
> `export/windows/rcedit` (and `export/windows/wine`) at them, then flip
> `modify_resources` back to `true`. A real `.ico` in `application/icon` is also
> needed for a custom taskbar icon (the project only ships `icon.svg`).

### 5.2 Android (Google Play)

Preset `preset.3` currently outputs a **debug-signed APK**
(`build/android/Blockfall.apk`) for sideload testing — `./build-all.sh android`
produces `dist/Blockfall-android-debug.apk` in one command. For the Play Store,
switch the preset to `gradle_build/export_format=1` (`.aab`) and configure the
release keystore below. `gradle_build/use_gradle_build=true` is enabled so
plugins can be compiled in.

**a) Toolchain**

1. Install the **Android SDK** (Android Studio or command-line tools) and
   **JDK 17**, plus the **.NET Android workload** (`dotnet workload install
   android`) — required for C# projects.
2. In Godot: **Editor → Editor Settings → Export → Android** — set the paths to
   the **Android SDK** and **Java SDK (JDK 17)** and the **debug keystore**.
3. **Project → Install Android Build Template** (installs the Gradle build
   template into `game/android/`; required for custom plugins and AAB export).
   If installed by hand (unzipping `android_source.zip`), also create
   `game/android/.gdignore` — otherwise the editor imports the template's
   `res/*.png` files and the generated `.import` files break the Gradle build.

**b) Release keystore & signing**

Create an upload keystore once and keep it secret (never commit it):

```bash
keytool -genkey -v -keystore blockfall-release.keystore \
  -alias blockfall -keyalg RSA -keysize 2048 -validity 10000
```

In the Android preset's **Keystore → Release** section, set the release keystore
path, alias, and passwords (or supply them via environment variables / the export
CLI for CI). Prefer **Play App Signing**: you upload with this key; Google
manages the final signing key.

**c) AdMob + IAP plugins**

The `MobilePlatform` backend accesses ads/IAP through **Godot Android plugins**
exposed as engine singletons (`AdMob`, and the IAP plugin), so the C# compiles
even before the plugins are installed — calls simply warn and no-op.

1. Install the **AdMob Godot plugin** (Godot 4.x / v2 Android plugin). Copy its
   `addons/` + Android plugin `.gdap`/`aar` artifacts into `game/addons/` and
   enable it in **Project → Project Settings → Plugins**. Add your **AdMob App
   ID** and ad-unit IDs (interstitial + rewarded) to the plugin config.
2. Install the **Godot Play Billing / IAP plugin** the same way for the
   **remove-ads** IAP and cosmetics. Define the product IDs in Play Console and
   wire `PurchaseRemoveAds` / `RestorePurchases` to the plugin's purchase flow.
3. Interstitials are **frequency-capped 1-in-3 runs** and suppressed for premium
   users — this policy lives in `PlatformHub.MaybeShowInterstitial()`.

**d) Export**

```bash
godot --headless --path game --export-release "Android" build/android/Blockfall.aab
```

Upload the `.aab` to the **Google Play Console**. For on-device testing you can
export an APK instead by switching the preset's export format, or use
`--export-debug`.

### 5.3 iOS (App Store)

> **Full step-by-step release walkthrough (Korean): `docs/IOS_RELEASE.md`.**
> Two routes: **(A) Codemagic cloud build — no Mac needed** (`codemagic.yaml` at
> the repo root runs Godot export + signing + TestFlight upload on a cloud
> macOS M2 machine; needs the repo pushed to GitHub and an App Store Connect
> API key), or **(B) a local Mac** running **`./build-ios.sh`**.

Preset `preset.4` is release-ready:

- `application/export_project_only=true` — Godot generates the **Xcode project**
  (into `game/build/ios/`); signing/archiving/upload happen in Xcode with
  **automatic signing** (no hand-made certificates or profiles needed — just log
  your Apple ID into Xcode and pick the team).
- `icons/app_store_1024x1024="res://assets/ios/icon_1024.png"` — opaque
  1024×1024 marketing icon (generated from `icon.svg`; Godot scales the
  remaining icon slots automatically).
- `application/additional_plist_content` answers the export-compliance question
  (`ITSAppUsesNonExemptEncryption=false` — standard HTTPS only).
- `application/min_ios_version="12.0"`, portrait orientation.

`./build-ios.sh --team <TEAM_ID> [--bundle <BUNDLE_ID>] [--open]` checks the
toolchain (Xcode, .NET 8, Godot 4.3 .NET), auto-installs export templates,
injects the Team/Bundle ID into the preset, exports, and prints the
Archive→Upload steps. Godot 4.3's C# iOS support is **experimental** — always
verify on a real device via TestFlight before submitting.

**Ads & IAP on iOS.** `MobilePlatform` only reports `SupportsAds`/`SupportsIap`
when the native plugins are actually present (AdMob singleton; `InAppStore` /
`GodotGooglePlayBilling` for billing). Without them — the current state — the
store shows **free skins only** (no price-tagged rows, no remove-ads, no
watch-ad-to-revive button), which keeps the build compliant with App Store
review (no dead ad buttons, no purchases outside StoreKit — guidelines 2.1 /
3.1.1). To monetize later: install the **AdMob iOS Godot plugin** (plus
`NSUserTrackingUsageDescription` + SKAdNetwork ids in `Info.plist`) and the
**InAppStore** plugin, define the products in App Store Connect, and the paid UI
reappears automatically.

---

## 6. Store-backend summary

| Build | Feature tag | Backend | Ads | Premium | Store services |
|-------|-------------|---------|-----|---------|----------------|
| Editor / desktop dev | (none) | `NullPlatform` | no | always | none |
| Steam W/M/L | `steam` (custom) | `SteamPlatform` | no | always | GodotSteam achievements + leaderboards |
| Android / iOS | `mobile` (auto) | `MobilePlatform` | yes (1-in-3 cap) | via remove-ads IAP | AdMob + IAP |

All three implement `IPlatformServices`; the correct one is chosen at runtime by
`PlatformHub` (§4). Native plugins are accessed via `Engine.GetSingleton(...)`,
so a build missing a plugin degrades gracefully (logs a warning, no-ops) instead
of crashing.

---

## 7. Continuous integration

`.github/workflows/ci.yml` builds and tests the **engine-agnostic core** on every
push and PR to `main`. It intentionally does **not** invoke the Godot toolchain,
keeping the job fast and dependency-light — Godot exports run in a separate
release pipeline.

```yaml
jobs:
  core-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '8.0.x' }
      - run: dotnet restore Blockfall.sln
      - run: dotnet build   Blockfall.sln --configuration Release --no-restore
      - run: dotnet test    Blockfall.sln --configuration Release --no-build --verbosity normal
```

Because CI only touches `Blockfall.sln` (Core + Core.Tests), it needs nothing but
the **.NET 8 SDK** — no Godot install, no export templates, no native plugins.
Keep gameplay logic in `Blockfall.Core` so it stays covered by this gate.

To extend CI to produce game builds later, add a separate job using a Godot
export image/action (with the 4.3 .NET editor + export templates) and the
platform toolchains from §5 — but keep it off the fast core-test path.

---

## 8. Troubleshooting

- **C# not available / no build option in editor** → you installed the *standard*
  Godot, not the **.NET/Mono** build. Reinstall the .NET build of 4.3.
- **Missing assembly / `Blockfall.Core` not found** → run
  `dotnet build game/Blockfall.csproj`; ensure the .NET 8 SDK is on `PATH`.
- **Export template errors** → **Manage Export Templates** must match the exact
  4.3 build string reported by `godot --version`.
- **Steam backend inactive on desktop** → the preset is missing the `steam`
  custom feature, or `steam_appid.txt`/GodotSteam is absent. Check the log for
  `[Steam] initialized` vs. the "singleton not found" warning.
- **Ads never show on mobile** → confirm the AdMob plugin is installed and
  enabled, the user is not premium (`AdsRemoved`), and at least 3 runs have
  elapsed (1-in-3 frequency cap).
- **Android build template errors** → re-run **Project → Install Android Build
  Template** and confirm JDK 17 + SDK paths in Editor Settings.
