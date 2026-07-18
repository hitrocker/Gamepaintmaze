using System.Collections.Generic;
using NUnit.Framework;
using PaintMaze.Core;
using PaintMaze.Domain;

namespace PaintMaze.Tests
{
    public class DifficultyQualityTrendTests
    {
        private readonly SectionedLevelProvider _provider = new SectionedLevelProvider();
        private readonly LevelQualityScorer _scorer = new LevelQualityScorer();

        private sealed class ModeAverages
        {
            public double Walls;
            public double Voids;
            public double Isolation;
            public double Branching;
            public double Score;
        }

        [Test]
        public void BakedCatalog_HarderModesTrendHigherComplexity()
        {
            var averages = new Dictionary<Difficulty, ModeAverages>();
            foreach (Difficulty d in EditModeTestSupport.PlayableDifficulties)
                averages[d] = ComputeAverages(d);

            // Extra Hard combines the former Extra and Ultra baked source catalogs.
            // Use generous 30% margins so minor bake drift does not flake.
            Assert.Less(averages[Difficulty.Easy].Walls + 3.0, averages[Difficulty.Medium].Walls);
            Assert.Less(averages[Difficulty.Medium].Walls + 2.0, averages[Difficulty.Hard].Walls);
            Assert.Less(averages[Difficulty.Hard].Walls + 1.5, averages[Difficulty.ExtraHard].Walls);

            Assert.Less(averages[Difficulty.Easy].Score + 5.0, averages[Difficulty.Medium].Score);
            Assert.Less(averages[Difficulty.Medium].Score + 5.0, averages[Difficulty.Hard].Score);
            Assert.Less(averages[Difficulty.Hard].Score + 5.0, averages[Difficulty.ExtraHard].Score);

            foreach (Difficulty d in EditModeTestSupport.PlayableDifficulties)
            {
                Assert.LessOrEqual(averages[d].Isolation, LevelBakeValidator.MaxIsolatedWallRatio + 0.05,
                    $"{d} isolation");
                Assert.GreaterOrEqual(averages[d].Branching, 2.0, $"{d} branching");
            }
        }

        private ModeAverages ComputeAverages(Difficulty difficulty)
        {
            int count = _provider.BakedCountFor(difficulty);
            double walls = 0;
            double voids = 0;
            double isolation = 0;
            double branching = 0;
            double score = 0;

            for (int index = 1; index <= count; index++)
            {
                Level level = _provider.GetLevel(difficulty, index);
                LevelQualityMetrics metrics = _scorer.Score(level, difficulty);
                walls += metrics.WallCount;
                voids += metrics.VoidCount;
                isolation += metrics.IsolatedWallRatio;
                branching += metrics.AverageBranching;
                score += metrics.Score;
            }

            return new ModeAverages
            {
                Walls = walls / count,
                Voids = voids / count,
                Isolation = isolation / count,
                Branching = branching / count,
                Score = score / count
            };
        }
    }
}
