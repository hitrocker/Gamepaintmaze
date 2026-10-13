using System.Collections.Generic;
using NUnit.Framework;
using PaintMaze.Domain;

namespace PaintMaze.Tests
{
    public class DifficultyConfigTests
    {
        [Test]
        public void VisibleModes_HaveExpectedBakedCounts()
        {
            foreach (Difficulty d in new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard })
                Assert.AreEqual(500, DifficultyConfig.For(d).BakedLevelCount, d.ToString());
            Assert.AreEqual(5000, DifficultyConfig.For(Difficulty.ExtraHard).BakedLevelCount);
            Assert.AreEqual(500, DifficultyConfig.For(Difficulty.UltraHard).BakedLevelCount,
                "Legacy Ultra tuning/source metadata remains intact");
        }

        [Test]
        public void UltraHard_HasHighestObstacleFraction()
        {
            double ultra = DifficultyConfig.For(Difficulty.UltraHard).ObstacleFraction;
            foreach (Difficulty d in EditModeTestSupport.AllDifficulties)
            {
                if (d == Difficulty.UltraHard) continue;
                Assert.Less(DifficultyConfig.For(d).ObstacleFraction, ultra, d.ToString());
            }
        }

        [Test]
        public void ExtraHardAndUltraHard_UseReferenceStyleProgressionBands()
        {
            var extra = DifficultyConfig.For(Difficulty.ExtraHard);
            Assert.AreEqual((9, 9), (extra.BoardRowsFor(1), extra.BoardColsFor(1)));
            Assert.AreEqual((10, 10), (extra.BoardRowsFor(126), extra.BoardColsFor(126)));
            Assert.AreEqual((12, 14), (extra.BoardRowsFor(251), extra.BoardColsFor(251)));
            Assert.AreEqual((14, 16), (extra.BoardRowsFor(376), extra.BoardColsFor(376)));

            var ultra = DifficultyConfig.For(Difficulty.UltraHard);
            Assert.AreEqual(16, ultra.MaxRows);
            Assert.AreEqual(22, ultra.MaxCols);
            Assert.AreEqual((14, 16), (ultra.BoardRowsFor(1), ultra.BoardColsFor(1)));
            Assert.AreEqual((14, 18), (ultra.BoardRowsFor(126), ultra.BoardColsFor(126)));
            Assert.AreEqual((16, 18), (ultra.BoardRowsFor(251), ultra.BoardColsFor(251)));
            Assert.AreEqual((16, 20), (ultra.BoardRowsFor(376), ultra.BoardColsFor(376)));
            Assert.AreEqual((16, 22), (ultra.BoardRowsFor(501), ultra.BoardColsFor(501)));
            Assert.AreEqual((16, 22),
                (ultra.BoardRowsFor(DifficultyConfig.PracticalMaxLevel),
                    ultra.BoardColsFor(DifficultyConfig.PracticalMaxLevel)));
        }

        [Test]
        public void PracticalMaxLevel_IsTwoBillion()
        {
            Assert.AreEqual(2_000_000_000, DifficultyConfig.PracticalMaxLevel);
        }

        [Test]
        public void ArenaModes_GrowMonotonicallyInsideTheirBands()
        {
            foreach (Difficulty d in new[] { Difficulty.ExtraHard, Difficulty.UltraHard })
            {
                var cfg = DifficultyConfig.For(d);
                int prevRows = cfg.BoardRowsFor(1);
                int prevCols = cfg.BoardColsFor(1);
                for (int level = 2; level <= 600; level++)
                {
                    int rows = cfg.BoardRowsFor(level);
                    int cols = cfg.BoardColsFor(level);
                    Assert.GreaterOrEqual(rows, prevRows, $"{d} rows at {level}");
                    Assert.GreaterOrEqual(cols, prevCols, $"{d} cols at {level}");
                    prevRows = rows;
                    prevCols = cols;
                }
            }
        }

        [Test]
        public void StandardModes_VarySizeAndObstacleFractionInsideTheBand()
        {
            double ultra = DifficultyConfig.For(Difficulty.UltraHard).ObstacleFraction;
            foreach (Difficulty d in new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard })
            {
                DifficultyConfig cfg = DifficultyConfig.For(d);
                var sizes = new HashSet<(int, int)>();
                var fractions = new HashSet<string>();
                for (int level = 1; level <= 40; level++)
                {
                    int rows = cfg.BoardRowsFor(level);
                    int cols = cfg.BoardColsFor(level);
                    Assert.That(rows, Is.InRange(cfg.BaseRows, cfg.MaxRows), $"{d} rows {level}");
                    Assert.That(cols, Is.InRange(cfg.BaseCols, cfg.MaxCols), $"{d} cols {level}");
                    sizes.Add((rows, cols));
                    double fraction = cfg.ObstacleFractionFor(level);
                    Assert.Less(fraction, ultra, $"{d} fraction {level}");
                    fractions.Add(fraction.ToString("0.00"));
                }

                Assert.GreaterOrEqual(sizes.Count, 2, $"{d} sizes");
                Assert.GreaterOrEqual(fractions.Count, 2, $"{d} obstacle fractions");
            }
        }
    }
}
