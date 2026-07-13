# Blockfall

> A neon falling-block puzzle — original brand, modern feel.

**Blockfall** is a fast, glowing, minimalist stacking game built with **Godot 4.3 + C#**. Drop, rotate, and clear lines across eight single-player modes with a full modern rule set — SRS wall kicks, 7-bag randomizer, hold, ghost piece, T-spins, combos, back-to-back, and perfect clears — all wrapped in a dark, additive-glow neon aesthetic with colorblind-friendly hues. The rules engine is a pure, engine-agnostic C# library, so the hard parts are unit-tested and deterministic while Godot handles rendering, input, audio, and platform services.

<!-- SCREENSHOT PLACEHOLDER -->
<p align="center">
  <img src="docs/screenshot-neon.png" alt="Blockfall — neon gameplay screenshot" width="360">
  <br>
  <em>Neon gameplay — placeholder. Replace with a captured build screenshot.</em>
</p>

---

## Features

### Game modes (V1)
| Mode | Goal |
| --- | --- |
| **Marathon** | Clear lines and climb levels up to a level-15 cap. |
| **Sprint 40** | Clear 40 lines as fast as possible — the clock is your score. |
| **Ultra 2:00** | Score as much as possible within a 2-minute limit. |
| **Zen** | Relaxed, endless play with no clock and no goal. |
| **Daily Challenge** | A 2-minute score attack on a seed shared by everyone that day — same pieces for all players, leaderboard-ready. |
| **Dig Race** | Excavate 10 pre-filled garbage rows as fast as you can — a time attack. |
| **Survival** | Garbage rises faster and faster; your clears (attack) push it back. Last as long as you can. |
| **Master 20G** | Instant gravity + tight lock + all-spin scoring — pure execution. |
| **Versus CPU** | Real-time 1v1 garbage battle against a heuristic AI (Easy → **Grandmaster**, a 2-ply lookahead). Clear lines to send garbage, cancel incoming with your own attacks, KO your opponent. |
| **Versus Online** | The same 1v1 duel against a **human** — either **direct-connect by IP** (ENet P2P, no server) or **Quick Match** matchmaking that finds any opponent online via a relay server. |

