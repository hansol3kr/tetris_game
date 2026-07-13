# Blockfall — Roadmap

Blockfall is an original neon falling-block puzzle built on Godot 4.3 + C# (.NET 8),
with a pure, engine-agnostic rules library (`Blockfall.Core`) sitting under the game
project. The core is deterministic, allocation-light on the hot path, and holds no
engine types — which is the single most important fact for everything below: most
future milestones are *presentation and plumbing* around a rules engine that already
supports them.

Effort estimates are rough T-shirt sizes for a small team:
**S** = days · **M** = 1–2 weeks · **L** = 3–6 weeks · **XL** = a quarter+.

---

## v1.0 — Shipped scope

The baseline release. Everything here exists and is covered by the 46-test
`core.tests` suite plus the Godot presentation layer.

- **Rules engine (`Blockfall.Core`):** SRS rotation with wall kicks, 7-bag
  randomizer, hold, ghost, hard/soft drop, lock delay with move reset (cap 15),
  T-spin detection (3-corner rule, mini vs full), combo, back-to-back, and perfect
  clear. Fully deterministic given a seed + input/`dt` sequence.
- **Single-player modes:** Marathon (level cap 15), Sprint 40, Ultra 2:00, Zen.
  Modes are declarative data (`GameMode`): goal, gravity, start level, top-out
  behavior, and a `GameConfig` of timing knobs.
- **Presentation:** neon/minimal art, additive bloom via `WorldEnvironment`, soft
  particles, colorblind-conscious hues (`Theme/Palette`), HUD, ghost, previews.
- **Input:** DAS/ARR handling for keyboard, gamepad, and touch through a shared
  `InputController`; virtual touch buttons reuse the same timing path.
- **Platform / monetization:** one `IPlatformServices` interface with Steam, Mobile
  (AdMob + IAP), and Null (dev) backends selected at runtime by OS feature tags
  (`"mobile"` / `"steam"`). Interstitials frequency-capped 1-in-3 runs; rewarded
  ads, remove-ads IAP, achievements, and leaderboard hooks stubbed per platform.
- **Persistence:** local per-mode bests + settings via `SaveManager` (JSON in
  `user://`), including DAS/ARR/ghost/volume in `GameSettings`.
- **Targets:** Android, iOS, Steam (Windows/macOS/Linux).

---

## Future milestones (prioritized)

### 1. Daily Challenge — fixed seed · **S–M**
Everyone plays the identical piece sequence for the day and competes on the same
board.

- **Existing seams:** `Game.Create(modeId, seed)` already injects a seed, and
  `SceneRouter.GenerateSeed()` explicitly notes "daily-challenge mode would pass a
  fixed seed instead." The `XorShiftRandom` generator was deliberately chosen over
  `System.Random` precisely so a seed produces the *same* sequence on mobile and
  Steam across .NET versions.
- **Work:** derive the seed from the UTC date, add a daily entry point + one-attempt
  bookkeeping in `SaveManager`, and a results/leaderboard surface. No engine changes.

### 2. Replays — deterministic input log — ✅ **SHIPPED**
Record a run and play it back exactly; also the substrate for anti-cheat on
leaderboards and for shareable clips.

- **Delivered:** a fixed **60 Hz** simulation clock (`Sim.TickDt`), a per-tick
  `Buttons` bitmask, and a deterministic `InputProcessor` (DAS/ARR + finesse) that the
  live solo game now runs through — so play is frame-rate independent. `ReplayRecorder`
  stores *(seed, mode, modifiers, DAS/ARR, one Buttons byte per tick)*; `ReplayData`
  serializes to a compact versioned blob; `ReplayPlayer` re-simulates it. `ReplayViewer`
  plays it back with pause + 1-2-4× speed. Round-trip is unit-tested to be bit-identical.
- **Also delivered:** a disk **replay library** (`ReplayStore`, gzip .brp files),
  a browser to watch/delete/import, copy-paste **share codes** (gzip+base64), and
  **`ReplayValidator`** — deterministic re-simulation for **ranked anti-cheat**.
- **Still open:** cloud sync of the replay library.

### 3a. CPU Versus — ✅ **SHIPPED**
Real-time 1v1 garbage battle against a heuristic AI. This landed the entire local
half of versus:
- `VersusMatch` runs two `Game` instances on the **same piece seed** (fair) and
  routes **net** garbage (`Game.GarbageSentToOpponent`, after own-incoming
  cancellation) into the opponent's `ReceiveGarbage`, committing on lock via
  `CommitPendingGarbageOnLock`.
- `BotPlayer` + `BotEvaluator` — a deterministic **El-Tetris** heuristic AI (landing
  height, eroded cells, row/col transitions, holes, cumulative wells) with **five**
  difficulty tiers up to **Grandmaster (2-ply lookahead)**. Because it plays through
  the same command API a human uses and is fully deterministic, **bot-vs-bot matches
  are unit-tested** (e.g. Master and Grandmaster complete Sprint 40).
