using Godot;
using System;
using Blockfall.Core;

namespace Blockfall.Platform;

/// <summary>
/// Desktop/editor fallback. No store, everything unlocked, no ads. Lets the game
/// run fully during development without any native plugin present.
/// </summary>
public sealed class NullPlatform : IPlatformServices
{
    public string Name => "Null (dev)";
    public bool SupportsAds => false;
    public bool SupportsIap => false;
    public bool IsPremium => true;

    public void Initialize() { }
    public void Process(double delta) { } // no native callbacks to pump
    public void SubmitScore(GameModeId mode, long score, double time)
        => GD.Print($"[Null] score {mode}={score} ({time:0.00}s)");
    public void ReportAchievements(RunStats stats, bool completed, GameModeId mode) { }
    public void ShowLeaderboard(GameModeId mode) { }
    public void LoadInterstitial() { }
    public void MaybeShowInterstitial() { }
    public void ShowRewardedAd(Action<bool> onReward) => onReward(true);
    public void PurchaseRemoveAds(Action<bool> onComplete) => onComplete(true);
    public void PurchaseItem(string productId, Action<bool> onComplete) => onComplete(true); // dev: everything free
    public void RestorePurchases() { }
}

/// <summary>
/// Steam backend via the GodotSteam GDExtension (exposes a global "Steam" object).
/// Paid game => always premium (no ads). Achievements + leaderboards are wired
/// through the singleton so we don't hard-depend on the plugin's C# bindings.
/// See docs/BUILD.md for GodotSteam setup + app_id.
/// </summary>
public sealed class SteamPlatform : IPlatformServices
{
    public string Name => "Steam";
    public bool SupportsAds => false;
    public bool SupportsIap => false;
    public bool IsPremium => true;

    // GodotSteam 4.x signal names — isolated here so a version bump is a one-line change.
    private const string SigFindResult = "leaderboard_find_result";
    private const string SigScoreUploaded = "leaderboard_score_uploaded";

    private GodotObject? _steam;
    // The score waiting to upload once its leaderboard handle resolves (findLeaderboard
    // is async: it fires leaderboard_find_result, and only then can we upload).
    private long? _pendingScore;

    public void Initialize()
    {
        if (!Engine.HasSingleton("Steam"))
        {
            GD.PushWarning("[Steam] GodotSteam singleton not found — running without Steam.");
            return;
        }
        _steam = Engine.GetSingleton("Steam");
        // steamInit is called with your app id configured in the export/GodotSteam.
        _steam.Call("steamInit");

        // Wire the async leaderboard flow: find -> (found) -> upload -> (uploaded) log.
        Connect(SigFindResult, Callable.From((long handle, long found) => OnLeaderboardFound(found != 0)));
        Connect(SigScoreUploaded, Callable.From((long success) =>
            GD.Print($"[Steam] leaderboard upload {(success != 0 ? "ok" : "failed")}")));

        GD.Print("[Steam] initialized");
    }

    /// <summary>Steam requires its callbacks pumped every frame; no-op if the plugin is absent.</summary>
    public void Process(double delta) => _steam?.Call("run_callbacks");

    public void SubmitScore(GameModeId mode, long score, double time)
    {
        if (_steam is null) return;
        // Time modes rank by milliseconds (ascending); score modes by points (descending).
        // Set each board's sort direction in the Steamworks partner site to match.
        _pendingScore = mode == GameModeId.Sprint ? (long)(time * 1000) : score;
        _steam.Call("findLeaderboard", $"LB_{mode}".ToUpperInvariant());
        // -> fires leaderboard_find_result -> OnLeaderboardFound uploads _pendingScore.
    }

    private void OnLeaderboardFound(bool found)
    {
        if (_steam is null || !found || _pendingScore is null) return;
        // uploadLeaderboardScore(score, keep_best) targets the just-found leaderboard.
        _steam.Call("uploadLeaderboardScore", _pendingScore.Value, true);
        _pendingScore = null;
    }

    private void Connect(string signal, Callable target)
    {
        // Connecting to a missing signal only warns (returns an error code), never
        // throws — so a GodotSteam version mismatch degrades gracefully.
        if (_steam is null) return;
        if (_steam.HasSignal(signal)) _steam.Connect(signal, target);
        else GD.PushWarning($"[Steam] signal '{signal}' not found on this GodotSteam version.");
    }

    public void ReportAchievements(RunStats stats, bool completed, GameModeId mode)
    {
        if (_steam is null) return;
        if (stats.Quads >= 1) Unlock("ACH_FIRST_TETRIS");
        if (stats.TSpins >= 1) Unlock("ACH_FIRST_TSPIN");
        if (stats.PerfectClears >= 1) Unlock("ACH_PERFECT_CLEAR");
        if (stats.MaxCombo >= 5) Unlock("ACH_COMBO_5");
        if (completed && mode == GameModeId.Marathon) Unlock("ACH_MARATHON_CLEAR");
        _steam.Call("storeStats");
    }

    private void Unlock(string id) => _steam?.Call("setAchievement", id);

    public void ShowLeaderboard(GameModeId mode)
        => _steam?.Call("activateGameOverlayToWebPage", "https://steamcommunity.com/stats");

    public void LoadInterstitial() { }
    public void MaybeShowInterstitial() { }
    public void ShowRewardedAd(Action<bool> onReward) => onReward(true);
    public void PurchaseRemoveAds(Action<bool> onComplete) => onComplete(true);
    // The paid Steam build includes cosmetics/boosters — grant instantly. Real
    // Steam microtransactions (ISteamMicroTxn) can replace this later if wanted.
    public void PurchaseItem(string productId, Action<bool> onComplete) => onComplete(true);
    public void RestorePurchases() { }
}

