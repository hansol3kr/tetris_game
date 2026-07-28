# Blockfall — Roadmap

**Status: 2026-07-28, after the v1.5.0 tag.** Rewritten from a code audit, not from the
previous roadmap — the old version had drifted badly in *both* directions (it called
shipped features "future" and future features "shipped"). Every row below was verified by
reading the code and tracing entry paths. Open product decisions live in
[DECISIONS.md](DECISIONS.md); this file only records **what is true today**.

> **The one distinction that matters here: `built` ≠ `reachable`.**
> A large amount of this codebase is complete, unit-tested, and unreachable from the menu.
> A roadmap that counts those as "shipped" is how the project lost track of them. Every
> row therefore carries a reachability verdict, and "built but unreachable" is **not**
> a form of shipped.

---

## 1. What a player can actually reach today

The entire menu is `MainMenu.cs:87-94`. There are five gameplay entry points and five utility screens.

| Menu entry | What it launches | Verdict |
|---|---|---|
| **PLAY** (hero card) | Block Fit — drag/place puzzle, no gravity (`SceneRouter.cs:105` → `:257`) | ✅ reachable |
| **DAILY CHALLENGE** | Block Fit on today's shared seed (`SceneRouter.cs:98` → `:361`) | ✅ reachable |
| **DESCENT** | Block Fit survival — garbage accelerates with score (`SceneRouter.cs:106` → `:264`) | ✅ reachable |
| **VERSUS → CPU** | Block Fit placement duel vs bot (`SceneRouter.cs:101` → `:308` → `:314`) | ✅ reachable |
| **VERSUS → ONLINE** | Falling-block 1v1 over ENet direct connect or relay Quick Match (`:285` → `:296` → `:302`) | ✅ reachable |
| HOW TO PLAY | Falling-block tutorial — **teaches mechanics no reachable mode uses** (`:102` → `:238`) | ⚠️ see DECISIONS D-002 |
| STORE | 31 themes + 11 burst-FX artifacts, ownership + equip | ✅ reachable |
| PROFILE | Stats / achievements / leaderboards / rank tabs | ⚠️ renders, but **permanently empty** — DECISIONS D-003 |
| REPLAYS | Replay library browser | ⚠️ renders, but **permanently empty** — nothing reachable records a replay |
| SETTINGS | Handling (DAS/ARR), key rebinding, language, colorblind, reduced motion, text scale | ✅ reachable |

**In one sentence: the shipping product is Block Fit, plus an online falling-block versus mode.**

---

## 2. Built, tested, and NOT reachable

This is the most important section in the file. None of the following is reachable by a
player in a shipped build; several are covered by substantial test suites.

| Feature | Where it lives | Why it is unreachable |
|---|---|---|
| **All falling solo modes** (Marathon, Sprint, Ultra, Zen, Dig, Survival, Master) | `core/GameMode.cs` presets; UI grid `MainMenu.cs:276` | `BuildModeGrid()` has **0 callers**; `ModeChosen` is never emitted |
| **Custom run** (7 modifiers + seed entry) | `MainMenu.cs:340` | `BuildCustomRun()` has **0 callers**; `SeedEntered` never emitted |
| **Descent — charm-draft gauntlet** (5 escalating strata, drafted rule-bending charms) | `core/RunDirector.cs`, `core/Charm.cs`, `core/CharmSet.cs`, `UI/CharmDraftScreen.cs`, `UI/DescentResultsScreen.cs` — **42 core tests** | The menu's DESCENT card routes to Block Fit survival instead (`SceneRouter.cs:106`). `StartDescent`/`GoToCharmDraft` have 0 production callers |
| **Results screen** (+ score/achievement/replay/interstitial hooks) | `UI/ResultsScreen.cs` | Only built from `GoToResults` ← `GameController.RunFinished`, itself unreachable |
| **CPU falling versus** (garbage battle) | `Gameplay/VersusController.cs` | **Never constructed anywhere** — the whole file is dead |
| **Replay recording** | `ReplayRecorder` via `GameController.cs:92` | Only the falling `GameController` records; Block Fit records nothing |
| **Second Chance revive** (ad or booster) | `GameController.cs:268-353` | Lives only in `GameController`. Block Fit's game-over offers RETRY / MENU only |
| **Career stats · 17 achievements · local leaderboards** | `core/Progression/` | Fed only by `SaveManager.RecordRun` / `SubmitResult`, called only from dead paths |
| **Ranked anti-cheat re-simulation** | `core/Replay/ReplayValidator.cs` | Fully implemented and tested; **0 production callers** |

The falling **engine** itself is *not* dead — it still runs in the tutorial
(`TutorialController.cs:56`), online versus (`NetVersusController.cs:74`), and the replay
viewer (`ReplayViewer.cs:33`). What is unreachable is `GameController`, the wrapper that
adds modes, scoring, records, ghost race, and replay recording.

---

## 3. Corrections to the previous roadmap

The old file's claims, checked against code:

