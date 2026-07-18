using NUnit.Framework;
using PaintMaze.Domain;
using PaintMaze.Services;

namespace PaintMaze.Tests
{
    /// <summary>
    /// UI-independent proof that the four endless tracks never surface finite or
    /// wrapping copy and that "next" always advances exactly one level.
    /// </summary>
    public class LevelLabelTests
    {
        [Test]
        public void DockPosition_UsesUpperDisplayName_AndLevelNumber()
        {
            Assert.AreEqual("EASY  ·  LEVEL 1", LevelLabel.DockPosition(Difficulty.Easy, 1));
            Assert.AreEqual("EXTRA HARD  ·  LEVEL 7", LevelLabel.DockPosition(Difficulty.ExtraHard, 7));
        }

        [Test]
        public void DockPosition_NeverShowsFiniteTotalOrWrap()
        {
            foreach (Difficulty d in EditModeTestSupport.PlayableDifficulties)
            {
                foreach (int level in new[] { 1, 60, 500, 501, 12_345, DifficultyConfig.PracticalMaxLevel })
                {
                    string text = LevelLabel.DockPosition(d, level);
                    StringAssert.DoesNotContain("/", text);
                    StringAssert.DoesNotContain(" OF ", text);
                    StringAssert.DoesNotContain("\n", text);
                    StringAssert.Contains("LEVEL " + level, text);
                }
            }
        }

        [Test]
        public void HudTitle_IsUnboundedAndHasNoTotal()
        {
            Assert.AreEqual("Level 1", LevelLabel.HudTitle(1));
            Assert.AreEqual("Level 501", LevelLabel.HudTitle(501));
            Assert.AreEqual("Level 1000000", LevelLabel.HudTitle(1_000_000));
            StringAssert.DoesNotContain("/", LevelLabel.HudTitle(742));
            StringAssert.DoesNotContain("of", LevelLabel.HudTitle(742));
        }

        [Test]
        public void NextLevel_AdvancesExactlyOne()
        {
            Assert.AreEqual(2, LevelLabel.NextLevel(1));
            Assert.AreEqual(501, LevelLabel.NextLevel(500));   // crosses baked -> generated, no wrap
            Assert.AreEqual(502, LevelLabel.NextLevel(501));
            Assert.AreEqual(1_000_001, LevelLabel.NextLevel(1_000_000));
        }

        [Test]
        public void NextLevel_NeverExceedsPracticalMax()
        {
            Assert.AreEqual(DifficultyConfig.PracticalMaxLevel,
                LevelLabel.NextLevel(DifficultyConfig.PracticalMaxLevel));
        }

        [Test]
        public void Clamp_KeepsLevelWithinPlayableRange()
        {
            Assert.AreEqual(1, LevelLabel.Clamp(0));
            Assert.AreEqual(1, LevelLabel.Clamp(-42));
            Assert.AreEqual(500, LevelLabel.Clamp(500));
            Assert.AreEqual(DifficultyConfig.PracticalMaxLevel,
                LevelLabel.Clamp(DifficultyConfig.PracticalMaxLevel + 10));
        }
    }

    /// <summary>
    /// The four tracks are independent: completing one mode never changes another,
    /// and completion unlocks exactly the next level.
    /// </summary>
    public class EndlessTrackProgressionTests
    {
        [SetUp]
        public void SetUp() => EditModeTestSupport.ClearPlayerPrefs();

        [TearDown]
        public void TearDown() => EditModeTestSupport.ClearPlayerPrefs();

        [Test]
        public void EachTrack_DefaultsToLevelOne()
        {
            foreach (Difficulty d in EditModeTestSupport.PlayableDifficulties)
                Assert.AreEqual(1, SaveService.HighestUnlocked(d), d.ToString());
        }

        [Test]
        public void MarkCompleted_UnlocksExactlyNextLevel()
        {
            SaveService.MarkCompleted(Difficulty.Hard, 5);
            Assert.AreEqual(6, SaveService.HighestUnlocked(Difficulty.Hard));
        }

        [Test]
        public void Progress_IsIndependentPerDifficulty()
        {
            SaveService.MarkCompleted(Difficulty.Easy, 40);
            SaveService.MarkCompleted(Difficulty.ExtraHard, 3);

            Assert.AreEqual(41, SaveService.HighestUnlocked(Difficulty.Easy));
            Assert.AreEqual(4, SaveService.HighestUnlocked(Difficulty.ExtraHard));
            // Untouched tracks stay at the start.
            Assert.AreEqual(1, SaveService.HighestUnlocked(Difficulty.Medium));
            Assert.AreEqual(1, SaveService.HighestUnlocked(Difficulty.Hard));
        }

        [Test]
        public void Progress_CrossesBakedBoundaryWithoutCapping()
        {
            SaveService.MarkCompleted(Difficulty.Medium, 500);
            Assert.AreEqual(501, SaveService.HighestUnlocked(Difficulty.Medium));
        }
    }
}
