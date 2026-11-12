using System;
using Unity.Services.Analytics;

namespace Wagenheimer.Tracking
{
    /// <summary>
    /// Typed events for the questions a casual/puzzle game has to answer: where do players quit, which
    /// levels are too hard, what makes them pay, which ads they watch. Names are fixed (lowerCamelCase) so
    /// every game reports the same taxonomy and the same dashboards work across games.
    /// </summary>
    public static partial class GameAnalytics
    {
        // ---------------------------------------------------------------- progression

        /// <summary>A level begins. <paramref name="attempt"/> counts tries of the same level (1 = first).</summary>
        public static void LevelStart(int level, int attempt = 1, string mode = null) =>
            Track("levelStart", ("level", level), ("attempt", attempt), ("mode", mode));

        /// <summary>A level is won. The funnel is levelStart -> levelComplete.</summary>
        public static void LevelComplete(int level, float seconds, int moves, int stars = 0, int score = 0,
            int attempt = 1, string mode = null) =>
            Track("levelComplete", ("level", level), ("seconds", (int)seconds), ("moves", moves), ("stars", stars),
                ("score", score), ("attempt", attempt), ("mode", mode));

        /// <summary>A level is lost. <paramref name="reason"/> examples: "noMoves", "timeUp", "quit".</summary>
        public static void LevelFail(int level, string reason, float seconds, int moves, int attempt = 1,
            string mode = null) =>
            Track("levelFail", ("level", level), ("reason", reason), ("seconds", (int)seconds), ("moves", moves),
                ("attempt", attempt), ("mode", mode));

        /// <summary>The player leaves a level without finishing (menu, restart, app closed).</summary>
        public static void LevelQuit(int level, float seconds, int moves, string via = null) =>
            Track("levelQuit", ("level", level), ("seconds", (int)seconds), ("moves", moves), ("via", via));

        /// <summary>A power-up, hint or booster is used. Shows which levels need help.</summary>
        public static void PowerUpUsed(string type, int level, bool paid = false) =>
            Track("powerUpUsed", ("type", type), ("level", level), ("paid", paid));

        /// <summary>A tutorial step is shown or completed. Drop-off between steps is the first-session funnel.</summary>
        public static void TutorialStep(string step, int index, bool completed = true) =>
            Track("tutorialStep", ("step", step), ("index", index), ("completed", completed));

        /// <summary>An achievement is unlocked.</summary>
        public static void AchievementUnlocked(string id) => Track("achievementUnlocked", ("id", id));

        // ---------------------------------------------------------------- economy

        /// <summary>
        /// Virtual currency moves. <paramref name="source"/> is where it came from or went to
        /// ("levelReward", "powerUp", "iap", "rewardedAd").
        /// </summary>
        public static void Economy(string currency, bool earned, long amount, string source, long balance) =>
            Track(earned ? "currencyEarned" : "currencySpent", ("currency", currency), ("amount", amount),
                ("source", source), ("balance", balance));

        // ---------------------------------------------------------------- monetization

        /// <summary>The paywall or unlock screen is shown. <paramref name="trigger"/> says why (e.g. "level16").</summary>
        public static void PaywallShown(string trigger, int level) =>
            Track("paywallShown", ("trigger", trigger), ("level", level));

        /// <summary>The player dismissed the paywall without buying.</summary>
        public static void PaywallDismissed(string trigger, int level) =>
            Track("paywallDismissed", ("trigger", trigger), ("level", level));

        /// <summary>The player tapped a buy button.</summary>
        public static void PurchaseStarted(string productId, int level) =>
            Track("purchaseStarted", ("productId", productId), ("level", level));

        /// <summary>A purchase did not finish: <paramref name="reason"/> is "cancelled", "failed" or "unavailable".</summary>
        public static void PurchaseFailed(string productId, string reason) =>
            Track("purchaseFailed", ("productId", productId), ("reason", reason));

        /// <summary>The player restored previous purchases.</summary>
        public static void PurchasesRestored(bool foundAny) => Track("purchasesRestored", ("foundAny", foundAny));

        /// <summary>
        /// A completed real-money purchase, as Unity's standard <c>transaction</c> event so revenue shows up in
        /// the built-in dashboards. <paramref name="priceMinorUnits"/> is in the smallest unit (cents).
        /// </summary>
        public static void Purchase(string productId, string currencyCode, long priceMinorUnits, string transactionId,
            string storeId = null)
        {
            var e = new TransactionEvent
            {
                TransactionName = productId,
                TransactionId = transactionId,
                TransactionType = TransactionType.PURCHASE,
                ProductId = productId,
                SpentRealCurrency = new TransactionRealCurrency
                {
                    RealCurrencyType = currencyCode,
                    RealCurrencyAmount = priceMinorUnits
                }
            };
            if (!string.IsNullOrEmpty(storeId)) e.StoreId = storeId;
            Record(e);
        }

        /// <summary>A rewarded video or interstitial was shown (the player-facing event, for ad frequency).</summary>
        public static void AdShown(AdPlacementType type, string placement, bool completed, int level = 0) =>
            Track("adShown", ("type", type.ToString()), ("placement", placement), ("completed", completed),
                ("level", level));

        /// <summary>
        /// Ad revenue for one impression (from the ad network's impression-level revenue callback), as Unity's
        /// standard <c>adImpression</c> event so ad revenue joins the same dashboards as IAP.
        /// </summary>
        public static void AdImpression(string adSource, string placementId, string placementName, AdPlacementType type,
            double revenueUsd, bool completed = true)
        {
            Record(new AdImpressionEvent
            {
                AdCompletionStatus = completed ? AdCompletionStatus.Completed : AdCompletionStatus.Incomplete,
                AdProvider = AdProvider.IrnSource, // LevelPlay (ironSource) mediation
                PlacementId = string.IsNullOrEmpty(placementId) ? placementName : placementId,
                PlacementName = placementName,
                PlacementType = type,
                AdEcpmUsd = revenueUsd * 1000.0,
                AdSource = adSource
            });
        }

        // ---------------------------------------------------------------- settings / app

        /// <summary>The player changed the language.</summary>
        public static void LanguageChanged(string language) => Track("languageChanged", ("language", language));

        /// <summary>A screen or form was opened (use sparingly: menus, store, settings).</summary>
        public static void ScreenView(string screen) => Track("screenView", ("screen", screen));
    }
}
