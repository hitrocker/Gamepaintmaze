using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using PaintMaze.Core;
using PaintMaze.Domain;

namespace PaintMaze.Tests
{
    public class DeterministicBatchLevelProviderTests
    {
        private string _tempRoot;

        [SetUp]
        public void SetUp() => _tempRoot = EditModeTestSupport.CreateTempDirectory("pm-batch-");

        [TearDown]
        public void TearDown() => EditModeTestSupport.DeleteDirectoryIfExists(_tempRoot);

        [Test]
        public void GetLevel_PersistsOnlyRequestedIndex()
        {
            var cache = new LevelCacheStore(_tempRoot);
            var provider = new DeterministicBatchLevelProvider(cache);

            Assert.IsNotNull(provider.GetLevel(Difficulty.Hard, 501));

            Assert.IsTrue(cache.TryLoadLevel(Difficulty.Hard, 501, out _));
            Assert.IsFalse(cache.TryLoadLevel(Difficulty.Hard, 502, out _));
            Assert.AreEqual(1, Directory.GetFiles(Path.Combine(cache.Root, "hard"), "*.txt").Length);
        }

        [Test]
        public void GeneratedRange_IsDeterministicAndSolvable()
        {
            var cache = new LevelCacheStore(_tempRoot);
            var provider = new DeterministicBatchLevelProvider(cache);
            const int batchStart = 501;

            Level[] first = FetchRange(provider, Difficulty.Hard, batchStart);
            EditModeTestSupport.DeleteDirectoryIfExists(_tempRoot);
            _tempRoot = EditModeTestSupport.CreateTempDirectory("pm-batch-");
            cache = new LevelCacheStore(_tempRoot);
            provider = new DeterministicBatchLevelProvider(cache);
            Level[] second = FetchRange(provider, Difficulty.Hard, batchStart);

            var solver = new Solver();
            for (int i = 0; i < first.Length; i++)
            {
                Assert.IsTrue(EditModeTestSupport.GridsEqual(first[i].Grid, second[i].Grid), $"offset {i}");
                Assert.AreEqual(batchStart + i, first[i].Index);
                Assert.IsTrue(solver.IsAlwaysSolvable(first[i]), $"index {batchStart + i}");
            }
        }

        [Test]
        public void PersistedSingleSlot_MatchesColdReload()
        {
            var firstProvider = new DeterministicBatchLevelProvider(
                new LevelCacheStore(_tempRoot), allowBakedRange: true);
            Level generated = firstProvider.GetLevel(Difficulty.ExtraHard, 2000);

            var reloadedProvider = new DeterministicBatchLevelProvider(
                new LevelCacheStore(_tempRoot), allowBakedRange: true);
            Level reloaded = reloadedProvider.GetLevel(Difficulty.ExtraHard, 2000);

            Assert.IsNotNull(generated);
            Assert.IsNotNull(reloaded);
            Assert.IsTrue(EditModeTestSupport.GridsEqual(generated.Grid, reloaded.Grid));
            Assert.AreEqual(generated.Spawn, reloaded.Spawn);
        }

        [Test]
        public void GeneratedLevels_AreIndependentOfRequestOrder()
        {
            const int batchStart = 1001;
            var forward = new DeterministicBatchLevelProvider(
                new LevelCacheStore(_tempRoot), allowBakedRange: true);
            var forwardLevels = new Level[10];
            for (int i = 0; i < forwardLevels.Length; i++)
                forwardLevels[i] = forward.GetLevel(Difficulty.ExtraHard, batchStart + i);

            string reverseRoot = EditModeTestSupport.CreateTempDirectory("pm-batch-rev-");
            try
            {
                var reverse = new DeterministicBatchLevelProvider(
                    new LevelCacheStore(reverseRoot), allowBakedRange: true);
                var reverseLevels = new Level[10];
                for (int i = reverseLevels.Length - 1; i >= 0; i--)
                    reverseLevels[i] = reverse.GetLevel(Difficulty.ExtraHard, batchStart + i);

                for (int i = 0; i < forwardLevels.Length; i++)
                {
                    Assert.IsNotNull(forwardLevels[i]);
                    Assert.IsNotNull(reverseLevels[i]);
                    Assert.IsTrue(EditModeTestSupport.GridsEqual(forwardLevels[i].Grid, reverseLevels[i].Grid),
                        $"offset {i} differs by request order");
                    Assert.AreEqual(forwardLevels[i].Spawn, reverseLevels[i].Spawn, $"spawn offset {i}");
                }
            }
            finally
            {
                EditModeTestSupport.DeleteDirectoryIfExists(reverseRoot);
            }
        }

