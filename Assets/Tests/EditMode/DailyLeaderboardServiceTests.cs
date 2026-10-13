using System;
using NUnit.Framework;
using PaintMaze.Domain;
using PaintMaze.Menu;
using PaintMaze.Services;

namespace PaintMaze.Tests
{
    public sealed class DailyLeaderboardServiceTests
    {
        [Test]
        public void ShouldReplace_PrioritizesTimeThenMoves()
        {
            Assert.IsTrue(DailyLeaderboardService.ShouldReplace(
                6000, 10, 5000, 30));
            Assert.IsFalse(DailyLeaderboardService.ShouldReplace(
                5000, 30, 6000, 10));
            Assert.IsTrue(DailyLeaderboardService.ShouldReplace(
                5000, 30, 5000, 29));
            Assert.IsFalse(DailyLeaderboardService.ShouldReplace(
                5000, 29, 5000, 30));
        }

        [Test]
        public void ComputeRank_AddsFasterAndTieBreakerCounts()
        {
            Assert.AreEqual(1, DailyLeaderboardService.ComputeRank(0, 0));
            Assert.AreEqual(8, DailyLeaderboardService.ComputeRank(5, 2));
        }

        [Test]
        public void MergeTopTen_DeduplicatesAndSortsOwnScore()
        {
            DailyLeaderboardEntry[] entries = BuildEntries(10);
            var own = new DailyLeaderboardEntry
            {
                UserId = "player-5",
                DayId = "20260724",
                DisplayName = "You",
                ElapsedMilliseconds = 500,
                MoveCount = 50,
                IsCurrentUser = true
            };

            var merged = DailyLeaderboardService.MergeTopTen(entries, own);

            Assert.AreEqual(10, merged.Count);
            Assert.AreEqual("player-5", merged[0].UserId);
            Assert.AreEqual(1, CountUser(merged, "player-5"));
        }

        [Test]
        public void MergeTopTen_DoesNotForceSlowOwnScoreIntoTopTen()
        {
            DailyLeaderboardEntry[] entries = BuildEntries(10);
            var own = new DailyLeaderboardEntry
            {
                UserId = "slow-player",
                DayId = "20260724",
                DisplayName = "You",
                ElapsedMilliseconds = 999999,
                MoveCount = 50,
                IsCurrentUser = true
            };

            var merged = DailyLeaderboardService.MergeTopTen(entries, own);

            Assert.AreEqual(10, merged.Count);
            Assert.AreEqual(0, CountUser(merged, "slow-player"));
        }

        [Test]
        public void ServerFetch_IsLimitedToOncePerDayUnlessForced()
        {
            Assert.IsTrue(DailyLeaderboardService.ShouldFetchServer(
                "20260724", string.Empty));
            Assert.IsFalse(DailyLeaderboardService.ShouldFetchServer(
                "20260724", "20260724"));
            Assert.IsTrue(DailyLeaderboardService.ShouldFetchServer(
                "20260725", "20260724"));
            Assert.IsTrue(DailyLeaderboardService.ShouldFetchServer(
                "20260724", "20260724", true));
        }

        [Test]
        public void Submission_IsGatedToValidNewPersonalBest()
        {
            var status = new DailyPuzzleStatus(
                new DateTime(2026, 7, 24),
                "20260724",
                Difficulty.Medium,
                100,
                true,
                1,
                1,
                1,
                false);
            var best = new DailyPuzzleCompletion(
                status,
                true,
                5f,
                20,
                5f,
                20,
                true);
            var unchanged = new DailyPuzzleCompletion(
                status,
                false,
                6f,
                18,
                5f,
                20,
                false);

            Assert.IsTrue(DailyLeaderboardService.ShouldSubmit(best));
            Assert.IsFalse(DailyLeaderboardService.ShouldSubmit(unchanged));
        }

        [Test]
        public void DocumentId_IsStablePerUtcDayAndUser()
        {
            Assert.AreEqual(
                "20260724_user-123",
                DailyLeaderboardService.DocumentId(
                    "20260724",
                    "user-123"));
        }

        [Test]
        public void OwnRow_IsHighlightedAndNotPinnedWhenVisible()
        {
            DailyLeaderboardEntry[] entries = BuildEntries(10);
            DailyLeaderboardEntry own = entries[4];
            own.IsCurrentUser = true;

            Assert.IsTrue(DailyLeaderboardPanel.IsOwnRow(own, own));
            Assert.IsTrue(DailyLeaderboardPanel.ShouldShowPinnedOwn(
                entries,
                own,
                3));
            Assert.IsFalse(DailyLeaderboardPanel.ShouldShowPinnedOwn(
                entries,
                own,
                10));
        }

        [Test]
        public void OwnRow_MatchesStableUserIdWithoutFlag()
        {
            DailyLeaderboardEntry[] entries = BuildEntries(3);
            var own = new DailyLeaderboardEntry
            {
                UserId = entries[1].UserId
            };

            Assert.IsTrue(DailyLeaderboardPanel.IsOwnRow(entries[1], own));
            Assert.IsFalse(DailyLeaderboardPanel.ShouldShowPinnedOwn(
                entries,
                own,
                3));
        }

        private static DailyLeaderboardEntry[] BuildEntries(int count)
        {
            var entries = new DailyLeaderboardEntry[count];
            for (int i = 0; i < count; i++)
            {
                entries[i] = new DailyLeaderboardEntry
                {
                    UserId = "player-" + i,
                    DayId = "20260724",
                    DisplayName = "Player " + i,
                    ElapsedMilliseconds = 1000 + i * 100,
                    MoveCount = 20 + i,
                    SubmittedAtUtc = new DateTime(
                        2026,
                        7,
                        24,
                        12,
                        0,
                        i,
                        DateTimeKind.Utc)
                };
            }
            return entries;
        }

        private static int CountUser(
            System.Collections.Generic.IReadOnlyList<DailyLeaderboardEntry> entries,
            string userId)
        {
            int count = 0;
            foreach (DailyLeaderboardEntry entry in entries)
                if (entry.UserId == userId) count++;
            return count;
        }
    }
}
