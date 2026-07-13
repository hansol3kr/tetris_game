# Monetization — Blockfall

Blockfall ships a **hybrid** monetization model, chosen entirely at **runtime**
by OS feature tag, so a single codebase serves every store without conditional
compilation in gameplay code:

| Build | Feature tag | Backend (`IPlatformServices`) | Money model |
|-------|-------------|-------------------------------|-------------|
| Steam (Win/macOS/Linux) | `steam` | `SteamPlatform` | **Paid premium** — one-time purchase, no ads, `IsPremium == true` always |
| Android / iOS | `mobile` | `MobilePlatform` | **Free** — AdMob interstitial + rewarded ads, remove-ads IAP, cosmetics IAP |
| Editor / desktop dev | (none) | `NullPlatform` | Everything unlocked, no ads, `IsPremium == true` |

Selection happens once in `PlatformHub.Initialize()`:

```csharp
if (OS.HasFeature("mobile"))      _services = new MobilePlatform(Bootstrap.Instance.Save);
else if (OS.HasFeature("steam"))  _services = new SteamPlatform();
else                              _services = new NullPlatform();
```

All of gameplay/UI codes only against the `IPlatformServices` interface (or the
`PlatformHub` convenience wrapper). No monetization SDK call ever leaks into the
rules engine (`Blockfall.Core`) or into scene scripts.

---

## 1. Steam premium (paid, no ads)

`SteamPlatform` (`game/scripts/Platform/Platforms.cs`) is a paid product, so it
is intentionally hard-wired:

- `SupportsAds => false`
- `SupportsIap => false`
- `IsPremium => true` — never shows an ad, never gates a feature behind a purchase.

Because `PlatformHub.MaybeShowInterstitial()` early-returns when
`IsPremium` is `true` or `SupportsAds` is `false`, Steam players can never see an
interstitial even if ad code is called from shared UI. Steam builds monetize
purely through the store list price (and optional cosmetic DLC later).

Steam-side services (`SubmitScore`, `ReportAchievements`, `ShowLeaderboard`) go
through the GodotSteam singleton; they are unrelated to monetization but share
the same interface.

---

## 2. Mobile free tier

`MobilePlatform` is the only backend that monetizes at runtime:

- `SupportsAds => true`
- `SupportsIap => true`
- `IsPremium => _save.Settings.AdsRemoved` — flips to premium once the remove-ads
  IAP is owned. From that moment ad code paths self-disable exactly like Steam.

### 2.1 Interstitial ads (frequency-capped 1-in-3)

Interstitials are the primary free-tier revenue source. Two guards keep them
non-intrusive:

1. **Frequency cap — 1 in 3 runs.** `PlatformHub` counts finished runs in
   `_runsSinceAd`; only every 3rd call actually surfaces an ad:

   ```csharp
   public void MaybeShowInterstitial()
   {
       if (!_services.SupportsAds || _services.IsPremium) return;
       _runsSinceAd++;
       if (_runsSinceAd >= 3) // frequency cap: 1 in 3 runs
       {
           _runsSinceAd = 0;
           _services.MaybeShowInterstitial();
           _services.LoadInterstitial(); // preload the next one
       }
   }
   ```

2. **Premium / no-ads short-circuit.** If the player bought remove-ads
   (`AdsRemoved == true`), `IsPremium` is `true` and the method returns before
   incrementing the counter — no ad, ever.

The next interstitial is preloaded immediately after each show, and once at
startup via `PlatformHub.Initialize() -> _services.LoadInterstitial()`, so the ad
is warm by the time it's needed (no black-screen stall).

**Call site:** `MaybeShowInterstitial()` must be called **only from the results
screen**, after a run ends — never during play. See §4.

### 2.2 Rewarded ads

Opt-in, player-initiated video that grants a concrete in-game benefit:

```csharp
public void ShowRewardedAd(Action<bool> onReward) => _services.ShowRewardedAd(onReward);
```

Use cases:

- **Retry / continue** — watch an ad to retry a Sprint 40 / Ultra run without
  losing the seed, or to resurrect once.
- **Cosmetic unlock** — earn a one-off cosmetic (skin/palette/trail) via a
  rewarded view instead of paying.

The `onReward` callback receives `true` only if the ad completed and the reward
signal fired. On `NullPlatform`/`SteamPlatform` it returns `true` immediately
(reward always granted in dev/premium), which keeps the reward UI testable in the
editor.

### 2.3 Remove-ads IAP

A one-time non-consumable purchase that turns the free tier into an ad-free
experience:

```csharp
public void PurchaseRemoveAds(Action<bool> onComplete)
{
    // Trigger the IAP plugin purchase flow; on success:
    _save.Settings.AdsRemoved = true;
    _save.SetSettings(_save.Settings);   // persists to user://blockfall_save.json
    onComplete(true);
}
```

