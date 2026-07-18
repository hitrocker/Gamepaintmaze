using System.Collections.Generic;
using NUnit.Framework;
using PaintMaze.Core;
using PaintMaze.Domain;

namespace PaintMaze.Tests
{
    public class LevelFingerprintTests
    {
        [Test]
        public void Hash_IsInvariantUnderRotationsAndMirrors()
        {
            Level original = TestHelpers.Make(
                "S.#",
                ".#.",
                "...");
            ulong baseline = LevelFingerprint.From(original).Hash;

            for (int rotation = 0; rotation < 4; rotation++)
            {
                for (int mirror = 0; mirror < 2; mirror++)
                {
                    Level transformed = EditModeTestSupport.TransformLevel(original, rotation, mirror == 1);
                    Assert.AreEqual(baseline, LevelFingerprint.From(transformed).Hash,
                        $"rotation={rotation} mirror={mirror}");
                }
            }
        }

        [Test]
        public void Hash_IsSensitiveToSpawn()
        {
            Level a = TestHelpers.Make("S..", "...");
            Level b = TestHelpers.Make(".S.", "...");
            Assert.AreNotEqual(LevelFingerprint.From(a).Hash, LevelFingerprint.From(b).Hash);
        }

        [Test]
        public void SerializerAndParser_RoundTrip_PreservesFingerprint()
        {
            Level original = TestHelpers.Make(
                "_S._",
                "_.#_",
                "_.._");
            string pack = LevelPackSerializer.ToPack(new[] { original });
            List<Level> parsed = LevelParser.Parse(pack, original.Difficulty);
            Assert.AreEqual(1, parsed.Count);

            Assert.IsTrue(EditModeTestSupport.GridsEqual(original.Grid, parsed[0].Grid));
            Assert.AreEqual(original.Spawn, parsed[0].Spawn);
            Assert.AreEqual(LevelFingerprint.From(original).Hash, LevelFingerprint.From(parsed[0]).Hash);
        }

        [Test]
        public void CanonicalBytesEqual_DetectsDifferences()
        {
            Level a = TestHelpers.Make("S..", "...");
            Level b = TestHelpers.Make("S#.", "...");
            var fpA = LevelFingerprint.From(a);
            var fpB = LevelFingerprint.From(b);
            Assert.IsFalse(LevelFingerprint.CanonicalBytesEqual(fpA.CanonicalBytes, fpB.CanonicalBytes));
            Assert.IsTrue(LevelFingerprint.CanonicalBytesEqual(fpA.CanonicalBytes, fpA.CanonicalBytes));
        }

        [Test]
        public void GeneratorVersions_AreScopedWithoutChangingFingerprintFormat()
        {
            Assert.AreEqual(3, LevelFingerprint.GeneratorVersion);
            Assert.AreEqual(3, LevelGenerator.VersionFor(Difficulty.Easy));
            Assert.AreEqual(6, LevelGenerator.VersionFor(Difficulty.ExtraHard));
            Assert.AreEqual(6, LevelGenerator.VersionFor(Difficulty.UltraHard));
        }
    }
}