### Depth & replayability
- **CPU Versus** — a real opponent to fight: a deterministic **El-Tetris heuristic AI** with five tiers up to **Grandmaster (2-ply lookahead)**, exchanging garbage on a second board. It lives in the pure-C# core, so bot-vs-bot matches are unit-tested.
- **Online Versus** — battle over ENet by IP, or **Quick Match** through the [matchmaking/relay server](docs/NETWORKING.md) (works across NAT). Ranked scores can be re-simulated for **anti-cheat** validation.
- **Ranked ladder** — Quick Match duels move an **Elo-style rating** across seven tiers (Bronze → Grandmaster). Provisional accounts converge fast, then settle; the rating math and tier bands are pure-C# and unit-tested. A mid-match disconnect never counts, so a flaky connection can't be farmed. *(Direct-connect IP matches are unranked.)*
- **Cloud save** — the whole save (bests, dailies, achievements, cosmetics, career stats, ladder) reconciles across devices with a **conflict-free merge** that keeps every best, unions collections, and never double-counts career totals. The merge is engine-agnostic and unit-tested; the platform backend (Steam Cloud / mobile) only moves opaque JSON blobs.
- **Deterministic replays** — every solo run is recorded as a per-tick input stream on a fixed 60 Hz clock: **watch it back** (play / pause / 1-2-4× speed), **save** it to a library, or **share** it as a copy-paste code. Same seed + same inputs = a bit-identical re-simulation.
- **Interactive tutorial** — a hands-on *How to Play* (auto-launched on first run) that gates progress on actually performing each mechanic — move, rotate, drop, hold, clear, **T-spin**, and **defend against garbage** — with keyboard and touch prompts.
- **Progression** — a **Profile** with career stats, **17 achievements** (unlock toasts), and per-mode **local leaderboards** (top-10, and you can watch the record run's saved replay). All aggregation logic is pure-C# and unit-tested.
- **Ghost race** — once you've set a leaderboard time, your best run replays in lockstep beside you and a live **GHOST ±N** readout shows whether you're ahead of your own record.
- **Attack & garbage** system (guideline attack table) — powers Survival's rising garbage and Dig Race, with a live **"SENT ×N"** readout
- **Finesse meter** — a real-time skill grade of your input economy: every piece is measured against the fewest keypresses that could have placed it (a deterministic solver), with a live **percent + letter rank (F→S)**, clean-placement streaks, and a per-run breakdown
- **All-Spin** — immobile S/Z/L/J/I spins score (on Master, or as a modifier)
- **Shareable seeds & replay** — every run has a short **seed code**; replay the exact board, or type any word as a seed
- **Custom modifiers** — stack **No-Hold / No-Ghost / Low-G / Blitz / Hard-Lock / All-Spin / Invisible** on any mode (Invisible hides the stack — play from memory, glimpsing it on each lock)

### Modern guideline rules
- **SRS** rotation system with full **wall kicks**
- **7-bag randomizer** for fair, predictable piece distribution
- **Hold** slot and **ghost/shadow** projection
- **Hard drop** and **soft drop**
- **Lock delay** with move reset (capped at 15 resets to prevent stalling)
- **T-spin** detection (3-corner rule; mini vs. full)
- **Combo**, **back-to-back**, and **perfect clear** scoring bonuses
- Tunable handling — DAS, ARR, gravity curve, spawn delay — via a single config

### Presentation & game feel
- Dark background with additive **bloom glow** (WorldEnvironment) — on by default on desktop; **off by default on mobile** (HDR 2D is a perf/driver risk across the device long tail) and always off on macOS, where hand-drawn glow underlays carry the neon look. Toggleable in settings everywhere it's supported
- **Line-clear particle bursts, screen shake, and floating popups** (TETRIS!, T-SPIN, COMBO ×N, PERFECT CLEAR!) — all asset-free and intensity-tunable
- A **colorblind-friendly** neon palette (Okabe–Ito), toggleable in settings
- **Cross-platform controls** — keyboard arrows/gamepad on desktop, and on phones **direct-manipulation touch**: grab the board and *drag the falling piece into place*, tap to rotate, flick down to hard-drop, flick up to hold (with a classic on-screen d-pad as a settings opt-out). Every gesture is funnelled through the same per-tick button stream the keyboard uses, so a touch-played run records and replays bit-for-bit

### Settings
- Audio (SFX / music / mute), visual comfort (ghost, colorblind palette, **finesse meter**, juice intensity), and handling (**DAS / ARR**, **drag vs. d-pad touch controls**) — persisted locally and applied to gameplay
- **Language** — English / 한국어, switchable live (rebuilds open UI instantly). Strings are keyed by their English source, so any untranslated string degrades gracefully to English rather than showing a raw key
- **Custom controls** — remap every keyboard action (click a key, press the new one); a collision **swaps** the two so nothing is ever left unbound. Stored as physical keycodes (layout-independent) and applied by repointing Godot's input map, so gameplay, recording, and replay are untouched. Gamepad and touch are unaffected
- **Accessibility** — a **text-size** scale that enlarges UI typography live without touching the play area, so the board never clips
- **Fullscreen** toggle on desktop (settings or **F11** anywhere); mobile is always fullscreen so the toggle is hidden there
- **Auto-pause on focus loss** — a live run pauses itself when the app is backgrounded (alt-tab or minimize on desktop; a call, notification, or home press on mobile), so no piece drops while you're away

---

## Tech stack & why

| Layer | Technology |
| --- | --- |
| Rules engine | **Blockfall.Core** — pure C# class library (.NET 8) |
| Tests | **xUnit** (247 passing) |
| Game / presentation | **Godot 4.3** with **.NET (C#)** |
| CI | GitHub Actions — builds and tests the core on every push/PR |

**Why a pure-C# core, separate from Godot?**

- **Testability** — SRS kicks, T-spin logic, and scoring are the easiest things to get subtly wrong. Isolating them from the engine lets us verify every edge case with `dotnet test`, fast and without launching Godot.
- **Maintainability** — gameplay logic stays free of rendering and input concerns, so each side can evolve independently.
- **Determinism** — the core is built deterministically, so seeds behave identically everywhere (important for fair scoring and future daily challenges).
- **Portability** — if the presentation engine ever changes, the rules come along unchanged.

---

## Repository layout

```
tetris_game/
├── Blockfall.sln            Solution: Core + Core.Tests
├── core/                    Blockfall.Core — pure C# rules engine (no engine deps)
│   ├── Game.cs              Top-level game loop / state machine
│   ├── Board.cs             Playfield grid + line clears
│   ├── Tetromino.cs         Piece shapes + SRS rotation states
│   ├── Piece.cs             Active piece state
│   ├── SpinDetector.cs      T-spin (mini/full) detection
│   ├── Randomizer.cs        7-bag piece generator
│   ├── Scoring.cs           Combo / B2B / perfect-clear scoring
│   ├── AttackTable.cs       Guideline garbage-sent table (attack)
│   ├── Finesse.cs           Finesse solver (BFS) + input-economy tracker
│   ├── GameMode.cs          Marathon / Sprint / Ultra / Zen / Dig / Survival / Master presets
│   ├── GameConfig.cs        Timing & handling knobs (gravity, DAS/ARR, lock delay)
│   ├── ModifierSet.cs       Stackable run modifiers (No-Hold, Invisible, …)
│   ├── SeedCode.cs          Short shareable seed codes
│   ├── GameEvents.cs        Events emitted to the presentation layer
│   └── Primitives.cs        Core value types
├── core.tests/              xUnit test suite (Board, Rotation, Scoring, Spin, …)
├── game/                    Godot 4.3 C# project — rendering / input / audio / platform
│   ├── Blockfall.csproj     References Blockfall.Core
│   ├── project.godot        Godot project config
│   ├── scenes/Main.tscn     Main scene
│   ├── shaders/             neon_glow.gdshader
│   ├── scripts/
│   │   ├── Gameplay/        GameController, BoardView, Hud, input, touch
│   │   ├── UI/              MainMenu, SceneRouter, ResultsScreen
│   │   ├── Audio/           AudioManager
│   │   ├── Theme/           Palette
│   │   └── Platform/        PlatformHub + services (ads / IAP / Steam)
│   └── assets/              fonts, images (audio is synthesized at runtime — no files)
├── docs/                    Project documentation
└── .github/workflows/       CI (core build + test)
```

---

## Quick start

**Prerequisites:** [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and [Godot 4.3 — .NET/Mono edition](https://godotengine.org/download).

**1. Clone**
```bash
git clone <repo-url> tetris_game
cd tetris_game
```

**2. Run the core tests** (no Godot required)
```bash
dotnet test Blockfall.sln
```
Expected: **247 passing** tests.

**3. Open the game in Godot**
- Launch the **Godot 4.3 .NET** editor.
- Import the project by selecting `game/project.godot`.
- Godot builds the C# solution automatically; press **Play** to run.

---

## Platform targets

- **Android** and **iOS** — free, ad-supported (AdMob interstitials frequency-capped 1-in-3 runs, plus rewarded ads), with a remove-ads IAP and cosmetics.
- **Steam** (Windows / macOS / Linux) — paid premium, no ads.

The store backend is selected **at runtime** via OS feature tags (`mobile` / `steam`), so a single codebase serves every platform without per-build compile symbols.

---

## Trademark notice

**Blockfall is an original game and is not affiliated with, endorsed by, or connected to Tetris® or The Tetris Company.** "Tetris" is a registered trademark of The Tetris Company, LLC. Blockfall uses only generic falling-block mechanics and its own name, brand, and art. Please do not refer to this project as "Tetris."

---

## Documentation

| Document | Description |
| --- | --- |
| [Architecture](docs/ARCHITECTURE.md) | Core ↔ Godot boundary, event flow, design rationale. |
| [Build & Export](docs/BUILD.md) | Per-platform setup, export presets, and runtime backend selection. |
| [Store Submission](docs/STORE_SUBMISSION.md) | Google Play / App Store / Steam checklists + the trademark section. |
| [Monetization](docs/MONETIZATION.md) | Ads, IAP, and the mobile/Steam service split. |
| [Networking](docs/NETWORKING.md) | Direct-connect + matchmaking server, relay protocol, and ranked anti-cheat. |
| [Roadmap](docs/ROADMAP.md) | Shipped v1 scope and prioritized post-launch milestones. |
| [Assets](game/assets/README.md) | Audio/art drop-in layout and naming conventions. |