- Godot `VersusController` renders both boards with incoming-garbage meters.

### 3b. Online Versus — ✅ **SHIPPED** (direct connect + matchmaking)
Real-time 1v1 against **humans**.

- **Delivered:** ENet P2P (`NetPeer`, host/join by IP) AND **Quick Match**
  matchmaking — a Node WebSocket relay server (`server/`, tested) that pairs
  players and forwards frames across NAT, reached via a transport-agnostic
  `INetChannel` (`RelayChannel`). Reliable-ordered `NetProtocol` (board/piece
  snapshots + authoritative `Attack` garbage), lobby, rematch, forfeit —
  cross-platform. See [docs/NETWORKING.md](NETWORKING.md).
- **Still open:** ranked ladder + small lobbies (3+), and hosting/scaling the
  matchmaker as a managed service.

### 4. More modes — Cheese / Survival / Master (20G) · **S–M each**
- **Existing seams:** `GameMode` is declarative ("adding a new mode is data, not
  code"), and `GameConfig.MaxGravityLevel` + `GravityForLevel` already model 20G
  ("gravity effectively becomes instant"). `Board` supports pre-filling garbage.
- **Work:** *Master 20G* is nearly free — a preset with a low/instant gravity level.
  *Cheese* needs a garbage-fill spawn routine (reuses the versus garbage API) and a
  "dig to the bottom" goal predicate. *Survival* needs a periodic rising-garbage rule.
  Each new goal type may need a small addition to `GameMode.IsGoalReached`.

### 5. Accessibility — colorblind palettes, remappable keys, handling UI · **S–M**
- **Existing seams:** `Theme/Palette.ForPiece()` is the single source of piece color;
  `InputController` reads *named* input actions (`move_left`, `rotate_cw`, …) from the
  Godot input map; `GameSettings` already persists `DasSeconds`/`ArrSeconds`/ghost and
  `AudioManager.ApplySettings` already consumes settings.
- **Work:** ship 2–3 alternate `Palette` sets (deuteran/protan/tritan) plus an optional
  per-piece glyph/pattern overlay; a key-rebinding screen that writes `InputMap`
  overrides to the save; and a settings panel exposing the DAS/ARR/ghost knobs that
  already flow into `GameConfig`. Mostly UI + persistence, no engine change.

### 6. Leaderboards & social · **M** (per platform)
- **Existing seams:** `IPlatformServices` already declares `SubmitScore`,
  `ShowLeaderboard`, and `ReportAchievements`; the Steam backend sketches
  `findLeaderboard`/achievement unlocks and the mobile backend has the Play
  Games/Game Center submit stubbed. `RunStats` exposes the metrics
  (score, time, `PiecesPerSecond`) leaderboards rank on.
- **Work:** finish the platform SDK callbacks (Steam leaderboard upload on the
  find-result callback; Play Games/Game Center auth + submit), add friend/relative
  leaderboard views, and — once Replays land — attach a replay to each score for
  verification. Best done alongside Daily Challenge, which gives leaderboards their
  most compelling board.

### 7. Cosmetics & themes · **M–L**
Alternate block skins, backgrounds, particle/bloom looks, and music packs — the
non-ad half of the mobile monetization plan and premium flair on Steam.

- **Existing seams:** all look is centralized — `Palette` for colors, a
  `WorldEnvironment` for bloom, `AudioManager` for music — so a "theme" is a data
  bundle swapped at runtime. IAP/entitlement plumbing (`IPlatformServices`,
  `PurchaseRemoveAds`/`RestorePurchases`, `IsPremium`) is the template for owning
  cosmetics.
- **Work:** define a `Theme` resource (palette + background + particles + music),
  a picker UI, ownership tracking in the save, and store SKUs. The rendering seams
  exist; the effort is content plus an entitlement/catalog layer generalizing the
  existing remove-ads purchase.

### 8. Localization · **M**
- **Existing seams:** none yet — this is the one milestone the codebase does *not*
  pre-wire; UI strings are currently inline (e.g. mode names like "Sprint 40" live in
  `GameMode`). Flagging it early keeps the debt small.
- **Work:** extract strings into Godot `.po`/CSV translation tables, route UI text
  through `tr()`, separate display names from `GameModeId`, and handle font coverage
  (CJK) + layout reflow. Cheap if done before the UI grows; increasingly painful after.

---

## Suggested sequencing

1. **Daily Challenge** + **Leaderboards** — small, high-retention, and they exercise
   the seed/submit seams together.
2. **Replays** — unlocks score verification and is a prerequisite for trustworthy
   competitive play.
3. **Accessibility** + **More modes** — cheap wins on top of existing data-driven
   seams; good "content update" fodder.
4. **Localization** — schedule *before* the UI surface area balloons.
5. **Cosmetics/themes** — monetization depth once the audience is established.
6. **Online Versus** — the flagship, built last on the determinism, garbage-cell, and
   replay foundations the earlier milestones harden.
