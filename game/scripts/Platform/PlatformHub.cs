using Godot;
using System;
using Blockfall.Core;

namespace Blockfall.Platform;

/// <summary>
/// Selects and hosts the active <see cref="IPlatformServices"/> for this build and
/// forwards convenience calls. Build symbols (set per export preset) decide which
/// backend is compiled in:
///   GAME_STEAM  -> Steam achievements + leaderboards, no ads, paid = always premium.
///   GAME_MOBILE -> AdMob ads + IAP remove-ads.
///   (neither)   -> NullPlatform for desktop/editor development.
/// </summary>
public partial class PlatformHub : Node
{
    private IPlatformServices _services = null!;
    private int _runsSinceAd;

    public bool IsPremium => _services.IsPremium;
    public bool SupportsAds => _services.SupportsAds;
    public bool SupportsIap => _services.SupportsIap;

    public void Initialize()
    {
        // Runtime selection. Mobile exports report the "mobile" feature; Steam
        // desktop builds add a custom "steam" feature tag in their export preset
        // (see docs/BUILD.md). The editor/dev build falls back to NullPlatform.
        if (OS.HasFeature("mobile"))
            _services = new MobilePlatform(Bootstrap.Instance.Save);
        else if (OS.HasFeature("steam"))
            _services = new SteamPlatform();
        else
            _services = new NullPlatform();

        _services.Initialize();
        _services.LoadInterstitial();
        GD.Print($"[Platform] Active backend: {_services.Name}");
    }

    public override void _Process(double delta) => _services?.Process(delta);

    public void SubmitScore(GameModeId mode, long score, double time) => _services.SubmitScore(mode, score, time);
    public void ReportAchievements(RunStats stats, bool completed, GameModeId mode)
        => _services.ReportAchievements(stats, completed, mode);
    public void ShowLeaderboard(GameModeId mode) => _services.ShowLeaderboard(mode);

    /// <summary>
    /// Show an interstitial only every few runs, never for premium users, and never after a mode
    /// that <see cref="AdPolicy"/> exempts.
    ///
    /// <para>The mode is a REQUIRED argument on purpose. It used to take none, so the two results
    /// screens called it knowing the mode and unable to say so, and the Zen exclusion that
    /// docs/MONETIZATION.md has required since the cap was written simply never existed. Making it
    /// a parameter means a future caller cannot fail to answer the question.</para>
    ///
    /// <para>The exemption is checked BEFORE <c>_runsSinceAd</c> moves: counting an exempt run
    /// would just push the ad onto the player's next run, which is the same interruption one screen
    /// later.</para>
    /// </summary>
    public void MaybeShowInterstitial(GameModeId mode)
    {
        if (!AdPolicy.AllowsInterstitial(mode)) return;
        if (!_services.SupportsAds || _services.IsPremium) return;
        _runsSinceAd++;
        if (_runsSinceAd >= 3) // frequency cap: 1 in 3 runs
        {
            _runsSinceAd = 0;
            _services.MaybeShowInterstitial();
            _services.LoadInterstitial(); // preload the next one
        }
    }

    public void ShowRewardedAd(Action<bool> onReward) => _services.ShowRewardedAd(onReward);
    public void PurchaseRemoveAds(Action<bool> onComplete) => _services.PurchaseRemoveAds(onComplete);
    public void PurchaseItem(string productId, Action<bool> onComplete) => _services.PurchaseItem(productId, onComplete);
    public void RestorePurchases() => _services.RestorePurchases();

    // ---- Cloud save ---------------------------------------------------------

    public bool SupportsCloud => _services.SupportsCloud;

    /// <summary>
    /// Reconcile local ↔ cloud: pull the cloud blob, merge it into the local save
    /// (tested <c>SaveMerge</c> keeps every best), then push the reconciled result
    /// back up. No-op on backends without a cloud. Best-effort; failures are silent.
    /// </summary>
    public void SyncCloud()
    {
        if (!_services.SupportsCloud) return;
        var save = Bootstrap.Instance.Save;
        _services.CloudLoad(json =>
        {
            if (!string.IsNullOrEmpty(json)) save.MergeCloudJson(json!);
            _services.CloudSave(save.ExportJson());
        });
    }
}
