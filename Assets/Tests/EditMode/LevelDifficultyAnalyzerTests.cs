using NUnit.Framework;
using PaintMaze.Core;
using PaintMaze.Domain;

namespace PaintMaze.Tests
{
    public class LevelDifficultyAnalyzerTests
    {
        [Test]
        public void Analyze_IsDeterministicForSeedAndBoard()
        {
            Level level = TestHelpers.Make(
                "S...",
                ".##.",
                "....",
                "_...");
            var analyzer = new LevelDifficultyAnalyzer();
            var options = new LevelDifficultyAnalysisOptions
            {
                MaxExactStates = 10000,
                ExactFloorLimit = 100,
                RandomRolloutCount = 12,
                RolloutMaxMoves = 80
            };

            LevelDifficultyMetrics a = analyzer.Analyze(level, 123456, options);
            LevelDifficultyMetrics b = analyzer.Analyze(level, 123456, options);

            Assert.AreEqual(a.MinMoves, b.MinMoves);
            Assert.AreEqual(a.RandomSolutionBest, b.RandomSolutionBest);
            Assert.AreEqual(a.RandomSolutionAverage, b.RandomSolutionAverage);
            Assert.AreEqual(a.RandomSolutionFailed, b.RandomSolutionFailed);
            Assert.AreEqual(a.Score, b.Score);
        }

        [Test]
        public void LargeBoardAnalysis_UsesBoundedRolloutsWithoutExactSearch()
        {
            var grid = new Tile[16, 22];
            for (int r = 0; r < 16; r++)
                for (int c = 0; c < 22; c++)
                    grid[r, c] = Tile.Floor;
            var level = new Level(grid, new Position(0, 0), Difficulty.UltraHard, 501);
            var analyzer = new LevelDifficultyAnalyzer();

            LevelDifficultyMetrics metrics = analyzer.Analyze(
                level,
                42,
                new LevelDifficultyAnalysisOptions
                {
                    MaxExactStates = 0,
                    ExactFloorLimit = 0,
                    RandomRolloutCount = 4,
                    RolloutMaxMoves = 32
                });

            Assert.IsFalse(metrics.MinMoves.HasValue);
            Assert.AreEqual(4, metrics.RandomRolloutCount);
            Assert.GreaterOrEqual(metrics.Score, 0);
        }

        [Test]
        public void ForcedForwardCorridor_HasLessStrategicChoiceThanOpenLayout()
        {
            var generator = new LevelGenerator();
            Level corridor = generator.GenerateGuaranteedFallback(
                14, 16, Difficulty.ExtraHard, 251);
            Level open = generator.Generate(
                Difficulty.ExtraHard,
                251,
                new LevelGenerationOptions { AttemptCount = 1 });
            var analyzer = new LevelDifficultyAnalyzer();
            var options = new LevelDifficultyAnalysisOptions
            {
                MaxExactStates = 0,
                ExactFloorLimit = 0,
                RandomRolloutCount = 8,
                RolloutMaxMoves = 240
            };

            LevelDifficultyMetrics corridorMetrics = analyzer.Analyze(
                corridor, 99, options);
            LevelDifficultyMetrics openMetrics = analyzer.Analyze(
                open, 99, options);

            Assert.Greater(openMetrics.FloorGraphCycleRank, 0);
            Assert.Greater(openMetrics.MeaningfulChoiceRatio, 0);
            Assert.Greater(
                openMetrics.FloorGraphCycleRank,
                corridorMetrics.FloorGraphCycleRank);
            Assert.Greater(
                openMetrics.MeaningfulChoiceRatio,
                corridorMetrics.MeaningfulChoiceRatio);
        }
    }
}
