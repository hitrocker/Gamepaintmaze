using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using PaintMaze.Core;
using PaintMaze.Domain;

namespace PaintMaze.Tests
{
    public class LevelVarietyTests
    {
        [Test]
        public void EasyRun_IsNotOneSizeWallRatioAndLayoutFamily()
        {
            var generator = new LevelGenerator();
            var solver = new Solver();
            var scorer = new LevelQualityScorer(solver);
            var options = new LevelGenerationOptions { AttemptCount = 2 };
            var sizes = new HashSet<(int, int)>();
            var families = new HashSet<LevelLayoutFamilyKind>();
            double minWall = double.MaxValue;
            double maxWall = double.MinValue;

            for (int index = 1; index <= 12; index++)
            {
                Level level = generator.Generate(Difficulty.Easy, index, options);
                Assert.IsTrue(solver.IsAlwaysSolvable(level), $"Easy #{index} stuck");
                Assert.IsTrue(LevelTopology.HasOnlyExteriorVoid(level), $"Easy #{index} void");
                LevelQualityMetrics quality = scorer.Score(level, Difficulty.Easy);
                Assert.IsTrue(quality.PassesQualityFloors, $"Easy #{index} failed quality floors");

                LevelSlideStats slides = LevelSlideStats.Measure(level);
                Assert.IsTrue(
                    LevelShapeComparison.HasReadableSlides(level, slides),
                    $"Easy #{index} slides are uniform (one-cell {slides.OneCellRatio:0.00}, longest {slides.Longest})");

                sizes.Add((level.Rows, level.Cols));
                families.Add(LevelLayoutFamily.Classify(level));
                double wallRatio = WallRatio(level);
                if (wallRatio < minWall) minWall = wallRatio;
                if (wallRatio > maxWall) maxWall = wallRatio;
            }

            Assert.GreaterOrEqual(sizes.Count, 2, "sizes: " + string.Join(", ", sizes));
            Assert.GreaterOrEqual(families.Count, 2, "families: " + string.Join(", ", families));
            Assert.Greater(
                maxWall - minWall,
                0.03,
                $"wall ratios {minWall.ToString("0.00", CultureInfo.InvariantCulture)}..{maxWall.ToString("0.00", CultureInfo.InvariantCulture)}");
        }

