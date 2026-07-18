using NUnit.Framework;
using PaintMaze.Core;
using PaintMaze.Domain;

namespace PaintMaze.Tests
{
    public class BakedRuntimeParityTests
    {
        private string _tempRoot;

        [SetUp]
        public void SetUp() =>
            _tempRoot = EditModeTestSupport.CreateTempDirectory("pm-baked-parity-");

        [TearDown]
        public void TearDown() =>
            EditModeTestSupport.DeleteDirectoryIfExists(_tempRoot);

        [TestCase(1001)]
        [TestCase(2000)]
        [TestCase(3767)]
        [TestCase(4000)]
        [TestCase(5000)]
        public void ExtendedExtraHardSource_MatchesRuntimePublication(int catalogIndex)
        {
            var baked = new SectionedLevelProvider();
            var runtime = new DeterministicBatchLevelProvider(
                new LevelCacheStore(_tempRoot),
                allowBakedRange: true);

            Level packaged = baked.GetLevel(Difficulty.ExtraHard, catalogIndex);
            Level generated = runtime.GetLevel(Difficulty.ExtraHard, catalogIndex);

            Assert.IsNotNull(packaged);
            Assert.IsNotNull(generated);
            Assert.IsTrue(
                EditModeTestSupport.GridsEqual(packaged.Grid, generated.Grid),
                $"Extra Hard source #{catalogIndex} grid");
            Assert.AreEqual(packaged.Spawn, generated.Spawn);
            Assert.AreEqual(
                LevelFingerprint.From(generated).Hash,
                LevelFingerprint.From(packaged).Hash);
        }
    }
}
