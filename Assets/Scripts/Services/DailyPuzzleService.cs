using System;
using System.Globalization;
using PaintMaze.Domain;
using UnityEngine;

namespace PaintMaze.Services
{
    public interface IDailyPuzzleStorage
    {
        int GetInt(string key, int fallback);
        string GetString(string key, string fallback);
        void SetInt(string key, int value);
        void SetString(string key, string value);
        void Save();
    }

    public sealed class PlayerPrefsDailyPuzzleStorage : IDailyPuzzleStorage
    {
        public int GetInt(string key, int fallback) =>
            PlayerPrefs.GetInt(key, fallback);

        public string GetString(string key, string fallback) =>
            PlayerPrefs.GetString(key, fallback);

        public void SetInt(string key, int value) =>
            PlayerPrefs.SetInt(key, value);

        public void SetString(string key, string value) =>
            PlayerPrefs.SetString(key, value);

        public void Save() => PlayerPrefs.Save();
    }

    public sealed class DailyPuzzleStatus
    {
        public DateTime Date { get; }
        public string DayId { get; }
        public Difficulty Difficulty { get; }
        public int LevelIndex { get; }
        public bool IsCompleted { get; }
        public int CurrentStreak { get; }
        public int BestStreak { get; }
        public int AttemptsUsed { get; }
        public bool IsAttemptInProgress { get; }
        public int AttemptsRemaining =>
            Mathf.Max(0, DailyPuzzleService.MaxAttempts - AttemptsUsed);
        public bool IsLocked =>
            AttemptsUsed >= DailyPuzzleService.MaxAttempts &&
            !IsAttemptInProgress;

        public DailyPuzzleStatus(
            DateTime date,
            string dayId,
            Difficulty difficulty,
            int levelIndex,
            bool isCompleted,
            int currentStreak,
            int bestStreak,
            int attemptsUsed,
            bool isAttemptInProgress)
        {
            Date = date.Date;
            DayId = dayId;
            Difficulty = difficulty;
            LevelIndex = levelIndex;
            IsCompleted = isCompleted;
            CurrentStreak = currentStreak;
            BestStreak = bestStreak;
            AttemptsUsed = attemptsUsed;
            IsAttemptInProgress = isAttemptInProgress;
        }
    }

    public sealed class DailyPuzzleCompletion
    {
        public DailyPuzzleStatus Status { get; }
        public bool WasNewCompletion { get; }
        public float ElapsedSeconds { get; }
        public int MoveCount { get; }
        public float BestTimeSeconds { get; }
        public int BestMoveCount { get; }
        public bool IsNewBest { get; }

        public DailyPuzzleCompletion(
            DailyPuzzleStatus status,
            bool wasNewCompletion,
            float elapsedSeconds = 0f,
            int moveCount = 0,
            float bestTimeSeconds = 0f,
            int bestMoveCount = 0,
            bool isNewBest = false)
        {
            Status = status;
            WasNewCompletion = wasNewCompletion;
            ElapsedSeconds = elapsedSeconds;
            MoveCount = moveCount;
            BestTimeSeconds = bestTimeSeconds;
            BestMoveCount = bestMoveCount;
            IsNewBest = isNewBest;
        }
    }

    /// <summary>
    /// Daily challenge state for the screenshot player. UTC dates keep every
    /// player on the same puzzle and leaderboard window.
    /// </summary>
    public sealed class DailyPuzzleService
    {
        private const string KeyLastCompleted = "pm_screenshot_daily_last";
        private const string KeyCurrentStreak = "pm_screenshot_daily_current";
        private const string KeyBestStreak = "pm_screenshot_daily_best";
        private const string KeySessionOwner = "pm_screenshot_daily_session_owner";
        private const string KeyElapsedPrefix = "pm_screenshot_daily_elapsed_";
        private const string KeyBestTimePrefix = "pm_screenshot_daily_best_time_";
        private const string KeyBestMovesPrefix = "pm_screenshot_daily_best_moves_";
        private const string KeyAttemptsPrefix = "pm_screenshot_daily_attempts_";
        private const string KeyAttemptActivePrefix =
            "pm_screenshot_daily_attempt_active_";
        private const int PoolSize = 4096;
        public const int MaxAttempts = 3;

