using NUnit.Framework;
using PaintMaze.Core;
using PaintMaze.Domain;
using System.Threading;
using UnityEngine;

namespace PaintMaze.Tests
{
    public class SectionedLevelProviderTests
    {
        private SectionedLevelProvider _provider;

        [SetUp]
        public void SetUp() => _provider = new SectionedLevelProvider();

        [Test]
        public void BakedCountFor_UsesFourModeBoundaries()
        {
            foreach (Difficulty d in new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard })
                Assert.AreEqual(500, _provider.BakedCountFor(d), d.ToString());
            Assert.AreEqual(5000, _provider.BakedCountFor(Difficulty.ExtraHard));
            Assert.AreEqual(0, _provider.BakedCountFor(Difficulty.UltraHard));
        }

        [Test]
        public void SectionStartFor_MapsBoundaryIndices()
        {
            Assert.AreEqual(1, SectionedLevelProvider.SectionStartFor(1));
            Assert.AreEqual(1, SectionedLevelProvider.SectionStartFor(50));
            Assert.AreEqual(51, SectionedLevelProvider.SectionStartFor(51));
            Assert.AreEqual(451, SectionedLevelProvider.SectionStartFor(500));
            Assert.AreEqual(4951, SectionedLevelProvider.SectionStartFor(5000));
        }

        [TestCase(Difficulty.Easy)]
        [TestCase(Difficulty.Medium)]
        [TestCase(Difficulty.Hard)]
        public void BoundaryLevels_LoadWithCorrectDifficultyAndIndex(Difficulty difficulty)
        {
            foreach (int index in new[] { 1, 50, 51, 500 })
            {
                Level level = _provider.GetLevel(difficulty, index);
                Assert.IsNotNull(level, $"{difficulty} #{index}");
                Assert.AreEqual(difficulty, level.Difficulty);
                Assert.AreEqual(index, level.Index);
            }

            Assert.IsNull(_provider.GetLevel(difficulty, 501), $"{difficulty} #501");
        }

        [Test]
        public void ExtraHard_CombinesExistingAndExtendedUltraCatalogs()
        {
            AssertExtraHardMatchesSource(500, Difficulty.ExtraHard, 500);
            AssertExtraHardMatchesSource(501, Difficulty.UltraHard, 1);
            AssertExtraHardMatchesSource(1000, Difficulty.UltraHard, 500);
            AssertExtraHardMatchesSource(1001, Difficulty.UltraHard, 501);
            AssertExtraHardMatchesSource(2500, Difficulty.UltraHard, 2000);
            AssertExtraHardMatchesSource(5000, Difficulty.UltraHard, 4500);
            Assert.IsNull(_provider.GetLevel(Difficulty.ExtraHard, 5001));
        }

        private void AssertExtraHardMatchesSource(
            int visibleIndex, Difficulty sourceDifficulty, int sourceIndex)
        {
            Level actual = _provider.GetLevel(Difficulty.ExtraHard, visibleIndex);
            int sectionStart = SectionedLevelProvider.SectionStartFor(sourceIndex);
            int sectionEnd = SectionedLevelProvider.SectionEndFor(sectionStart);
            string slug = sourceDifficulty == Difficulty.ExtraHard ? "extrahard" : "ultrahard";
            var asset = Resources.Load<TextAsset>(
                $"Levels/Baked/{slug}_{sectionStart:D4}_{sectionEnd:D4}");
            Level expected = LevelParser.Parse(asset.text, sourceDifficulty)[sourceIndex - sectionStart];

            Assert.IsNotNull(actual);
            Assert.IsTrue(EditModeTestSupport.GridsEqual(expected.Grid, actual.Grid));
            Assert.AreEqual(expected.Spawn, actual.Spawn);
            Assert.AreEqual(Difficulty.ExtraHard, actual.Difficulty);
            Assert.AreEqual(visibleIndex, actual.Index);
        }

        [Test]
        public void OutOfRangeIndices_ReturnNull()
        {
            Assert.IsNull(_provider.GetLevel(Difficulty.Easy, 0));
            Assert.IsNull(_provider.GetLevel(Difficulty.Easy, 501));
            Assert.IsNull(_provider.GetLevel(Difficulty.Easy, -1));
        }

        [Test]
        public void Prefetch_ParsesSectionInBackgroundAndPublishesReadyLevel()
        {
            _provider.Prefetch(Difficulty.Easy, 1);

            Assert.IsTrue(SpinWait.SpinUntil(
                () => _provider.IsReady(Difficulty.Easy, 1), 5000));
            Assert.IsNotNull(_provider.GetLevel(Difficulty.Easy, 1));
        }
    }
}
