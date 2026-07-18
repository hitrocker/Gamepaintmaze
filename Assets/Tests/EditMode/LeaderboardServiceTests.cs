using NUnit.Framework;
using PaintMaze.Domain;
using PaintMaze.Menu;
using PaintMaze.Services;

namespace PaintMaze.Tests
{
    public class LeaderboardServiceTests
    {
        [TestCase(Difficulty.Easy, "easy")]
        [TestCase(Difficulty.Medium, "medium")]
        [TestCase(Difficulty.Hard, "hard")]
        [TestCase(Difficulty.ExtraHard, "extraHard")]
        public void BoardIdFor_MapsPlayableDifficulty(Difficulty difficulty, string expected)
        {
            Assert.AreEqual(expected, LeaderboardService.BoardIdFor(difficulty));
        }

        [Test]
        public void ShouldRaise_OnlyAcceptsHigherCompletedLevel()
        {
            Assert.IsTrue(LeaderboardService.ShouldRaise(12, 13));
            Assert.IsFalse(LeaderboardService.ShouldRaise(12, 12));
            Assert.IsFalse(LeaderboardService.ShouldRaise(12, 4));
        }

        [Test]
        public void DisplayNameFor_GuestIsDeterministic()
        {
            Assert.AreEqual("Guest CDEF",
                LeaderboardService.DisplayNameFor("uid-abcdef", string.Empty));
            Assert.AreEqual("Maze Pro",
                LeaderboardService.DisplayNameFor("uid-abcdef", "  Maze Pro  "));
            Assert.AreEqual("Guest CDEF",
                LeaderboardService.DisplayNameFor("uid-abcdef", "Name That Is Far Too Long"));
        }

        [TestCase(1, 0)]
        [TestCase(2, 1)]
        [TestCase(38, 37)]
        public void CompletedLevelForHighestUnlocked_BackfillsPreviousLevel(
            int highestUnlocked, int expected)
        {
            Assert.AreEqual(expected,
                LeaderboardService.CompletedLevelForHighestUnlocked(highestUnlocked));
        }

        [TestCase(0, 1)]
        [TestCase(1, 2)]
        [TestCase(37, 38)]
        public void HighestUnlockedForCompletedLevel_RestoresNextLevel(
            int completedLevel, int expected)
        {
            Assert.AreEqual(expected,
                LeaderboardService.HighestUnlockedForCompletedLevel(completedLevel));
        }

        [TestCase(0L, 0L, 1)]
        [TestCase(25L, 0L, 26)]
        [TestCase(25L, 3L, 29)]
        public void ComputeGlobalRank_AddsHigherLevelsAndEarlierTies(
            long higherLevels, long earlierTies, int expected)
        {
            Assert.AreEqual(expected,
                LeaderboardService.ComputeGlobalRank(higherLevels, earlierTies));
        }

        [Test]
        public void ComputeGlobalRank_ClampsInvalidAndOversizedCounts()
        {
            Assert.AreEqual(1,
                LeaderboardService.ComputeGlobalRank(-10, -20));
            Assert.AreEqual(int.MaxValue,
                LeaderboardService.ComputeGlobalRank(long.MaxValue, long.MaxValue));
        }

        [TestCase(null, "?")]
        [TestCase(0, "?")]
        [TestCase(446, "446")]
        public void FormatPinnedRank_UsesPlaceholderUntilExactRankIsKnown(
            int? rank, string expected)
        {
            Assert.AreEqual(expected, LeaderboardController.FormatPinnedRank(rank));
        }
    }
}
