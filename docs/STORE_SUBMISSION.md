# Blockfall — Store Submission Guide

Submission checklists for **Google Play**, **Apple App Store**, and **Steam**. Blockfall is a Godot 4.3 + C#/.NET 8 falling-block puzzle. Mobile builds are free with AdMob (interstitial frequency-capped 1-in-3 runs + rewarded ads, remove-ads IAP, cosmetics); Steam is paid premium with no ads. Keep this document updated each release.

---

> ## ⚠️ TRADEMARK — READ FIRST (non-negotiable) ⚠️
>
> **"Tetris", "Tetrimino", "Tetromino"-as-brand, and the classic Tetris look are protected by The Tetris Company (TTC), which enforces its rights aggressively** — takedowns, store delistings, and legal action against clones are routine and well documented across all three platforms below.
>
> **Absolute rules for every store listing, screenshot, trailer, keyword field, metadata, and in-game text:**
>
> - **DO NOT** use the word **"Tetris"** anywhere — not in the title, subtitle, description, keywords, promo text, screenshots, trailer, filenames, package/bundle IDs, URLs, or ASO keyword fields. Not even "like Tetris", "Tetris-style", "better than Tetris", or "the Tetris you love".
> - **DO NOT** use TTC's trademarked/associated terms: **"Tetrimino"**, and avoid marketing the game as generic "tetromino" gameplay in a way that trades on the Tetris brand. Our pieces are **"blocks"** / **"pieces"**.
> - **DO NOT** copy the **iconic Tetris color scheme** for the 7 pieces (the specific cyan-I / yellow-O / purple-T / green-S / red-Z / blue-J / orange-L palette as presented by TTC), the "GAME BOY" green-LCD styling, or any distinctive Tetris visual trade dress. Use our **original neon palette** (colorblind-friendly hues, additive glow) that is deliberately distinct.
> - **DO NOT** replicate TTC marketing art, logos, fonts, the falling-block logo lockup, or sound effects.
> - **DO** use only the original brand: **"Blockfall"**, a neon falling-block puzzle. Describe mechanics generically ("clear lines", "rotate pieces", "hold", "hard drop") without invoking Tetris.
> - **DO** keep a defensive record: our name, art, and audio are independently created; the rules engine implements generic public-domain falling-block mechanics (line clears, 7-bag, wall kicks) under our own naming.
>
> **Note on mechanics vs. brand:** game *rules/mechanics* are generally not copyrightable, but the **name, specific color trade dress, logo, and "Tetris" brand ARE protected**. Our exposure is almost entirely in *branding and presentation* — keep those 100% original. If in doubt on any asset, remove the Tetris association. A single "Tetris" slip in a keyword field is enough to trigger a takedown.

---

## Pre-submission — applies to all three stores

- [ ] **Original branding audit**: grep the entire repo, all store metadata, screenshots, and trailer for "tetris"/"tetrimino" (case-insensitive). Must return zero hits. (See Trademark section.)
- [ ] Final build number / version string bumped and consistent across `game/Blockfall.csproj`, platform export presets, and store listing.
- [ ] Privacy policy published at a stable public URL (see Privacy section). Same URL reused on all stores.
- [ ] Support/contact email live and monitored: `hansolkr5@gmail.com` (or a dedicated support alias).
- [ ] Backend feature-tag selection verified: `mobile` build ships AdMob + IAP; `steam` build ships neither (no ad SDK, no ad permissions) — confirm the Steam build has the ad SDK and network/ad permissions stripped.
- [ ] Age rating questionnaires answered consistently across stores (same content = same answers).
- [ ] Screenshots/trailer captured from the real neon build at required resolutions.
- [ ] Legal: EULA / terms if applicable; open-source license attributions bundled (Godot, .NET, any NuGet deps) in an in-game "Licenses" screen.

---

## 1. Google Play (Android)

### Account & fees
- **Google Play Developer account**: one-time **$25 USD** registration fee.
- Organizations must complete **D-U-N-S** verification; individual accounts require identity + (for newer accounts) closed testing with **12+ testers for 14 days** before production access. Budget time for this.
- Set up a **Google Payments merchant profile** for IAP and paid content.

### Build upload format
- **Android App Bundle (`.aab`)** — required for new apps (APK no longer accepted for new titles).
- Godot 4.3: export via the **Android** preset with **"Use Gradle Build"** enabled, gradle build → `.aab`; ensure the C#/.NET export template is installed and the correct **NDK/JDK** are configured.
- **Signing**: enroll in **Play App Signing** (Google holds the app signing key; you upload with an upload key). Keep the upload keystore backed up securely.
- Target the **current required target API level** (Play enforces a minimum `targetSdkVersion` — verify the current requirement at submission time; typically "latest − 1").
- 64-bit native libs required (Godot/.NET provide arm64-v8a; include armeabi-v7a only if you still support 32-bit).

