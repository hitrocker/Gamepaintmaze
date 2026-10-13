using System;

namespace PaintMaze.Services
{
    /// <summary>
    /// Owns the daily free-hint quota and immediate paid-hint transaction.
    /// Gameplay validates and displays a route before committing either use.
    /// </summary>
    public sealed class HintService
    {
        public const int FreeHintsPerDay = 3;

        private readonly EconomySyncService _economySync;
        private readonly Func<DateTime> _utcNow;

        public HintService(
            EconomySyncService economySync = null,
            Func<DateTime> utcNow = null)
        {
            _economySync = economySync;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public string CurrentDayId =>
            DailyPuzzleService.DayId(_utcNow().Date);

        public int FreeHintsUsed =>
            SaveService.GetDailyHintsUsed(CurrentDayId);

        public int FreeHintsRemaining =>
            Math.Max(0, FreeHintsPerDay - FreeHintsUsed);

        public bool TryConsumeFreeHint()
        {
            string dayId = CurrentDayId;
            int used = SaveService.GetDailyHintsUsed(dayId);
            if (used >= FreeHintsPerDay) return false;

            SaveService.SetDailyHintUsage(dayId, used + 1);
            _economySync?.PushLocal();
            return true;
        }

        public bool TryPurchaseHint()
        {
            if (!SaveService.TrySpendCoins(CoinEconomy.HintCost))
                return false;
            _economySync?.PushLocal();
            return true;
        }
    }
}