| Old claim | Reality |
|---|---|
| "Daily Challenge — **future**, S–M" | **Shipped** (as Block Fit): `core/GameMode.cs:146-155`, `Gameplay/DailyChallenge.cs`, `SceneRouter.cs:355`, one-attempt bookkeeping via `SaveManager.SubmitDaily` |
| "More modes (Cheese / Survival / Master 20G) — **future**, S–M each" | **All three built as data**: `GameMode.cs:157-188` (DigRace / Survival / Master). Unreachable (§2) |
| "Localization — *the one milestone the codebase does not pre-wire*" | **Flatly false.** `core/Localization/Loc.cs` + `LocData.cs` ship a full runtime with a Korean dictionary, settings picker, and tests. Implemented as an English-source-keyed C# dictionary, **not** the `.po`/`tr()` approach the old file prescribed |
| Online Versus: "still open — **ranked ladder**" | **Shipped**: `core/Online/RankSystem.cs` (Elo, 7 tiers), persisted, applied at `NetVersusController.cs:310-314`, Quick Match forces ranked, ladder UI in `NetLobbyScreen.cs:85-87`, tested. **But the Elo math is effectively inert** — `SaveManager.cs:358` passes the player's own rating as the opponent's, so expected score is always 0.5 |
| Cosmetics & themes — "future, M–L" | **Shipped**: 31 themes + 11 burst artifacts, ownership, equip, live store previews. Music packs were part of the plan and are **not** built (0 hits in the catalog) |
| "covered by the **46-test** core.tests suite" | **374** executed cases (302 `[Fact]` + 72 `[InlineData]` rows across 12 `[Theory]`) |
| "Single-player modes: Marathon, Sprint 40, Ultra 2:00, Zen" | 10 `GameModeId` values exist with 10 `ById` cases (`GameMode.cs:215-228`) |
| Accessibility — "ship 2–3 alternate palettes (deuteran/protan/tritan)" | **One** colorblind set behind a single bool (`Theme/Palette.cs:68`, `:224-233`). Key rebinding and the DAS/ARR/ghost handling UI **did** ship (`SettingsScreen.cs:129-130`, `:310`) |
| Leaderboards — "finish Steam upload on find-result callback" | **Done** (`Platforms.cs:76-98`). Mobile `SubmitScore`/`ReportAchievements`/`ShowLeaderboard` are still **empty bodies** (`Platforms.cs:199-205`). Friend/relative views not built |
| Replays "still open: cloud sync" | Still accurate — `SaveMerge` moves save JSON only, never `.brp` files |

---

## 4. Shipped since v1.0 and never recorded here

| Feature | Where |
|---|---|
| **Block Fit** — the drag/place puzzle that is now the main game, plus its CPU versus | `core/BlockFit/`, `Gameplay/BlockFitController.cs`, `BlockFitVersusController.cs` |
| **Rules V2 + `RulesVersion` branching** — per-action lock resets, tick-quantised DAS/ARR, standard 180° kick table, with V1 preserved bit-identically for old recordings | `core/RulesVersion.cs`, `core/Sim.cs`, golden fixtures in `core.tests/LegacyReplayGoldenTests.cs` |
| **Cloud save merge** — conflict-free, keeps every best, never double-counts | `core/Progression/SaveMerge.cs` |
| **Finesse tracking** | `core/Finesse.cs` (25 tests) |
| **Ghost race** | `GameController.cs:42,146`; `SceneRouter.cs:336` |
| **Localization runtime** (English / Korean) | `core/Localization/` |
| **Deterministic bot** — 5 tiers to 2-ply lookahead, published heuristic (landing height, eroded cells, row/col transitions, holes, cumulative wells); bot-vs-bot matches are unit-tested | `core/Bot/` |
| **Build hardening** — `tools/godot-guard.sh` watchdog after a CI import hang burned a 90-minute budget and lost a release | `tools/godot-guard.sh` |

---

## 5. Not built

| Item | Note |
|---|---|
| UMP (GDPR) consent flow, ATT prompt | **0 code**. `docs/MONETIZATION.md` §5 requires consent *before* the first ad request. Blocks any real ad serving |
| Real AdMob unit IDs | Google's public **test** IDs are hard-coded (`Platforms.cs:156-157`) with no debug/release swap |
| Native ad + billing plugins | No `.gdap` / plugin in the repo. `PurchaseItem` grants items **without calling any billing flow** (`Platforms.cs:246-256`) |
| `RestorePurchases` | Empty body behind a live Restore button (`Platforms.cs:260-264`, `StoreScreen.cs:127`) |
| Mobile leaderboard / achievement submit | Empty bodies (`Platforms.cs:199-205`) |
| Seasons / MMR-based matchmaking | Rating exists; the matchmaker pairs by queue order and no rating is sent |
| Lobbies of 3+ | Not built |
| Replay cloud sync | Not built |
| Music packs as a cosmetic kind | Not built |
| Bundle SKUs (cosmetic bundle, supporter bundle) | Priced in `docs/MONETIZATION.md`, absent from the catalog |
| Store-fetched localized prices | `PriceLabel` is a hard-coded placeholder (`StoreCatalog.cs:22,35`) |

---

## 6. What happens next

**Sequencing is not decided here.** Nine open decisions gate it — most of all whether the
falling game is formally retired (D-001), which determines whether §2 becomes cleanup or
becomes the next release. See [DECISIONS.md](DECISIONS.md).

Two items are unblocked and independent of every open decision:

1. **D-000** — remove the prohibited trademark string from player- and store-facing
   surfaces. Inviolable rule, currently violated.
2. **D-006** — ASC Beta App Information + the Codemagic GitHub secret. This is the only
   thing standing between the team and real-device feedback, and *every* release commit
   to date ends with "실기기 테스트는 미수행".

> **Note on effort estimates:** the old file carried T-shirt sizes (S/M/L/XL) for every
> milestone. They have been removed rather than refreshed — they were authored before any
> of this shipped and none of them was ever checked against actual elapsed time. Estimates
> return when there is a basis for them.