### Store listing assets
- **App icon**: 512×512 PNG (32-bit, with alpha), ≤ 1 MB.
- **Feature graphic**: 1024×500 PNG/JPG (required; shown at top of listing and for featuring).
- **Phone screenshots**: 2–8, PNG/JPG, 16:9 or 9:16, each side 320–3840 px.
- **Tablet screenshots** (7" and 10"): recommended for tablet visibility.
- **Promo/trailer video**: optional YouTube URL.
- **Text**: app title (≤ 30 chars), short description (≤ 80 chars), full description (≤ 4000 chars). **No "Tetris" anywhere.**

### Age rating
- **IARC questionnaire** in Play Console → generates ratings for all regions (ESRB, PEGI, USK, etc.) automatically. Puzzle game with no objectionable content typically rates **Everyone / PEGI 3** — but you **must** disclose that the app **contains ads** and **in-app purchases** in the questionnaire.

### Privacy policy & Data safety
- **Privacy policy URL is mandatory** (app handles ads/IDs).
- **Data safety form** (App content → Data safety): declare all collection/sharing. **AdMob collects data on mobile** — you must declare, at minimum:
  - **Device or other IDs** (Advertising ID / GAID) — collected, shared, used for **Advertising or marketing**.
  - **App activity / interactions**, and possibly **approximate location** and **diagnostics**, per AdMob's disclosure guidance.
  - Mark data as collected **and shared** (AdMob = third party). Follow Google's official AdMob "Data safety" mapping guidance.
- Declare the **Advertising ID permission** (`com.google.android.gms.permission.AD_ID`) in the manifest for API 33+.
- **Families / ads policy**: if the app could target children, extra rules apply — Blockfall targets a general audience; set target age to teen/adult to avoid Families Policy complications, and gate personalized ads by consent (see UMP/GDPR below).
- Implement **Google UMP SDK** (consent) for EEA/UK GDPR + serve non-personalized ads where required; comply with **US state privacy** signals.

### Content guidelines
- Comply with **Play Developer Program Policies**: no misleading metadata, ads must not be deceptive/interfere with gameplay, IAP disclosed, no impersonation/IP infringement (**Tetris trade dress = infringement risk — see Trademark section**).
- Ad placement: interstitials must not show unexpectedly during active play or on app open in a disruptive way; our 1-in-3-runs cap shows them at run end — compliant.

### Review timeline
- New personal accounts: initial access can take **days to weeks** (incl. testing requirement). Established accounts: reviews typically **hours to a few days**; can be longer for new apps or if flagged.

### Launch steps
1. Create app in Play Console → set default language, app/game category (**Puzzle**), free/paid.
2. Complete **App content**: privacy policy, ads declaration, Data safety, content rating (IARC), target audience, news/COVID declarations as prompted.
3. Upload `.aab` to **Internal testing** → verify AdMob (test IDs first), IAP, remove-ads restore.
4. Set up **in-app products**: `remove_ads` (managed product / non-consumable) + cosmetics; test purchases with license testers.
5. Promote to **Closed → Open/Production**; set countries, pricing (free), rollout %.
6. Submit for review; monitor **Policy status** and pre-launch report (crashes, ANRs).
7. Staged rollout → 100%.

---

## 2. Apple App Store (iOS)

### Account & fees
- **Apple Developer Program**: **$99 USD/year**.
- Requires a Mac with **Xcode** for building/signing/notarization and uploading.
- Set up certificates, an **App ID**, and **provisioning profiles**; enable capabilities (In-App Purchase). Manage banking/tax in **App Store Connect → Agreements** (paid apps agreement must be active for IAP).

### Build upload format
- **`.ipa`** archived and uploaded via **Xcode Organizer** or **Transporter**, distributed through **TestFlight** first, then submitted for App Store review.
- Godot 4.3 C#: export the **iOS** preset → generates an Xcode project; open in Xcode, set signing team, archive, and upload. Confirm .NET/C# iOS export templates and the iOS toolchain are installed.
- Provide required **encryption compliance** answer (`ITSAppUsesNonExemptEncryption` in Info.plist — standard HTTPS = exempt, set to `false` if only using standard crypto).

