using NUnit.Framework;
using PaintMaze.Domain;

namespace PaintMaze.Tests
{
    public class DifficultyCatalogTests
    {
        [Test]
        public void Playable_ContainsExactlyFourVisibleModes()
        {
            CollectionAssert.AreEqual(EditModeTestSupport.PlayableDifficulties, DifficultyCatalog.Playable);
            Assert.IsFalse(DifficultyCatalog.IsPlayable(Difficulty.UltraHard));
            Assert.AreEqual(Difficulty.ExtraHard,
                DifficultyCatalog.NormalizePlayable(Difficulty.UltraHard));
        }

        [TestCase(500, Difficulty.ExtraHard, 500)]
        [TestCase(501, Difficulty.UltraHard, 1)]
        [TestCase(1000, Difficulty.UltraHard, 500)]
        [TestCase(5000, Difficulty.UltraHard, 4500)]
        public void ExtraHardBakedIndex_MapsToExpectedSource(
            int visibleIndex, Difficulty expectedSource, int expectedIndex)
        {
            DifficultyCatalog.ResolveBakedSource(
                Difficulty.ExtraHard, visibleIndex, out Difficulty source, out int sourceIndex);

            Assert.AreEqual(expectedSource, source);
            Assert.AreEqual(expectedIndex, sourceIndex);
        }

        [TestCase(501, 1)]
        [TestCase(751, 251)]
        [TestCase(1001, 501)]
        [TestCase(2000, 1500)]
        [TestCase(5001, 4501)]
        public void ExtraHardGeneration_UsesMatchingFormerUltraHardStream(
            int visibleIndex, int expectedSourceIndex)
        {
            DifficultyCatalog.ResolveGenerationSource(
                Difficulty.ExtraHard, visibleIndex,
                out Difficulty source, out int sourceIndex);

            Assert.AreEqual(Difficulty.UltraHard, source);
            Assert.AreEqual(expectedSourceIndex, sourceIndex);
        }
    }
}