        private readonly Func<DateTime> _utcNow;
        private readonly IDailyPuzzleStorage _storage;

        public DailyPuzzleService(
            Func<DateTime> now = null,
            IDailyPuzzleStorage storage = null)
        {
            _utcNow = now ?? (() => DateTime.UtcNow);
            _storage = storage ?? new PlayerPrefsDailyPuzzleStorage();
        }

        public DailyPuzzleStatus Today => StatusFor(_utcNow().Date);

        public DailyPuzzleStatus StatusFor(DateTime date)
        {
            DateTime day = date.Date;
            string dayId = DayId(day);
            string lastCompleted =
                _storage.GetString(KeyLastCompleted, string.Empty);
            int current = Mathf.Max(
                0, _storage.GetInt(KeyCurrentStreak, 0));
            int best = Mathf.Max(
                current, _storage.GetInt(KeyBestStreak, 0));
            int attempts = Mathf.Clamp(
                _storage.GetInt(KeyAttempts(dayId), 0),
                0,
                MaxAttempts);
            bool attemptActive =
                _storage.GetInt(KeyAttemptActive(dayId), 0) != 0;
            if (TryParseDay(lastCompleted, out DateTime last) &&
                last < day.AddDays(-1))
                current = 0;
            return new DailyPuzzleStatus(
                day,
                dayId,
                Difficulty.Medium,
                PuzzleIndexFor(day),
                string.Equals(lastCompleted, dayId, StringComparison.Ordinal),
                current,
                best,
                attempts,
                attemptActive);
        }

        public bool TryBeginToday(out DailyPuzzleStatus status) =>
            TryBeginAttempt(_utcNow().Date, out status);

        public bool TryBeginAttempt(
            DateTime date,
            out DailyPuzzleStatus status)
        {
            DateTime day = date.Date;
            string dayId = DayId(day);
            status = StatusFor(day);
            if (status.IsAttemptInProgress) return true;
            if (status.IsLocked) return false;

            _storage.SetInt(
                KeyAttempts(dayId),
                Mathf.Min(MaxAttempts, status.AttemptsUsed + 1));
            _storage.SetInt(KeyAttemptActive(dayId), 1);
            _storage.SetInt(KeyElapsed(dayId), 0);
            _storage.Save();
            status = StatusFor(day);
            return true;
        }

        public void EndAttempt(DateTime date)
        {
            string dayId = DayId(date);
            _storage.SetInt(KeyAttemptActive(dayId), 0);
            _storage.Save();
        }

        public DailyPuzzleCompletion CompleteToday() =>
            Complete(_utcNow().Date);

        public DailyPuzzleCompletion Complete(DateTime date)
        {
            return Complete(date, 0f, 0);
        }

        public DailyPuzzleCompletion Complete(
            DateTime date,
            float elapsedSeconds,
            int moveCount)
        {
            DateTime day = date.Date;
            string dayId = DayId(day);
            string rawLast =
                _storage.GetString(KeyLastCompleted, string.Empty);
            if (TryParseDay(rawLast, out DateTime last) && last > day)
                return new DailyPuzzleCompletion(StatusFor(day), false);

            bool wasNewCompletion =
                !string.Equals(rawLast, dayId, StringComparison.Ordinal);
            _storage.SetInt(KeyAttemptActive(dayId), 0);
            if (wasNewCompletion)
            {
                int current = Mathf.Max(
                    0, _storage.GetInt(KeyCurrentStreak, 0));
                int next = TryParseDay(rawLast, out last) &&
                           last == day.AddDays(-1)
                    ? current + 1
                    : 1;
                int best = Mathf.Max(
                    next, _storage.GetInt(KeyBestStreak, 0));
                _storage.SetString(KeyLastCompleted, dayId);
                _storage.SetInt(KeyCurrentStreak, next);
                _storage.SetInt(KeyBestStreak, best);
            }

            int elapsedMilliseconds = SecondsToMilliseconds(elapsedSeconds);
            int bestMilliseconds =
                _storage.GetInt(KeyBestTime(dayId), 0);
            int bestMoves =
                _storage.GetInt(KeyBestMoves(dayId), 0);
            bool validResult = elapsedMilliseconds > 0 && moveCount > 0;
            bool isNewBest = validResult &&
                             IsBetterScore(
                                 elapsedMilliseconds,
                                 moveCount,
                                 bestMilliseconds,
                                 bestMoves);
            if (isNewBest)
            {
                bestMilliseconds = elapsedMilliseconds;
                bestMoves = moveCount;
                _storage.SetInt(KeyBestTime(dayId), bestMilliseconds);
                _storage.SetInt(KeyBestMoves(dayId), bestMoves);
            }
            if (elapsedMilliseconds > 0)
                _storage.SetInt(KeyElapsed(dayId), elapsedMilliseconds);
            _storage.Save();
            return new DailyPuzzleCompletion(
                StatusFor(day),
                wasNewCompletion,
                MillisecondsToSeconds(elapsedMilliseconds),
                moveCount,
                MillisecondsToSeconds(bestMilliseconds),
                bestMoves,
                isNewBest);
        }