### Store listing assets
- **App icon**: 1024×1024 PNG, **no alpha, no transparency, no rounded corners** (Apple rounds it). Also ship the full in-app icon set (asset catalog handles device sizes).
- **Screenshots** (required per device size uploaded to App Store Connect):
  - **6.9"/6.7" iPhone**: 1290×2796 (or 1320×2868) portrait — required.
  - **6.5" iPhone**: 1242×2688 / 1284×2778 — often still required as fallback.
  - **iPad 12.9"/13"**: 2048×2732 — required **if** the app supports iPad.
  - Up to 10 screenshots per localization/size.
- **App preview video** (optional): 15–30 s, per device size, captured from device.
- **Text**: name (≤ 30 chars), subtitle (≤ 30), promotional text (≤ 170), description (≤ 4000), keywords (100-char comma list — **no "Tetris" keyword**), support URL, marketing URL.

### Age rating
- Answer Apple's **age-rating questionnaire** in App Store Connect (Apple's own system, moving toward regional/IARC-aligned bands). A clean puzzle game rates around **4+**. Disclose nothing objectionable; note the game is ad-supported (no explicit "contains ads" toggle like Play, but ad content must fit the rating — keep ad content rating filtered via AdMob max ad content rating **G**).

### Privacy policy & App Privacy ("Nutrition Label")
- **Privacy policy URL mandatory** (App Store Connect → App Information).
- **App Privacy** questionnaire → generates the privacy "nutrition label". **AdMob collects data on mobile** — declare per AdMob's Apple mapping, typically:
  - **Identifiers**: Device ID (IDFA) → used for **Third-Party Advertising**, **linked** or not per your config.
  - **Usage Data**, **Diagnostics**, possibly **Coarse Location**, per AdMob's disclosure.
  - Mark data **"used for tracking"** if you serve personalized ads across apps → this triggers **App Tracking Transparency (ATT)**.
- **App Tracking Transparency (ATT)**: if using IDFA/personalized ads, you **must** present the `ATTrackingManager` prompt with an `NSUserTrackingUsageDescription` string, before/at ad init. Without consent, request **non-personalized ads**. Coordinate ATT with the **Google UMP** consent flow.
- Add required Info.plist usage strings (`NSUserTrackingUsageDescription`; SKAdNetwork IDs for AdMob + mediation partners in `SKAdNetworkItems`).

### Content guidelines
- **App Store Review Guidelines**: no copycat/IP-infringing apps (**5.2 — Tetris trade dress is a rejection/legal risk**), ads must be appropriate to age rating and not disruptive, IAP must use Apple's system for digital goods (remove-ads + cosmetics = **must** use StoreKit IAP, not external payment). Provide **Restore Purchases**.
- Guideline **2.1** completeness: reviewers must be able to reach all modes; if anything is gated, provide a demo note. Ensure ads don't block review (test ad fill).

### Review timeline
- **TestFlight** external testing requires a lighter **Beta App Review** (usually ~1 day).
- **App Store review**: typically **~24–48 hours**, occasionally longer. Expedited review available for critical cases.

### Launch steps
1. App Store Connect → create app record; bundle ID, name, primary language, category (**Games → Puzzle**), price (free).
2. Configure **In-App Purchases**: `remove_ads` (Non-Consumable) + cosmetics; submit IAP metadata (can be reviewed with the build).
3. Archive in Xcode → upload build → wait for processing → assign to **TestFlight**; test AdMob (test units), ATT prompt, IAP purchase + **restore**.
4. Fill **App Privacy**, age rating, privacy policy URL, encryption compliance.
5. Add screenshots/preview per device size, description, keywords (**Tetris-free**).
6. Select build → **Submit for Review** (choose manual or automatic release).
7. On approval → **Release**; monitor crashes in Xcode Organizer / App Store Connect.

---

## 3. Steam (Windows / macOS / Linux)

### Account & fees
- **Steamworks Distribution / Steam Direct**: **$100 USD recoupable fee per app** (recouped after $1,000 in adjusted gross revenue).
- Complete company/individual **tax & bank (Payoneer/wire) verification** and digital paperwork before the store page can go live.
- Steam is **paid premium — no ads, no ad SDK**. Confirm the ad SDK, AdMob keys, and ad/network permissions are **absent** from the Steam build (feature tag `steam`).

