using NUnit.Framework;
using PaintMaze.Core;
using PaintMaze.Domain;

namespace PaintMaze.Tests
{
    public class DeterministicRngTests
    {
        private const double DoubleTolerance = 1e-15;

        [Test]
        public void Next_ProducesGoldenSequence_ForSeed42()
        {
            var rng = new DeterministicRng(42);
            int[] expected = { 291, 858, 764, 250, 62, 925, 908, 5 };
            for (int i = 0; i < expected.Length; i++)
                Assert.AreEqual(expected[i], rng.Next(1000), $"Next(1000) call {i}");
        }

        [Test]
        public void NextDouble_ProducesGoldenSequence_ForSeed42()
        {
            var rng = new DeterministicRng(42);
            // Advance state to match post-Next sequence in golden vector test.
            for (int i = 0; i < 8; i++) rng.Next(1000);

            double[] expected =
            {
                0.61848206635613479,
                0.20490183179877552,
                0.49298918579469242,
                0.51339611632214943
            };
            for (int i = 0; i < expected.Length; i++)
                Assert.AreEqual(expected[i], rng.NextDouble(), DoubleTolerance, $"NextDouble call {i}");
        }

        [Test]
        public void SeedZero_UsesFallbackState()
        {
            var rng = new DeterministicRng(0);
            Assert.AreEqual(2, rng.Next(7));
        }

        [Test]
        public void Next_RejectsNonPositiveBound()
        {
            var rng = new DeterministicRng(1);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => rng.Next(0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => rng.Next(-3));
        }

        [Test]
        public void Shuffle_IsDeterministic()
        {
            var rngA = new DeterministicRng(99);
            var rngB = new DeterministicRng(99);
            var listA = new System.Collections.Generic.List<int> { 0, 1, 2, 3, 4, 5, 6, 7 };
            var listB = new System.Collections.Generic.List<int> { 0, 1, 2, 3, 4, 5, 6, 7 };
            rngA.Shuffle(listA);
            rngB.Shuffle(listB);
            CollectionAssert.AreEqual(listA, listB);
        }
    }

    public class LevelSeedTests
    {
        [TestCase(Difficulty.Easy, 1, 2071832339)]
        [TestCase(Difficulty.Medium, 1, 522342467)]
        [TestCase(Difficulty.Hard, 1, 1054087656)]
        [TestCase(Difficulty.ExtraHard, 1, 748520598)]
        [TestCase(Difficulty.UltraHard, 1, 333137565)]
        [TestCase(Difficulty.Easy, 501, 1097264538)]
        [TestCase(Difficulty.UltraHard, 10000, 1870023370)]
        public void For_ProducesStableSeeds(Difficulty difficulty, int levelNumber, int expected)
        {
            Assert.AreEqual(expected, LevelSeed.For(difficulty, levelNumber));
        }

        [Test]
        public void For_UsesGeneratorVersionAndAttempt()
        {
            int baseline = LevelSeed.For(Difficulty.Hard, 42);
            Assert.AreNotEqual(baseline, LevelSeed.For(Difficulty.Hard, 42, generatorVersion: 2));
            Assert.AreNotEqual(baseline, LevelSeed.For(Difficulty.Hard, 42, attempt: 1));
        }
    }
}
