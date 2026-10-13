using PaintMaze.Domain;

namespace PaintMaze.Services
{
    /// <summary>
    /// Idempotent cosmetic-coin grants backed by <see cref="SaveService"/>.
    /// </summary>
    public static class CoinService
    {
        /// <summary>
        /// Grants campaign coins when <paramref name="levelIndex"/> is the current
        /// first-clear target (<see cref="SaveService.HighestUnlocked"/>). Call
        /// before <see cref="SaveService.MarkCompleted"/>.
        /// </summary>
        public static int TryGrantCampaignFirstClear(
            Difficulty difficulty,
            int levelIndex)
        {
            difficulty = DifficultyCatalog.NormalizePlayable(difficulty);
            if (levelIndex < 1) return 0;
            if (SaveService.HighestUnlocked(difficulty) != levelIndex)
                return 0;

            int amount = CoinEconomy.Campaign(difficulty);
            if (amount <= 0) return 0;
            SaveService.AddCoins(amount);
            return amount;
        }

        /// <summary>
        /// Grants the daily reward once per day for a new completion.
        /// </summary>
        public static int TryGrantDailyBase(string dayId, bool wasNewCompletion)
        {
            if (!wasNewCompletion || string.IsNullOrEmpty(dayId)) return 0;
            if (SaveService.WasDailyBaseGranted(dayId)) return 0;

            SaveService.MarkDailyBaseGranted(dayId);
            int amount = CoinEconomy.DailyBase;
            SaveService.AddCoins(amount);
            return amount;
        }

        /// <summary>
        /// Claims podium coins for a settled day. Marks the day claimed even when
        /// rank is outside the top three so failed offline retries remain possible
        /// only when this method is not called.
        /// </summary>
        public static int TryClaimPodium(string dayId, int rank)
        {
            if (string.IsNullOrEmpty(dayId)) return 0;
            if (SaveService.WasDailyPodiumGranted(dayId)) return 0;

            SaveService.MarkDailyPodiumGranted(dayId);
            int amount = CoinEconomy.Podium(rank);
            if (amount > 0)
                SaveService.AddCoins(amount);
            return amount;
        }
    }
}