### Build upload format
- **Steam depots via SteamPipe**, using **`steamcmd`** or the **SteamPipe GUI**. Define **depots** per platform (Windows/macOS/Linux) and **build scripts** (`app_build_*.vdf`, `depot_build_*.vdf`).
- Godot 4.3 C#: export **Windows Desktop**, **macOS**, and **Linux/X11** presets (self-contained .NET where possible). Upload each platform's exported files into its depot; set the correct **launch options** per OS in Steamworks.
- **macOS**: sign and **notarize** the `.app` with an Apple Developer ID (required for Gatekeeper) before packaging into the macOS depot.
- Integrate **Steamworks SDK** (via a C# wrapper such as Steamworks.NET / Facepunch.Steamworks) for achievements, cloud saves, and the required App ID init; guard all Steam calls behind the `steam` feature tag.

### Store listing assets (Steam has many specific sizes)
- **Header capsule**: 460×215.
- **Small capsule**: 231×87.
- **Main capsule**: 616×353.
- **Vertical capsule**: 374×448.
- **Page background**: 1438×810 (optional).
- **Library assets** (separate from store): Library capsule 600×900, Library header 460×215, Library hero 3840×1240, Library logo (transparent PNG).
- **Screenshots**: minimum 5, **1920×1080** recommended (16:9).
- **Trailer/video**: strongly recommended; upload source (e.g., 1920×1080, H.264) — trailers greatly affect conversion.
- **Text**: name, short description, full description (supports Steam BBCode), tags/genres.

### Age rating
- No mandatory global rating gate to publish, but Steam runs a **content survey** and supports **regional ratings** (e.g., **IARC** for some regions, **USK** for Germany). Fill the **content survey** honestly; a clean puzzle game needs no mature descriptors. Provide age ratings where required for specific territories.

### Privacy policy & data
- Steam build has **no AdMob and collects no advertising data** — the mobile data-safety concerns **do not apply**. If you integrate Steam features (achievements/leaderboards/cloud), data is handled by Valve; disclose any additional telemetry you add in a privacy policy linked on the store page. Keep the same published policy URL, noting the Steam build serves no ads.
- Comply with **Steam Subscriber Agreement** and **Steamworks Documentation** rules.

### Content guidelines
- Follow **Steamworks Onboarding / Distribution Agreement** and content rules: no infringing IP (**Tetris trade dress = removal risk**), accurate store page, working build, no misleading system requirements.
- **No ads / no external-payment schemes** — monetization is the upfront purchase + optional cosmetics (if sold, via Steam's systems / DLC).

### Review process & timeline
- Two review passes:
  - **Store page review**: Valve reviews your store assets/description before the page goes public (a few business days).
  - **Build review / release readiness**: Valve reviews the game build for launch; you must have the store page live **at least ~2 weeks** before release (Steam requires the coming-soon page up ≥ 2 weeks prior).
- Build/store reviews typically take **1–5 business days** per round; plan buffer.

### Launch steps
1. Steam Direct: pay $100, complete tax/bank/identity paperwork, get the **App ID**.
2. Configure **Steamworks**: app name, launch options per OS, supported OS/languages, controller support, cloud, achievements/stats.
3. Build **depots** (Win/macOS/Linux) and upload via **SteamPipe** (`steamcmd +run_app_build`); set the **Default** branch.
4. Build the **store page**: capsules, library assets, screenshots, trailer, description, tags, genre (**Puzzle/Casual**), pricing (premium, per-region).
5. Submit **store page for review**; publish **Coming Soon** page **≥ 2 weeks** before launch.
6. Submit **build for release review**; run **playtests/beta branch** with keys.
7. Set the **release date**; on approval, hit **Release** — the app goes live at the scheduled time.
8. Post-launch: monitor Steam discussions, reviews, and crash reports; push updates via SteamPipe.

---

## Cross-store summary table

| Item | Google Play | Apple App Store | Steam |
|---|---|---|---|
| Account fee | $25 one-time | $99/year | $100/app (recoupable) |
| Build format | `.aab` (Play App Signing) | `.ipa` via TestFlight | Depots via SteamPipe |
| App icon | 512×512 (alpha) | 1024×1024 (no alpha) | Capsules (multiple) |
| Store hero art | Feature graphic 1024×500 | (screenshots/preview) | Capsules + Library hero 3840×1240 |
| Screenshots | 2–8, 320–3840 px | Per device size (e.g. 1290×2796) | ≥5, 1920×1080 |
| Age rating | IARC (declare ads + IAP) | Apple questionnaire (4+) | Content survey + regional (USK/IARC) |
| Privacy label | Data safety (AdMob!) | App Privacy + ATT (AdMob!) | Minimal (no ads) |
| Ads/monetization | AdMob + IAP | AdMob + StoreKit IAP + ATT | None (premium) |
| Review time | hours–weeks | ~24–48 h | 1–5 business days/round |

*Sizes, target API levels, and required device screenshot dimensions change over time — verify current requirements in each store's official console at submission time.*

---

## Final reminder

**Every asset, every text field, every screenshot on all three stores must be 100% original Blockfall branding with zero "Tetris"/"Tetrimino" references and no Tetris color trade dress. The Tetris Company enforces aggressively. When in doubt, strip the association.**
