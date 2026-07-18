using System.Collections.Generic;
using NUnit.Framework;
using PaintMaze.Core;
using PaintMaze.Domain;

namespace PaintMaze.Tests
{
    /// <summary>
    /// Golden fingerprint snapshots for generated streams, independent of the
    /// player-facing baked catalog order. Lower modes retain generator v3 while
    /// Extra Hard intentionally uses its v6 stream.
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

        [TestCase(Difficulty.Easy, 501, "E40F22C0561EBFBE")]
        [TestCase(Difficulty.Easy, 1000, "B3FDD099BA59F58D")]
        [TestCase(Difficulty.Easy, 2000, "65D326FFCA21D7F2")]
        [TestCase(Difficulty.Easy, 10000, "9F6017C0438D4D49")]
        [TestCase(Difficulty.Medium, 501, "D24CEFB133113635")]
        [TestCase(Difficulty.Medium, 1000, "D61760CA0A726345")]
        [TestCase(Difficulty.Medium, 2000, "15107517EEE625DB")]
        [TestCase(Difficulty.Medium, 10000, "6DEF48B38EF325B2")]
        [TestCase(Difficulty.Hard, 501, "FD4DAF036264BC71")]
        [TestCase(Difficulty.Hard, 1000, "B3D7452CDB76D165")]
        [TestCase(Difficulty.Hard, 2000, "13905217D8DDA2C6")]
        [TestCase(Difficulty.Hard, 10000, "06517AE1495CBB5F")]
        [TestCase(Difficulty.ExtraHard, 1001, "61E79981B462DBB2")]
        [TestCase(Difficulty.ExtraHard, 1500, "1B498615D80F559A")]
        [TestCase(Difficulty.ExtraHard, 2500, "26FC206324B8DB48")]
        [TestCase(Difficulty.ExtraHard, 10500, "A901794B325B5C10")]
        public void GeneratedFingerprint_MatchesGoldenSnapshot(Difficulty difficulty, int index, string expectedHex)
        {
            Level level = _provider.GetLevel(difficulty, index);
            Assert.IsNotNull(level);
            string actual = LevelFingerprint.From(level).Hash.ToString("X16");
            Assert.AreEqual(expectedHex, actual, $"{difficulty} #{index}");
        }
    }
}