`AdsRemoved` lives in `GameSettings` (`SaveManager.cs`) and is persisted to the
`user://` sandbox, so the entitlement survives restarts. `RestorePurchases()`
must re-set `AdsRemoved = true` when the store reports the product as owned (for
reinstalls / new devices) — a store restore is legally required on iOS.

### 2.4 Cosmetics IAP

Non-consumable cosmetic packs (board skins, block palettes, particle/trail FX,
music themes). These are purely visual and **must not affect gameplay**
(colorblind-friendly hues remain available for free). Purchase flow mirrors
remove-ads: on success, persist the owned cosmetic id in the save and unlock it
in the cosmetics UI. Cosmetics are the natural upsell for players who bought
remove-ads and can no longer be shown ads.

---

## 3. Where to plug in real IDs and SDKs

Everything below is currently a no-op / warning stub so the project compiles and
runs before native plugins are installed. Replace the marked spots at
integration time.

### 3.1 AdMob unit IDs

`MobilePlatform` talks to the AdMob Godot plugin via the `AdMob` engine
singleton. The **ad unit IDs** belong in the plugin/export configuration, not in
gameplay code:

- Configure per-platform app IDs and ad unit IDs (interstitial + rewarded) in the
  AdMob plugin's export settings / config resource.
- Keep **separate Android and iOS** unit IDs.
- Use Google's **test unit IDs** in debug builds; swap to production IDs only for
  store builds. Never ship test IDs, and never click your own live ads.
- The plugin calls to wire up: `initialize`, `load_interstitial`,
  `show_interstitial`, `load_rewarded`/`show_rewarded`, plus the
  `rewarded_ad_reward` signal → route it into the `onReward` callback in
  `MobilePlatform.ShowRewardedAd`.

```
Google AdMob console  ─▶  app ID + unit IDs (interstitial, rewarded)  ─▶  AdMob plugin config
                                                                          (per Android / iOS)
```

### 3.2 IAP product IDs

`PurchaseRemoveAds`, cosmetics purchases, and `RestorePurchases` call the IAP
plugin (Google Play Billing on Android, StoreKit on iOS). Product IDs are
declared in each store console and referenced by the plugin:

| Product | Type | Suggested product ID |
|---------|------|----------------------|
| Remove ads | non-consumable | `blockfall.removeads` |
| Cosmetic — neon pack | non-consumable | `blockfall.cosmetic.neon` |
| Cosmetic — retro pack | non-consumable | `blockfall.cosmetic.retro` |

Wire the plugin's `purchase` / `product_purchased` / `restore` callbacks to the
`onComplete` handlers. Product IDs must match the store console exactly and be the
same string across code, Play Console, and App Store Connect.

### 3.3 Steam app ID / stat + achievement IDs

Steam monetization is just the store price, but the achievement IDs
(`ACH_FIRST_TETRIS`, `ACH_FIRST_TSPIN`, `ACH_PERFECT_CLEAR`, `ACH_COMBO_5`,
`ACH_MARATHON_CLEAR`) and leaderboard names (`LB_<MODE>`) must be created in the
Steamworks partner site, and the `app_id` set in the GodotSteam export config
(see `docs/BUILD.md`).

---

## 4. Ad placement — best practices

**Golden rule: never interrupt gameplay.** An interstitial mid-run is the single
fastest way to tank retention and reviews. Placement rules:

- **Interstitials fire on the results screen only** — after a run has fully
  ended (top-out, Sprint goal reached, Ultra time expired, or player-quit to
  menu). Call `PlatformHub.MaybeShowInterstitial()` from the results-screen
  controller, not from the gameplay scene.
- **Never** during a run, on pause, on level-up, on line-clear, or on the
  hard-drop that ends a run — no ad while the board is visible/interactive.
- **1-in-3 cap** is enforced centrally in `PlatformHub`; do not add extra ad
  calls elsewhere. Zen mode (endless, relaxing) should skip interstitials
  entirely to protect the mode's intent — gate the call so Zen never triggers it.
- **Rewarded ads are always opt-in** — shown only from an explicit button
  ("Watch to retry", "Watch to unlock"), never auto-played.
- **Preload** before showing (already handled) so the transition is instant.
- Respect a short cooldown after app launch — don't show an interstitial on the
  very first results screen of a fresh session if it feels punishing; the 1-in-3
  cap already softens this.

```
run ends ─▶ results screen shown ─▶ PlatformHub.MaybeShowInterstitial()
                                     (every 3rd run, non-premium, non-Zen)
```

---

## 5. GDPR / ATT consent

Ads and IAP process personal data, so consent must be collected **before** the
first ad request.

### 5.1 UMP (Google User Messaging Platform) — GDPR/CCPA

- On mobile launch, run the **UMP consent flow** (via the AdMob plugin's consent
  API) *before* `LoadInterstitial()`/`initialize` requests any personalized ad.
