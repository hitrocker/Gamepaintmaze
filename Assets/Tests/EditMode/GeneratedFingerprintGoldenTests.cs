using System.Collections.Generic;
using NUnit.Framework;
using PaintMaze.Core;
using PaintMaze.Domain;

namespace PaintMaze.Tests
{
    /// <summary>
    /// Golden fingerprint snapshots for generated streams, independent of the
    /// player-facing baked catalog order. Easy, Medium, and Hard use generator v5.
    /// Extra Hard uses generator v7. The fingerprint byte format stays v3.
    /// </summary>
    public class GeneratedFingerprintGoldenTests
    {
        private static readonly int[] StandardIndices = { 501, 1000, 2000, 10000 };
        private static readonly int[] ExtraHardIndices = { 1001, 1500, 2500, 10500 };

        private string _tempRoot;
        private DeterministicBatchLevelProvider _provider;

        [SetUp]
        public void SetUp()
        {
            _tempRoot = EditModeTestSupport.CreateTempDirectory("pm-golden-");
            _provider = new DeterministicBatchLevelProvider(
                new LevelCacheStore(_tempRoot),
                allowBakedRange: true);
        }

        [TearDown]
        public void TearDown() => EditModeTestSupport.DeleteDirectoryIfExists(_tempRoot);

        [Test]
        public void RepresentativeGeneratedFingerprints_AreCanonicallyUniqueAcrossModes()
        {
            var hashes = new HashSet<ulong>();
            foreach (Difficulty difficulty in EditModeTestSupport.PlayableDifficulties)
            {
                int[] indices = difficulty == Difficulty.ExtraHard
                    ? ExtraHardIndices
                    : StandardIndices;
                foreach (int index in indices)
                {
                    Level level = _provider.GetLevel(difficulty, index);
                    Assert.IsNotNull(level, $"{difficulty} #{index}");
                    ulong hash = LevelFingerprint.From(level).Hash;
                    Assert.IsTrue(hashes.Add(hash),
                        $"{difficulty} #{index} collides on canonical fingerprint {hash:X16}");
                }
            }

            Assert.AreEqual(16, hashes.Count);
        }

        [TestCase(Difficulty.Easy, 501, "9E21C057009565BE")]
        [TestCase(Difficulty.Easy, 1000, "54F2B01E7B96D2D3")]
        [TestCase(Difficulty.Easy, 2000, "067BE30C0B0EAC38")]
        [TestCase(Difficulty.Easy, 10000, "3128DFC672730E82")]
        [TestCase(Difficulty.Medium, 501, "FCFB46BA8E21B6BD")]
        [TestCase(Difficulty.Medium, 1000, "A2EC0AF997E3D9F6")]
        [TestCase(Difficulty.Medium, 2000, "C1247C86CD37D6AC")]
        [TestCase(Difficulty.Medium, 10000, "076D0012655B9023")]
        [TestCase(Difficulty.Hard, 501, "E980E38F3A248198")]
        [TestCase(Difficulty.Hard, 1000, "62DE5DCC62BFBE67")]
        [TestCase(Difficulty.Hard, 2000, "029E833CE71388C0")]
        [TestCase(Difficulty.Hard, 10000, "BA40ACC3A425D4F5")]
        [TestCase(Difficulty.ExtraHard, 1001, "5F0E0E98634580B5")]
        [TestCase(Difficulty.ExtraHard, 1500, "5EE6C899F76C47BF")]
        [TestCase(Difficulty.ExtraHard, 2500, "17E3F4E3EFC0535A")]
        [TestCase(Difficulty.ExtraHard, 10500, "954C5916F131F818")]
        public void GeneratedFingerprint_MatchesGoldenSnapshot(Difficulty difficulty, int index, string expectedHex)
        {
            Level level = _provider.GetLevel(difficulty, index);
            Assert.IsNotNull(level);
            string actual = LevelFingerprint.From(level).Hash.ToString("X16");
            Assert.AreEqual(expectedHex, actual, $"{difficulty} #{index}");
        }
    }
}