        [Test]
        public void ConcurrentGetLevelAndPrefetch_AreSafeAndDeterministic()
        {
            const int batchStart = 501;
            const int count = 10;

            var reference = new DeterministicBatchLevelProvider(new LevelCacheStore(
                EditModeTestSupport.CreateTempDirectory("pm-batch-ref-")));
            var expected = new Level[count];
            for (int i = 0; i < count; i++)
                expected[i] = reference.GetLevel(Difficulty.Hard, batchStart + i);

            var provider = new DeterministicBatchLevelProvider(new LevelCacheStore(_tempRoot));
            var results = new Level[count];
            Parallel.For(0, count * 4, k =>
            {
                int offset = k % count;
                int index = batchStart + offset;
                if (k % 2 == 0)
                {
                    Level level = provider.GetLevel(Difficulty.Hard, index);
                    Assert.IsNotNull(level);
                    results[offset] = level;
                }
                else
                {
                    provider.Prefetch(Difficulty.Hard, index);
                }
            });

            for (int i = 0; i < count; i++)
            {
                Level level = results[i] ?? provider.GetLevel(Difficulty.Hard, batchStart + i);
                Assert.IsNotNull(level);
                Assert.IsTrue(EditModeTestSupport.GridsEqual(expected[i].Grid, level.Grid), $"offset {i}");
                Assert.AreEqual(expected[i].Spawn, level.Spawn, $"spawn offset {i}");
            }
        }

