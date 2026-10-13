using System;
using System.Collections.Generic;
using NUnit.Framework;
using PaintMaze.Domain;
using PaintMaze.Services;
using UnityEngine;

namespace PaintMaze.Tests
{
    public class DailyPuzzleServiceTests
    {
        private DateTime _now;
        private MemoryStorage _storage;
        private DailyPuzzleService _service;

        [SetUp]
        public void SetUp()
        {
            _now = new DateTime(2026, 7, 24, 12, 0, 0);
            _storage = new MemoryStorage();
            _service = new DailyPuzzleService(() => _now, _storage);
        }

        [Test]
        public void Today_IsDeterministicAndUsesBoundedMediumLevel()
        {
            DailyPuzzleStatus first = _service.Today;
            DailyPuzzleStatus second = _service.Today;

            Assert.AreEqual(first.DayId, second.DayId);
            Assert.AreEqual(first.LevelIndex, second.LevelIndex);
            Assert.AreEqual(Difficulty.Medium, first.Difficulty);
            Assert.Greater(
                first.LevelIndex, DifficultyCatalog.StandardBakedCount);
            Assert.LessOrEqual(
                first.LevelIndex,
                DifficultyCatalog.StandardBakedCount + 4096);

            _now = _now.AddDays(1);

            Assert.AreNotEqual(first.LevelIndex, _service.Today.LevelIndex);
        }

        [Test]
        public void CompleteToday_IsIdempotent()
        {
            DailyPuzzleCompletion first = _service.CompleteToday();
            DailyPuzzleCompletion duplicate = _service.CompleteToday();

            Assert.IsTrue(first.WasNewCompletion);
            Assert.IsFalse(duplicate.WasNewCompletion);
            Assert.IsTrue(duplicate.Status.IsCompleted);
            Assert.AreEqual(1, duplicate.Status.CurrentStreak);
            Assert.AreEqual(1, duplicate.Status.BestStreak);
        }

        [Test]
        public void ConsecutiveCompletion_IncrementsStreak()
        {
            _service.CompleteToday();
            _now = _now.AddDays(1);

            DailyPuzzleCompletion result = _service.CompleteToday();

            Assert.AreEqual(2, result.Status.CurrentStreak);
            Assert.AreEqual(2, result.Status.BestStreak);
        }

        [Test]
        public void MissedDay_ResetsCurrentAndPreservesBest()
        {
            _service.CompleteToday();
            _now = _now.AddDays(1);
            _service.CompleteToday();
            _now = _now.AddDays(2);

            Assert.AreEqual(0, _service.Today.CurrentStreak);
            DailyPuzzleCompletion result = _service.CompleteToday();

            Assert.AreEqual(1, result.Status.CurrentStreak);
            Assert.AreEqual(2, result.Status.BestStreak);
        }

        [Test]
        public void DateRollover_ExposesNewIncompletePuzzle()
        {
            DailyPuzzleStatus completed = _service.CompleteToday().Status;
            _now = _now.AddDays(1);

            DailyPuzzleStatus next = _service.Today;

            Assert.AreNotEqual(completed.DayId, next.DayId);
            Assert.AreNotEqual(completed.LevelIndex, next.LevelIndex);
            Assert.IsFalse(next.IsCompleted);
            Assert.AreEqual(1, next.CurrentStreak);
        }

        [Test]
        public void UtcMidnight_RotatesPuzzleAndAttemptBudget()
        {
            _now = new DateTime(
                2026,
                7,
                24,
                23,
                59,
                59,
                DateTimeKind.Utc);
            Assert.IsTrue(_service.TryBeginToday(out DailyPuzzleStatus before));

            _now = _now.AddSeconds(2);
            DailyPuzzleStatus after = _service.Today;

            Assert.AreNotEqual(before.DayId, after.DayId);
            Assert.AreNotEqual(before.LevelIndex, after.LevelIndex);
            Assert.AreEqual(0, after.AttemptsUsed);
            Assert.IsFalse(after.IsLocked);
        }

