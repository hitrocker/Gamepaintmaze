using System;
using NUnit.Framework;
using PaintMaze.Core;
using PaintMaze.Domain;

namespace PaintMaze.Tests
{
    public class LevelSafetyValidatorTests
    {
        private readonly Solver _solver = new Solver();

        [Test]
        public void Level_RejectsNullOrEmptyGrid()
        {
            Assert.Throws<ArgumentNullException>(() => new Level(null, new Position(0, 0)));
            Assert.Throws<ArgumentException>(() =>
                new Level(new Tile[0, 1], new Position(0, 0)));
        }

        [Test]
        public void Level_RejectsOutOfBoundsOrNonFloorSpawn()
        {
            var grid = new[,]
            {
                { Tile.Floor, Tile.Wall }
            };

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new Level(grid, new Position(1, 0)));
            Assert.Throws<ArgumentException>(() =>
                new Level(grid, new Position(0, 1)));
        }

        [Test]
        public void SafetyValidator_RejectsValidStructureThatIsNotAlwaysSolvable()
        {
            Level level = TestHelpers.Make(
                "S#.",
                "###");

            Assert.IsFalse(LevelSafetyValidator.IsSafe(level, _solver, out string reason));
            Assert.That(reason, Does.StartWith("not always solvable: uncovered floor"));
        }

        [Test]
        public void GuaranteedFallbacks_AreSafeAcrossConfiguredSizesAndModes()
        {
            var generator = new LevelGenerator(_solver);
            int[] indices = { 1, 126, 501, 2000 };

            foreach (Difficulty difficulty in EditModeTestSupport.AllDifficulties)
            {
                DifficultyConfig config = DifficultyConfig.For(difficulty);
                foreach (int index in indices)
                {
                    int rows = config.BoardRowsFor(index);
                    int cols = config.BoardColsFor(index);
                    Level fallback = generator.GenerateGuaranteedFallback(
                        rows, cols, difficulty, index);

                    Assert.IsTrue(
                        LevelSafetyValidator.IsSafe(fallback, _solver, out string reason),
                        $"{difficulty} #{index} ({rows}x{cols}): {reason}");
                    Assert.IsTrue(fallback.IsFloor(fallback.Spawn));
                    Assert.Greater(fallback.TotalPaintable, 0);
                }
            }
        }
    }
}