- Gather consent info, load & present the consent form if required (EEA/UK/CCPA
  regions), and only then initialize AdMob and request ads.
- Provide an in-settings "Privacy options" / "Manage consent" entry so users in
  applicable regions can change their choice later (required by UMP).
- If consent is denied, AdMob serves non-personalized ads — still monetizes,
  stays compliant.

### 5.2 iOS App Tracking Transparency (ATT)

- On iOS 14.5+, call `ATTrackingManager.requestTrackingAuthorization` (via the
  plugin) **after** UMP but **before** requesting ads.
- Declare `NSUserTrackingUsageDescription` in the iOS export `Info.plist`.
- If the user declines tracking, still serve ads (non-personalized) — do not
  block the game or nag.
- Order matters: **UMP consent → ATT prompt → AdMob initialize → first ad load.**

Because `MobilePlatform.Initialize()` is where `AdMob.initialize` is called,
the consent gating should be inserted immediately before that call so no ad SDK
request precedes consent.

---

## 6. Reward / entitlement flow (summary)

```
Rewarded ad:
  UI button ─▶ PlatformHub.ShowRewardedAd(onReward)
            ─▶ MobilePlatform: AdMob.show_rewarded
            ─▶ plugin "rewarded_ad_reward" signal ─▶ onReward(true) ─▶ grant retry/cosmetic
  (denied / no fill) ─▶ onReward(false) ─▶ no reward, no penalty

Remove-ads IAP:
  UI button ─▶ PlatformHub.PurchaseRemoveAds(onComplete)
            ─▶ store purchase ─▶ Settings.AdsRemoved = true ─▶ SaveManager.Flush()
            ─▶ IsPremium == true ─▶ all interstitials suppressed

Restore (reinstall / new device):
  Settings ─▶ PlatformHub.RestorePurchases()
           ─▶ store reports owned ─▶ re-set AdsRemoved / cosmetics
```

---

## 7. Suggested pricing

Prices are USD anchors; localize per store with store-managed regional pricing.

| Item | Platform | Type | Suggested price |
|------|----------|------|-----------------|
| Blockfall (full game) | Steam | Paid, one-time | **$4.99** (launch **$3.99** / -20% intro) |
| Remove ads | Mobile | Non-consumable IAP | **$2.99** |
| Cosmetic pack (single theme) | Mobile | Non-consumable IAP | **$0.99 – $1.99** |
| Cosmetic bundle (all themes) | Mobile | Non-consumable IAP | **$4.99** |
| "Supporter" bundle (remove ads + all cosmetics) | Mobile | Non-consumable IAP | **$5.99** (bundle discount vs buying separately) |

Guidance:

- Keep **remove-ads** cheap and prominent — it is the highest-converting mobile
  purchase and directly improves the experience of your most engaged players.
- Offer the **Supporter bundle** as the anchor: it makes remove-ads look like the
  budget option and captures whales in one tap.
- Steam price stays low ($4.99) for a single-player puzzle; rely on volume,
  wishlists, and seasonal Steam sales rather than a high sticker price.
- Never gate core modes (Marathon / Sprint / Ultra / Zen) or colorblind palettes
  behind payment — only ads, convenience (retry), and cosmetics are monetized.

## Implemented catalog (2026-07-10)

The in-game store (`game/scripts/Platform/StoreCatalog.cs`, UI in
`game/scripts/UI/StoreScreen.cs`) now ships these items. Product ids must be
registered verbatim in Play Console / App Store Connect:

| Item id | Product id | Kind | Price anchor |
|---|---|---|---|
| `theme_neon_flux` | — (default, free) | Theme | FREE |
| `theme_sunset_drive` | `com.blockfall.theme.sunset` | Theme | $1.99 |
| `theme_deep_emerald` | `com.blockfall.theme.emerald` | Theme | $1.99 |
| `theme_mono_arcade` | `com.blockfall.theme.mono` | Theme | $1.99 |
| `booster_second_chance_3` | `com.blockfall.booster.secondchance3` | Consumable ×3 | $0.99 |
| `remove_ads` | `com.blockfall.removeads` | Non-consumable | $2.99 |

Fairness rules baked into code, do not relax them:

- **Second Chance** (revive: wipe board, keep run) is offered in solo modes
  only, never in Daily, never in any versus. A revived run sets **no local
  records and no leaderboard submissions** (`GameController.Finish`).
- A **rewarded ad** is offered as a free alternative to spending a Second
  Chance (mobile), keeping the booster monetization honest.
- Themes are pure cosmetics (piece colors + backdrop). The colorblind palette
  always overrides theme piece colors; UI accents/gold never retint.
- Ownership/equip/booster counts persist in the local save
  (`SaveManager: OwnedItems/EquippedTheme/Boosters`); platforms only process
  payment (`IPlatformServices.PurchaseItem`) — the game grants items itself,
  so `RestorePurchases` must re-grant non-consumables on reinstall.