        public float GetRunElapsedSeconds(string dayId) =>
            MillisecondsToSeconds(
                _storage.GetInt(KeyElapsed(dayId), 0));

        public void SetRunElapsedSeconds(string dayId, float elapsedSeconds)
        {
            _storage.SetInt(
                KeyElapsed(dayId),
                SecondsToMilliseconds(elapsedSeconds));
            _storage.Save();
        }

        public void ResetRunElapsed(string dayId)
        {
            _storage.SetInt(KeyElapsed(dayId), 0);
            _storage.Save();
        }

        public string PrepareSessionOwner(out string expiredOwner)
        {
            string current = SessionOwnerFor(Today.DayId);
            string previous =
                _storage.GetString(KeySessionOwner, string.Empty);
            expiredOwner = string.IsNullOrEmpty(previous) ||
                           string.Equals(previous, current, StringComparison.Ordinal)
                ? null
                : previous;
            if (!string.Equals(previous, current, StringComparison.Ordinal))
            {
                _storage.SetString(KeySessionOwner, current);
                _storage.Save();
            }
            return current;
        }

        public static int PuzzleIndexFor(DateTime date)
        {
            long dayNumber = date.Date.Ticks / TimeSpan.TicksPerDay;
            int offset = (int)(dayNumber % PoolSize);
            if (offset < 0) offset += PoolSize;
            return DifficultyCatalog.StandardBakedCount + 1 + offset;
        }

        public static string DayId(DateTime date) =>
            date.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        public static string SessionOwnerFor(string dayId) =>
            "screenshot-daily-" + (dayId ?? string.Empty);

        public static string FormatElapsed(float elapsedSeconds) =>
            Mathf.Max(0f, elapsedSeconds)
                .ToString("0.000", CultureInfo.InvariantCulture) + "s";

        public static bool IsBetterScore(
            int candidateMilliseconds,
            int candidateMoves,
            int bestMilliseconds,
            int bestMoves) =>
            candidateMilliseconds > 0 &&
            candidateMoves > 0 &&
            (bestMilliseconds <= 0 ||
             bestMoves <= 0 ||
             candidateMilliseconds < bestMilliseconds ||
             (candidateMilliseconds == bestMilliseconds &&
              candidateMoves < bestMoves));

        private static string KeyElapsed(string dayId) =>
            KeyElapsedPrefix + (dayId ?? string.Empty);

        private static string KeyBestTime(string dayId) =>
            KeyBestTimePrefix + (dayId ?? string.Empty);

        private static string KeyBestMoves(string dayId) =>
            KeyBestMovesPrefix + (dayId ?? string.Empty);

        private static string KeyAttempts(string dayId) =>
            KeyAttemptsPrefix + (dayId ?? string.Empty);

        private static string KeyAttemptActive(string dayId) =>
            KeyAttemptActivePrefix + (dayId ?? string.Empty);

        private static int SecondsToMilliseconds(float seconds)
        {
            double milliseconds = Math.Max(0d, seconds) * 1000d;
            return (int)Math.Min(int.MaxValue, Math.Round(milliseconds));
        }

        private static float MillisecondsToSeconds(int milliseconds) =>
            Mathf.Max(0, milliseconds) / 1000f;

        private static bool TryParseDay(string value, out DateTime date) =>
            DateTime.TryParseExact(
                value,
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date);
    }
}