        [Test]
        public void RuntimeStream_PastTheBakeBoundary_IsVariedAndReadable()
        {
            string cache = Path.Combine(Path.GetTempPath(), "pm-runtime-audit-v5");
            var provider = new ScalableLevelProvider(
                new SectionedLevelProvider(),
                new DeterministicBatchLevelProvider(new LevelCacheStore(cache)));
            var solver = new Solver();
            var movement = new MovementSystem();
            var scorer = new LevelQualityScorer(solver);
            var report = new StringBuilder();
            var problems = new List<string>();

            foreach (Difficulty difficulty in new[]
                     { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard })
            {
                var sizes = new HashSet<(int, int)>();
                var families = new HashSet<LevelLayoutFamilyKind>();
                var recent = new List<LevelShapeProfile>();
                double minWall = double.MaxValue;
                double maxWall = double.MinValue;
                int withReversal = 0;

                for (int index = 501; index <= 506; index++)
                {
                    Level level = provider.GetLevel(difficulty, index);
                    Assert.IsNotNull(level, $"{difficulty} #{index}");
                    Assert.AreEqual(index, level.Index);
                    Assert.IsTrue(solver.IsAlwaysSolvable(level), $"{difficulty} #{index} stuck");
                    Assert.IsTrue(LevelTopology.HasOnlyExteriorVoid(level), $"{difficulty} #{index} void");
                    LevelQualityMetrics quality = scorer.Score(level, difficulty);
                    Assert.IsTrue(quality.PassesQualityFloors, $"{difficulty} #{index} quality");
                    LevelSlideStats slides = LevelSlideStats.Measure(level, movement);
                    Assert.IsTrue(
                        LevelShapeComparison.HasReadableSlides(level, slides),
                        $"{difficulty} #{index} slides one-cell {slides.OneCellRatio:0.00} longest {slides.Longest}");

                    LevelShapeProfile profile = LevelShapeProfile.Capture(level);
                    LevelShapeProfile repeated = null;
                    foreach (LevelShapeProfile past in recent)
                    {
                        if (LevelShapeComparison.IsNearDuplicate(profile, past))
                        {
                            repeated = past;
                            break;
                        }
                    }

                    report.AppendLine(
                        $"{difficulty} #{index} {level.Rows}x{level.Cols} {profile.Family} " +
                        $"walls {profile.WallRatio.ToString("0.00", CultureInfo.InvariantCulture)} " +
                        $"slide {profile.LongestSlide} one-cell {slides.OneCellRatio.ToString("0.00", CultureInfo.InvariantCulture)} " +
                        $"turns {profile.Turns} rev {profile.Reversals} " +
                        $"repeat {(repeated == null ? "no" : "YES " + repeated.Rows + "x" + repeated.Cols + " " + repeated.Family)}");
                    if (repeated != null)
                        problems.Add($"{difficulty} #{index} repeats a recent {repeated.Family} board");
                    if (profile.Reversals > 0) withReversal++;
                    recent.Add(profile);
                    if (recent.Count > LevelShapeComparison.RecentWindow)
                        recent.RemoveAt(0);

                    sizes.Add((level.Rows, level.Cols));
                    families.Add(profile.Family);
                    double wallRatio = WallRatio(level);
                    if (wallRatio < minWall) minWall = wallRatio;
                    if (wallRatio > maxWall) maxWall = wallRatio;

                    report.AppendLine(BoardText(level));
                }

                Assert.GreaterOrEqual(sizes.Count, 2, difficulty + " sizes " + string.Join(", ", sizes));
                Assert.GreaterOrEqual(families.Count, 2, difficulty + " families " + string.Join(", ", families));
                Assert.Greater(maxWall - minWall, 0.03, difficulty + " wall spread");
                Assert.Greater(withReversal, 0, difficulty + " had no reverse");
            }

            File.WriteAllText("/tmp/level-audit.txt", report.ToString());
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void MirrorOfRecentLevel_IsRejected()
        {
            Level original = TestHelpers.Make(
                "S..#",
                ".##.",
                "..._",
                "..#.");
            LevelShapeProfile recent = LevelShapeProfile.Capture(original);
            var history = new[] { recent };

            Level mirror = EditModeTestSupport.TransformLevel(original, rotation: 0, mirror: true);
            Level rotated = EditModeTestSupport.TransformLevel(original, rotation: 1, mirror: false);
            Assert.AreEqual(1.0, LevelShapeComparison.GetSimilarityScore(
                LevelShapeProfile.Capture(mirror), recent), 0.0001);
            Assert.IsTrue(LevelShapeComparison.IsRejected(mirror, history));
            Assert.IsTrue(LevelShapeComparison.IsRejected(rotated, history));

            Level other = TestHelpers.Make(
                "S#",
                "..",
                "_.");
            Assert.IsFalse(LevelShapeComparison.IsRejected(other, history));
            Assert.Less(
                LevelShapeComparison.GetSimilarityScore(LevelShapeProfile.Capture(other), recent),
                1.0);
        }

        [Test]
        public void StandardTargets_ArePositive()
        {
            Assert.Greater(LevelDifficultyAnalyzer.TargetScore(Difficulty.Easy, 1), 0);
            Assert.Greater(LevelDifficultyAnalyzer.TargetScore(Difficulty.Medium, 1),
                LevelDifficultyAnalyzer.TargetScore(Difficulty.Easy, 1));
            Assert.Greater(LevelDifficultyAnalyzer.TargetScore(Difficulty.Hard, 1),
                LevelDifficultyAnalyzer.TargetScore(Difficulty.Medium, 1));
        }

        private static string BoardText(Level level)
        {
            var text = new StringBuilder();
            for (int row = 0; row < level.Rows; row++)
            {
                for (int col = 0; col < level.Cols; col++)
                {
                    if (level.Spawn.Row == row && level.Spawn.Col == col)
                        text.Append('S');
                    else if (level.Grid[row, col] == Tile.Wall)
                        text.Append('#');
                    else if (level.Grid[row, col] == Tile.Void)
                        text.Append('_');
                    else
                        text.Append('.');
                }

                text.AppendLine();
            }

            return text.ToString();
        }

        private static double WallRatio(Level level)
        {
            int walls = 0;
            int occupied = 0;
            for (int r = 0; r < level.Rows; r++)
            {
                for (int c = 0; c < level.Cols; c++)
                {
                    if (level.Grid[r, c] == Tile.Void) continue;
                    occupied++;
                    if (level.Grid[r, c] == Tile.Wall) walls++;
                }
            }

            return occupied > 0 ? (double)walls / occupied : 0;
        }
    }
}