/// <summary>
/// Mobile backend: AdMob (interstitial + rewarded) and IAP remove-ads. Both use
/// their respective Godot Android/iOS plugins accessed via Engine singletons, so
/// this class compiles even before the native plugins are installed (calls just
/// no-op with a warning). See docs/MONETIZATION.md + docs/BUILD.md.
/// </summary>
public sealed class MobilePlatform : IPlatformServices
{
    public string Name => "Mobile";

    // Capability flags reflect what is ACTUALLY installed, not what the backend
    // could theoretically do. A store build without the native plugins must not
    // surface ad buttons that do nothing or price tags that can't charge —
    // App Store review rejects both (guidelines 2.1 / 3.1.1). With the plugins
    // absent, ads/paid-store UI hides itself and only free content remains.
    public bool SupportsAds => _admob is not null;
    public bool SupportsIap => Engine.HasSingleton(IosIapSingleton) || Engine.HasSingleton(AndroidBillingSingleton);
    public bool IsPremium => _save.Settings.AdsRemoved;

    // Billing plugin singleton names: godot-ios-plugins InAppStore (iOS) and the
    // Godot Play Billing plugin (Android). Adjust here if the plugin renames it.
    private const string IosIapSingleton = "InAppStore";
    private const string AndroidBillingSingleton = "GodotGooglePlayBilling";

    // Google AdMob sample/TEST unit ids — safe (and required) while developing.
    // REPLACE with your real ad units before release; never ship test ids or click
    // your own live ads. IAP product id is configured in Play Console / App Store Connect.
    private const string InterstitialUnitId = "ca-app-pub-3940256099942544/1033173712";
    private const string RewardedUnitId = "ca-app-pub-3940256099942544/5224354917";
    private const string RemoveAdsProductId = "com.blockfall.removeads";

    // AdMob plugin signal names (adjust to match your installed plugin/version).
    private const string SigRewardEarned = "rewarded_ad_reward";
    private const string SigInterstitialClosed = "interstitial_closed";

    private readonly SaveManager _save;
    private GodotObject? _admob;
    private Action<bool>? _pendingReward;

    public MobilePlatform(SaveManager save) => _save = save;

    public void Initialize()
    {
        if (!Engine.HasSingleton("AdMob"))
        {
            GD.PushWarning("[Mobile] AdMob singleton not found — ads disabled for this build.");
            return;
        }
        _admob = Engine.GetSingleton("AdMob");
        _admob.Call("initialize");

        // Grant the reward when the rewarded ad reports completion...
        Connect(SigRewardEarned, Callable.From(() =>
        {
            var cb = _pendingReward; _pendingReward = null;
            cb?.Invoke(true);
        }));
        // ...and preload the next interstitial as soon as one closes.
        Connect(SigInterstitialClosed, Callable.From(LoadInterstitial));
    }

    public void Process(double delta) { } // AdMob plugin pumps its own callbacks

    private void Connect(string signal, Callable target)
    {
        if (_admob is null) return;
        if (_admob.HasSignal(signal)) _admob.Connect(signal, target);
        else GD.PushWarning($"[Mobile] AdMob signal '{signal}' not found — check your plugin version.");
    }

    public void SubmitScore(GameModeId mode, long score, double time)
    {
        // Google Play Games / Game Center leaderboard submit goes here.
    }

    public void ReportAchievements(RunStats stats, bool completed, GameModeId mode) { }
    public void ShowLeaderboard(GameModeId mode) { }

    public void LoadInterstitial()
    {
        if (_admob is null || IsPremium) return;
        _admob.Call("load_interstitial", InterstitialUnitId);
    }

    public void MaybeShowInterstitial()
    {
        if (_admob is null || IsPremium) return;
        _admob.Call("show_interstitial");
    }

    public void ShowRewardedAd(Action<bool> onReward)
    {
        if (_admob is null) { onReward(false); return; }
        _pendingReward = onReward;
        _admob.Call("load_rewarded", RewardedUnitId);
        _admob.Call("show_rewarded");
        // -> the plugin's reward signal (SigRewardEarned) invokes _pendingReward(true).
        // Also wire the plugin's "closed/failed" signal to invoke it with false so a
        // dismissed ad doesn't leave the callback pending.
    }

    public void PurchaseRemoveAds(Action<bool> onComplete)
    {
        // Kick off the IAP plugin purchase flow for RemoveAdsProductId. On the
        // plugin's purchase-success signal, persist the entitlement via GrantRemoveAds().
        if (!SupportsIap)
        {
            // No billing plugin ⇒ never grant paid entitlements for free on a
            // store build. (The store UI is hidden in this state anyway.)
            GD.PushWarning("[Mobile] PurchaseRemoveAds called without a billing plugin — refused.");
            onComplete(false);
            return;
        }
        GrantRemoveAds();
        onComplete(true);
    }

    public void PurchaseItem(string productId, Action<bool> onComplete)
    {
        // Kick off the billing plugin flow for the given product id (same plugin
        // path as remove-ads); the plugin's purchase-success signal should invoke
        // onComplete(true) and its cancel/fail signal onComplete(false).
        if (!SupportsIap)
        {
            GD.PushWarning($"[Mobile] PurchaseItem('{productId}') called without a billing plugin — refused.");
            onComplete(false);
            return;
        }
        onComplete(true);
    }

    public void RestorePurchases()
    {
        // Query owned products; if RemoveAdsProductId is owned, call GrantRemoveAds().
        // Non-consumable catalog items (themes) should re-grant via SaveManager too.
    }

    private void GrantRemoveAds()
    {
        _save.Settings.AdsRemoved = true;
        _save.SetSettings(_save.Settings);
    }
}
