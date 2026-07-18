using System;
using System.Collections.Generic;
using NUnit.Framework;
using PaintMaze.Core;
using PaintMaze.Domain;

namespace PaintMaze.Tests
{
    public class LevelCatalogOrderTests
    {
        [Test]
        public void OrderVersion_IsFrozen()
        {
            Assert.AreEqual(1, LevelCatalogOrder.OrderVersion);
        }

        [TestCase(Difficulty.Easy, 206, 112)]
        [TestCase(Difficulty.Medium, 474, 375)]
        [TestCase(Difficulty.Hard, 473, 24)]
        [TestCase(Difficulty.ExtraHard, 369, 1289)]
        public void FirstSlots_MatchVersionOneGoldenOrder(
            Difficulty difficulty, int first, int second)
        {
            Assert.AreEqual(first, LevelCatalogOrder.Map(difficulty, 1));
            Assert.AreEqual(second, LevelCatalogOrder.Map(difficulty, 2));
        }

        [TestCase(Difficulty.Easy)]
        [TestCase(Difficulty.Medium)]
        [TestCase(Difficulty.Hard)]
        [TestCase(Difficulty.ExtraHard)]
        public void Map_IsABijectionOverBakedCatalog(Difficulty difficulty)
        {
            int count = DifficultyCatalog.BakedCountFor(difficulty);
            var seen = new HashSet<int>();
            for (int visibleIndex = 1; visibleIndex <= count; visibleIndex++)
            {
                int catalogIndex = LevelCatalogOrder.Map(difficulty, visibleIndex);
                Assert.That(catalogIndex, Is.InRange(1, count));
                Assert.IsTrue(
                    seen.Add(catalogIndex),
                    $"{difficulty} catalog slot {catalogIndex} repeated");
            }
            Assert.AreEqual(count, seen.Count);
        }

        [Test]
        public void StandardDifficulties_HaveIndependentOrders()
        {
            const int sampleSize = 32;
            bool easyDiffersFromMedium = false;
            bool mediumDiffersFromHard = false;
            for (int visibleIndex = 1; visibleIndex <= sampleSize; visibleIndex++)
            {
                easyDiffersFromMedium |=
                    LevelCatalogOrder.Map(Difficulty.Easy, visibleIndex) !=
                    LevelCatalogOrder.Map(Difficulty.Medium, visibleIndex);
                mediumDiffersFromHard |=
                    LevelCatalogOrder.Map(Difficulty.Medium, visibleIndex) !=
                    LevelCatalogOrder.Map(Difficulty.Hard, visibleIndex);
            }
            Assert.IsTrue(easyDiffersFromMedium);
            Assert.IsTrue(mediumDiffersFromHard);
        }

        [Test]
        public void Map_RejectsNonBakedSlots()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => LevelCatalogOrder.Map(Difficulty.Easy, 0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => LevelCatalogOrder.Map(Difficulty.Easy, 501));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => LevelCatalogOrder.Map(Difficulty.UltraHard, 1));
        }
    }
}