        [Test]
        public void DailyAttempt_ResumeDoesNotConsumeAnotherTry()
        {
            Assert.IsTrue(_service.TryBeginToday(out DailyPuzzleStatus first));
            Assert.IsTrue(_service.TryBeginToday(out DailyPuzzleStatus resumed));

            Assert.AreEqual(1, first.AttemptsUsed);
            Assert.AreEqual(1, resumed.AttemptsUsed);
            Assert.AreEqual(
                Theme.PaintIndexForAttempt(
                    first.LevelIndex,
                    first.AttemptsUsed),
                Theme.PaintIndexForAttempt(
                    resumed.LevelIndex,
                    resumed.AttemptsUsed));
            Assert.IsTrue(resumed.IsAttemptInProgress);
            Assert.AreEqual(2, resumed.AttemptsRemaining);
        }

        [Test]
        public void DailyAttempts_UseThreeDistinctThemeColors()
        {
            ThemeMode original = Theme.Mode;
            try
            {
                foreach (ThemeMode mode in new[]
                         {
                             ThemeMode.Dark,
                             ThemeMode.Light
                         })
                {
                    Theme.Mode = mode;
                    int levelIndex = _service.Today.LevelIndex;
                    Color first = Theme.PaintForAttempt(levelIndex, 1);
                    Color second = Theme.PaintForAttempt(levelIndex, 2);
                    Color third = Theme.PaintForAttempt(levelIndex, 3);

                    Assert.AreEqual(Theme.Paint(levelIndex), first);
                    Assert.AreNotEqual(first, second);
                    Assert.AreNotEqual(first, third);
                    Assert.AreNotEqual(second, third);
                }
            }
            finally
            {
                Theme.Mode = original;
            }
        }

        [Test]
        public void DailyAttemptPaint_ClampsToThreeColorBudget()
        {
            int levelIndex = _service.Today.LevelIndex;

            Assert.AreEqual(
                Theme.PaintIndexForAttempt(levelIndex, 1),
                Theme.PaintIndexForAttempt(levelIndex, 0));
            Assert.AreEqual(
                Theme.PaintIndexForAttempt(levelIndex, 3),
                Theme.PaintIndexForAttempt(levelIndex, 4));
        }

        [Test]
        public void ThirdFinishedAttempt_LocksPuzzleUntilTomorrow()
        {
            for (int attempt = 1; attempt <= DailyPuzzleService.MaxAttempts; attempt++)
            {
                Assert.IsTrue(
                    _service.TryBeginToday(out DailyPuzzleStatus active));
                Assert.AreEqual(attempt, active.AttemptsUsed);
                _service.CompleteToday();
            }

            DailyPuzzleStatus locked = _service.Today;
            Assert.IsTrue(locked.IsLocked);
            Assert.AreEqual(0, locked.AttemptsRemaining);
            Assert.IsFalse(
                _service.TryBeginToday(out DailyPuzzleStatus rejected));
            Assert.IsTrue(rejected.IsLocked);

            _now = _now.AddDays(1);
            Assert.IsTrue(
                _service.TryBeginToday(out DailyPuzzleStatus tomorrow));
            Assert.AreEqual(1, tomorrow.AttemptsUsed);
            Assert.IsFalse(tomorrow.IsLocked);
        }

        [Test]
        public void EndingThirdAttemptWithoutCompletion_LocksPuzzle()
        {
            for (int attempt = 1; attempt <= DailyPuzzleService.MaxAttempts; attempt++)
            {
                Assert.IsTrue(
                    _service.TryBeginToday(out DailyPuzzleStatus active));
                _service.EndAttempt(active.Date);
            }

            Assert.IsTrue(_service.Today.IsLocked);
            Assert.IsFalse(_service.Today.IsCompleted);
        }

        [Test]
        public void SessionOwner_RotatesWithDate()
        {
            string first = _service.PrepareSessionOwner(out string initialExpired);
            _now = _now.AddDays(1);
            string second = _service.PrepareSessionOwner(out string expired);

            Assert.IsNull(initialExpired);
            Assert.AreNotEqual(first, second);
            Assert.AreEqual(first, expired);
        }