        [Test]
        public void CorruptCache_IsRegeneratedAndNeverNull()
        {
            var reference = new DeterministicBatchLevelProvider(new LevelCacheStore(
                EditModeTestSupport.CreateTempDirectory("pm-batch-ref2-")));
            Level expected = reference.GetLevel(Difficulty.Medium, 505);

            var cache = new LevelCacheStore(_tempRoot);
            string path = Path.Combine(cache.Root, "medium", "medium_0000000505.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "not a valid pack {{{");

            var provider = new DeterministicBatchLevelProvider(cache);
            Level actual = provider.GetLevel(Difficulty.Medium, 505);

            Assert.IsNotNull(actual);
            Assert.IsTrue(EditModeTestSupport.GridsEqual(expected.Grid, actual.Grid));
            Assert.AreEqual(expected.Spawn, actual.Spawn);
        }

        [Test]
        public void BakedCountFor_ReturnsZero()
        {
            var provider = new DeterministicBatchLevelProvider(new LevelCacheStore(_tempRoot));
            foreach (Difficulty d in EditModeTestSupport.AllDifficulties)
                Assert.AreEqual(0, provider.BakedCountFor(d));
        }

        [Test]
        public void RetainMemoryWindow_EvictsStaleSlotsButKeepsDiskCache()
        {
            var cache = new LevelCacheStore(_tempRoot);
            var provider = new DeterministicBatchLevelProvider(cache);
            Assert.IsNotNull(provider.GetLevel(Difficulty.Hard, 501));
            Assert.IsNotNull(provider.GetLevel(Difficulty.Hard, 502));
            Assert.IsTrue(provider.IsReady(Difficulty.Hard, 501));
            Assert.IsTrue(provider.IsReady(Difficulty.Hard, 502));

            provider.RetainMemoryWindow(
                Difficulty.Hard, currentIndex: 502, levelsBehind: 0, levelsAhead: 0);

            Assert.IsFalse(provider.IsReady(Difficulty.Hard, 501));
            Assert.IsTrue(provider.IsReady(Difficulty.Hard, 502));
            Assert.IsTrue(cache.TryLoadLevel(Difficulty.Hard, 501, out _));
        }

        [Test]
        public void IndicesAtOrBelowBakedCount_ReturnNull()
        {
            var provider = new DeterministicBatchLevelProvider(new LevelCacheStore(_tempRoot));
            Assert.IsNull(provider.GetLevel(Difficulty.Easy, 500));
            Assert.IsNull(provider.GetLevel(Difficulty.Easy, 1));
            Assert.IsNull(provider.GetLevel(Difficulty.ExtraHard, 5000));
            Assert.IsNull(provider.GetLevel(Difficulty.UltraHard, 1001));
        }

        private static Level[] FetchRange(DeterministicBatchLevelProvider provider, Difficulty difficulty,
            int rangeStart)
        {
            var levels = new Level[10];
            for (int i = 0; i < levels.Length; i++)
            {
                levels[i] = provider.GetLevel(difficulty, rangeStart + i);
                Assert.IsNotNull(levels[i]);
            }
            return levels;
        }
    }

    public class ScalableLevelProviderTests
    {
        private string _tempRoot;
        private ScalableLevelProvider _provider;

        [SetUp]
        public void SetUp()
        {
            _tempRoot = EditModeTestSupport.CreateTempDirectory("pm-scalable-");
            var baked = new SectionedLevelProvider();
            var generated = new DeterministicBatchLevelProvider(new LevelCacheStore(_tempRoot));
            _provider = new ScalableLevelProvider(baked, generated);
        }

        [TearDown]
        public void TearDown() => EditModeTestSupport.DeleteDirectoryIfExists(_tempRoot);

        [Test]
        public void BakedCountFor_DelegatesToSectionedProvider()
        {
            foreach (Difficulty d in new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard })
                Assert.AreEqual(500, _provider.BakedCountFor(d));
            Assert.AreEqual(5000, _provider.BakedCountFor(Difficulty.ExtraHard));
            Assert.AreEqual(0, _provider.BakedCountFor(Difficulty.UltraHard));
        }

        [TestCase(Difficulty.Easy, 1)]
        [TestCase(Difficulty.ExtraHard, 2)]
        public void BakedVisibleSlot_LoadsMappedCatalogBoardAndKeepsVisibleIndex(
            Difficulty difficulty, int visibleIndex)
        {
            int catalogIndex =
                LevelCatalogOrder.Map(difficulty, visibleIndex);
            Level expected = new SectionedLevelProvider()
                .GetLevel(difficulty, catalogIndex);
            Level actual = _provider.GetLevel(difficulty, visibleIndex);

            Assert.IsNotNull(expected);
            Assert.IsNotNull(actual);
            Assert.IsTrue(
                EditModeTestSupport.GridsEqual(expected.Grid, actual.Grid));
            Assert.AreEqual(expected.Spawn, actual.Spawn);
            Assert.AreEqual(difficulty, actual.Difficulty);
            Assert.AreEqual(visibleIndex, actual.Index);
        }

        [Test]
        public void BakedPrefetch_UsesSameMappedCatalogSlotAsGetLevel()
        {
            const int visibleIndex = 2;
            _provider.Prefetch(Difficulty.Medium, visibleIndex);

            Assert.IsTrue(SpinWait.SpinUntil(
                () => _provider.IsReady(Difficulty.Medium, visibleIndex), 5000));
            Level actual =
                _provider.GetLevel(Difficulty.Medium, visibleIndex);
            Assert.IsNotNull(actual);
            Assert.AreEqual(visibleIndex, actual.Index);
        }

        [Test]
        public void Handoff_From500To501_UsesBakedThenGenerated()
        {
            Level baked = _provider.GetLevel(Difficulty.Medium, 500);
            Level generated = _provider.GetLevel(Difficulty.Medium, 501);
            Assert.IsNotNull(baked);
            Assert.IsNotNull(generated);
            Assert.AreEqual(500, baked.Index);
            Assert.AreEqual(501, generated.Index);
            Assert.AreEqual(Difficulty.Medium, baked.Difficulty);
            Assert.AreEqual(Difficulty.Medium, generated.Difficulty);
        }

        [Test]
        public void Level501_MatchesDeterministicBatchProvider()
        {
            var generated = new DeterministicBatchLevelProvider(new LevelCacheStore(_tempRoot));
            Level expected = generated.GetLevel(Difficulty.Easy, 501);
            Level actual = _provider.GetLevel(Difficulty.Easy, 501);
            Assert.IsTrue(EditModeTestSupport.GridsEqual(expected.Grid, actual.Grid));
            Assert.AreEqual(expected.Spawn, actual.Spawn);
        }

        [Test]
        public void ExtraHard_HandoffFrom5000To5001_UsesBakedThenUltraTunedGeneration()
        {
            Level baked = _provider.GetLevel(Difficulty.ExtraHard, 5000);
            Level generated = _provider.GetLevel(Difficulty.ExtraHard, 5001);

            Assert.IsNotNull(baked);
            Assert.IsNotNull(generated);
            Assert.AreEqual(Difficulty.ExtraHard, baked.Difficulty);
            Assert.AreEqual(Difficulty.ExtraHard, generated.Difficulty);
            Assert.AreEqual(5000, baked.Index);
            Assert.AreEqual(5001, generated.Index);
        }

        [Test]
        public void OutOfRange_ReturnsNull()
        {
            Assert.IsNull(_provider.GetLevel(Difficulty.Easy, 0));
            Assert.IsNull(_provider.GetLevel(Difficulty.Easy, DifficultyConfig.PracticalMaxLevel + 1));
            Assert.IsNull(_provider.GetLevel(Difficulty.UltraHard, 1));
        }
    }
}
