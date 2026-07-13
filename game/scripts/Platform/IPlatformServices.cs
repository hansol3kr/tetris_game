using Blockfall.Core;

namespace Blockfall.Platform;

/// <summary>
/// One interface the whole game codes against, regardless of store. Concrete
/// implementations (Steam / Mobile / Null) are selected at build time by
/// <see cref="PlatformHub"/>. This keeps monetization + platform SDK calls out
/// of gameplay code and makes each store swappable/testable.
/// </summary>
public interface IPlatformServices
{
    string Name { get; }
    bool SupportsAds { get; }
    bool SupportsIap { get; }

    void Initialize();

    /// <summary>Pump native callbacks each frame (Steam needs this; others no-op).</summary>
    void Process(double delta);

    // Leaderboards / achievements.
    void SubmitScore(GameModeId mode, long score, double time);
    void ReportAchievements(RunStats stats, bool completed, GameModeId mode);
    void ShowLeaderboard(GameModeId mode);

    // Ads (mobile free tier only).
    void LoadInterstitial();
    void MaybeShowInterstitial();
    void ShowRewardedAd(System.Action<bool> onReward);

    // In-app purchases.
    void PurchaseRemoveAds(System.Action<bool> onComplete);
    /// <summary>Buy a catalog product (theme / booster pack) by its store product id.
    /// The CALLER grants the item on success — platforms only run the payment flow.</summary>
    void PurchaseItem(string productId, System.Action<bool> onComplete);
    void RestorePurchases();
    bool IsPremium { get; }

    // Cloud save. Default = unsupported (local-only). A store backend that has a
    // cloud (Steam Cloud, Google Play Saved Games, iCloud) overrides these; the
    // conflict merge itself lives in the tested core (SaveMerge), so a backend only
    // has to move opaque JSON blobs up and down.
    bool SupportsCloud => false;
    void CloudSave(string json) { }
    void CloudLoad(System.Action<string?> onLoaded) => onLoaded(null);
}