        [Test]
        public void RunElapsed_RoundTripsWithMillisecondPrecision()
        {
            string dayId = _service.Today.DayId;

            _service.SetRunElapsedSeconds(dayId, 65.432f);

            Assert.AreEqual(
                65.432f,
                _service.GetRunElapsedSeconds(dayId),
                0.001f);
            _service.ResetRunElapsed(dayId);
            Assert.AreEqual(0f, _service.GetRunElapsedSeconds(dayId));
        }

        [Test]
        public void FormatElapsed_ShowsTotalSecondsAndMilliseconds()
        {
            Assert.AreEqual(
                "0.000s",
                DailyPuzzleService.FormatElapsed(0f));
            Assert.AreEqual(
                "75.432s",
                DailyPuzzleService.FormatElapsed(75.432f));
        }

        [Test]
        public void DailyBest_PrioritizesTimeThenMoves()
        {
            DailyPuzzleCompletion first =
                _service.Complete(_now, 75.4f, 20);
            DailyPuzzleCompletion fasterWithMoreMoves =
                _service.Complete(_now, 60f, 21);
            DailyPuzzleCompletion fewerMoves =
                _service.Complete(_now, 90f, 19);
            DailyPuzzleCompletion fewerMovesTie =
                _service.Complete(_now, 60f, 19);

            Assert.IsTrue(first.IsNewBest);
            Assert.IsTrue(fasterWithMoreMoves.IsNewBest);
            Assert.IsFalse(fewerMoves.IsNewBest);
            Assert.IsTrue(fewerMovesTie.IsNewBest);
            Assert.AreEqual(19, fewerMovesTie.BestMoveCount);
            Assert.AreEqual(60f, fewerMovesTie.BestTimeSeconds, 0.001f);
            Assert.IsFalse(fewerMovesTie.WasNewCompletion);
        }

        [Test]
        public void BetterScore_UsesMillisecondsBeforeMoves()
        {
            Assert.IsTrue(DailyPuzzleService.IsBetterScore(
                5000, 30, 6000, 10));
            Assert.IsFalse(DailyPuzzleService.IsBetterScore(
                6000, 10, 5000, 30));
            Assert.IsTrue(DailyPuzzleService.IsBetterScore(
                5000, 20, 5000, 21));
        }

        [Test]
        public void ScreenshotMode_RequiresMatchingProductAndCaptureArgument()
        {
            Assert.IsTrue(ScreenshotPlayerMode.IsScreenshotProduct(
                "Paint Maze Screenshot"));
            Assert.IsFalse(ScreenshotPlayerMode.IsScreenshotProduct(
                "Paint Maze"));
            Assert.IsTrue(ScreenshotPlayerMode.HasCommandLineArgument(
                new[] { "Paint Maze Screenshot", "-captureOutput", "/tmp/out" },
                "-captureOutput"));
            Assert.IsFalse(ScreenshotPlayerMode.HasCommandLineArgument(
                new[] { "Paint Maze Screenshot" },
                "-captureOutput"));
            Assert.IsTrue(ScreenshotPlayerMode.HasCommandLineArgument(
                new[] { "Paint Maze Screenshot", "-demoVideoOutput", "/tmp/demo" },
                "-demoVideoOutput"));
        }

        private sealed class MemoryStorage : IDailyPuzzleStorage
        {
            private readonly Dictionary<string, int> _ints = new();
            private readonly Dictionary<string, string> _strings = new();

            public int GetInt(string key, int fallback) =>
                _ints.TryGetValue(key, out int value) ? value : fallback;

            public string GetString(string key, string fallback) =>
                _strings.TryGetValue(key, out string value) ? value : fallback;

            public void SetInt(string key, int value) => _ints[key] = value;

            public void SetString(string key, string value) =>
                _strings[key] = value;

            public void Save()
            {
            }
        }
    }
}
