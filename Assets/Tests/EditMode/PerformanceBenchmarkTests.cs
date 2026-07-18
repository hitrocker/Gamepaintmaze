using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using PaintMaze.Core;
using PaintMaze.Domain;

namespace PaintMaze.Tests
{
    public class PerformanceBenchmarkTests
    {
        private string _tempRoot;

        [SetUp]
        public void SetUp() => _tempRoot = EditModeTestSupport.CreateTempDirectory("pm-bench-");

        [TearDown]
        public void TearDown() => EditModeTestSupport.DeleteDirectoryIfExists(_tempRoot);

        [Test]
        public void RepresentativeGenerationAndCache_StaysWithinCeiling()
        {
            var cache = new LevelCacheStore(_tempRoot);
            var generated = new DeterministicBatchLevelProvider(cache);
            var sw = Stopwatch.StartNew();
            Level level = generated.GetLevel(Difficulty.Hard, 501);
            sw.Stop();
            double generateMs = sw.Elapsed.TotalMilliseconds;
            Assert.IsNotNull(level);
            UnityEngine.Debug.Log($"[Benchmark] Hard #501 (single, cold): {generateMs:F1} ms");
            Assert.Less(generateMs, 60_000, "single level generation regression");

            sw.Restart();
            Level ultra = generated.GetLevel(Difficulty.ExtraHard, 5001);
            sw.Stop();
            double ultraMs = sw.Elapsed.TotalMilliseconds;
            Assert.IsNotNull(ultra);
            UnityEngine.Debug.Log($"[Benchmark] ExtraHard #5001 (Ultra-tuned, cold): {ultraMs:F1} ms");
            Assert.Less(ultraMs, 90_000, "ultra-hard single level regression");

            sw.Restart();
            for (int i = 501; i <= 510; i++)
                Assert.IsNotNull(generated.GetLevel(Difficulty.Hard, i));
            sw.Stop();
            double batchMs = sw.Elapsed.TotalMilliseconds;
            UnityEngine.Debug.Log($"[Benchmark] Hard 501-510 (per-slot): {batchMs:F1} ms");
            Assert.Less(batchMs, 120_000, "batch generation regression");

            sw.Restart();
            Assert.IsTrue(cache.TryLoadLevel(Difficulty.Hard, 501, out _));
            sw.Stop();
            double cacheMs = sw.Elapsed.TotalMilliseconds;
            UnityEngine.Debug.Log($"[Benchmark] cache load level: {cacheMs:F1} ms");
            Assert.Less(cacheMs, 5_000, "single-level cache load regression");
        }

        /// <summary>
        /// Regression guard for the reported hardest-mode cold-load stall: a direct cold
        /// request above the 5,000-level baked runway must build and cache only that level.
        /// </summary>
        [Test]
        public void DirectColdExtraHard6000_ReturnsWithoutBuildingWholeBatch()
        {
            var generated = new DeterministicBatchLevelProvider(new LevelCacheStore(_tempRoot));

            var sw = Stopwatch.StartNew();
            Level level = generated.GetLevel(Difficulty.ExtraHard, 6000);
            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds;

            Assert.IsNotNull(level);
            Assert.AreEqual(6000, level.Index);
            Assert.AreEqual(1,
                Directory.GetFiles(Path.Combine(
                    new LevelCacheStore(_tempRoot).Root, "extrahard"), "*.txt").Length);
            UnityEngine.Debug.Log($"[Benchmark] ExtraHard #6000 (direct single, cold): {ms:F1} ms");

            // A single Ultra-tuned Extra Hard level costs a small fraction of a full batch. This
            // desktop ceiling is deliberately generous versus the measured ~0.25s so it
            // is not flaky, while still catching a regression back to full-batch builds.
            Assert.Less(ms, 5_000, "direct single ultra-hard cold request regressed toward full-batch cost");
        }
    }
}
