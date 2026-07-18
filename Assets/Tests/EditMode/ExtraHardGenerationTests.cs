using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PaintMaze.Core;
using PaintMaze.Domain;

namespace PaintMaze.Tests
{
    public class ExtraHardGenerationTests
    {
        [TestCase(Difficulty.ExtraHard, 1, 9, 9)]
        [TestCase(Difficulty.ExtraHard, 126, 10, 10)]
        [TestCase(Difficulty.ExtraHard, 251, 12, 14)]
        [TestCase(Difficulty.ExtraHard, 376, 14, 16)]
        [TestCase(Difficulty.UltraHard, 126, 14, 18)]
        [TestCase(Difficulty.UltraHard, 251, 16, 18)]
        [TestCase(Difficulty.UltraHard, 376, 16, 20)]
        [TestCase(Difficulty.UltraHard, 501, 16, 22)]
        public void GeneratedBands_AreDeterministicSafeAndStructurallyClustered(
            Difficulty difficulty, int index, int rows, int cols)
        {
            var generator = new LevelGenerator();
            var options = new LevelGenerationOptions
            {
                AttemptCount = 3,
                EvaluateMinMoves = false
            };

            Level first = generator.Generate(difficulty, index, options);
            Level second = generator.Generate(difficulty, index, options);

            Assert.AreEqual(rows, first.Rows);
            Assert.AreEqual(cols, first.Cols);
            Assert.AreEqual(
                LevelFingerprint.From(first).Hash,
                LevelFingerprint.From(second).Hash);
            Assert.IsTrue(LevelSafetyValidator.IsSafe(first, new Solver(), out string reason), reason);
            LevelLayoutMetrics layout = new LevelLayoutAnalyzer().Analyze(first);
            Assert.IsTrue(LevelLayoutAnalyzer.MeetsExtraHardFloor(
                first, layout, out string layoutReason), layoutReason);
            Assert.GreaterOrEqual(LargestWallCluster(first), 3,
                "Extra Hard boards should contain structural masses, not only isolated blockers");
        }

        [TestCase(131)]
        [TestCase(253)]
        public void BoundedBakeAnalysis_AcceptsRepresentativeBandLevel(int index)
        {
            var solver = new Solver();
            var scorer = new LevelQualityScorer(solver);
            var analyzer = new LevelDifficultyAnalyzer(solver);
            var generator = new LevelGenerator(solver, scorer);
            string lastReason = null;

            for (int block = 0; block < 8; block++)
            {
                var options = new LevelGenerationOptions
                {
                    AttemptCount = 8,
                    AttemptOffset = block * 8,
                    EvaluateMinMoves = true
                };
                Level candidate = generator.Generate(Difficulty.ExtraHard, index, options);
                LevelQualityMetrics quality = scorer.Score(candidate, Difficulty.ExtraHard);
                LevelDifficultyMetrics difficulty = analyzer.Analyze(
                    candidate,
                    LevelSeed.For(Difficulty.ExtraHard, index,
                        LevelGenerator.VersionFor(Difficulty.ExtraHard), 7001),
                    LevelDifficultyAnalysisOptions.Bake);
                if (LevelBakeValidator.MeetsRequirements(
                    candidate, solver, quality, difficulty,
                    Difficulty.ExtraHard, index, out lastReason))
                    return;
            }

            Assert.Fail("Representative bounded bake candidate was never accepted: " + lastReason);
        }

        [Test]
        public void GuaranteedFallback_RetryVariantsRemainSafeAndCanonicallyDistinct()
        {
            var solver = new Solver();
            var generator = new LevelGenerator(solver);
            var fingerprints = new HashSet<ulong>();

            for (int variant = 0; variant <= 64; variant += 8)
            {
                Level level = generator.GenerateGuaranteedFallback(
                    14, 16, Difficulty.ExtraHard, 321, variant);
                Assert.IsTrue(LevelSafetyValidator.IsSafe(level, solver, out string reason), reason);
                Assert.IsTrue(fingerprints.Add(LevelFingerprint.From(level).Hash),
                    $"Fallback retry variant {variant} duplicated an earlier canonical board.");
            }
        }

        [TestCase(12, 14, Difficulty.ExtraHard, 251)]
        [TestCase(16, 22, Difficulty.UltraHard, 501)]
        public void GeneralizedHamiltonianCorridor_FailsOpenLayoutContract(
            int rows, int cols, Difficulty difficulty, int index)
        {
            var solver = new Solver();
            var generator = new LevelGenerator(solver);
            var layoutAnalyzer = new LevelLayoutAnalyzer();
            Level corridor = generator.GenerateGuaranteedFallback(
                rows, cols, difficulty, index);

            Assert.IsTrue(LevelSafetyValidator.IsSafe(corridor, solver, out string safetyReason),
                safetyReason);
            LevelLayoutMetrics layout = layoutAnalyzer.Analyze(corridor);
            Assert.IsFalse(LevelLayoutAnalyzer.MeetsExtraHardFloor(
                corridor, layout, out string layoutReason));
            Assert.AreEqual("generalized one-cell corridor", layoutReason);
            Assert.AreEqual(0, layout.OpenFloorQuadCount);
            Assert.AreEqual(0, layout.FloorJunctionCount);
            Assert.AreEqual(0, layout.FloorGraphCycleRank);
        }

        [Test]
        public void OpenRoomFixture_PassesGeneralizedLayoutContract()
        {
            Level source = TestHelpers.Make(
                "..._....",
                "........",
                ".....##.",
                "....###.",
                "..#.....",
                "__......",
                "__S._...");
            var level = new Level(
                source.Grid, source.Spawn, Difficulty.ExtraHard, 1);
            LevelLayoutMetrics layout = new LevelLayoutAnalyzer().Analyze(level);

            Assert.IsTrue(LevelLayoutAnalyzer.MeetsExtraHardFloor(
                level, layout, out string reason), reason);
            Assert.Greater(layout.OpenFloorQuadCount, 0);
            Assert.Greater(layout.FloorJunctionCount, 0);
            Assert.Greater(layout.FloorGraphCycleRank, 0);
            Assert.Greater(layout.OpenFloorCoreCount, 0);
            Assert.Greater(layout.OpenFloorCoreCellRatio, 0);
        }

        [Test]
        public void ReferenceArenaFixtures_PassUnchangedNeverStuckContract()
        {
            var solver = new Solver();
            foreach (string[] rows in ReferenceArenaFixtures())
            {
                Level source = TestHelpers.Make(rows);
                var level = new Level(
                    source.Grid, source.Spawn, Difficulty.ExtraHard, 1);

                Assert.IsTrue(
                    LevelSafetyValidator.IsSafe(level, solver, out string reason),
                    reason + Environment.NewLine + string.Join(Environment.NewLine, rows));
                Assert.IsTrue(LevelTopology.HasOnlyExteriorVoid(level));
                LevelLayoutMetrics layout = new LevelLayoutAnalyzer().Analyze(level);
                Assert.IsTrue(
                    LevelLayoutAnalyzer.MeetsExtraHardFloor(
                        level, layout, out string structure),
                    structure + Environment.NewLine + string.Join(Environment.NewLine, rows));
            }
        }

        [Test]
        public void RejectedRibbonFixtures_AreSafeNegativeControls()
        {
            var solver = new Solver();
            foreach (string[] rows in RejectedRibbonFixtures())
            {
                Level source = TestHelpers.Make(rows);
                var level = new Level(
                    source.Grid, source.Spawn, Difficulty.ExtraHard, 1);

                Assert.IsTrue(
                    LevelSafetyValidator.IsSafe(level, solver, out string reason),
                    "A layout regression control must fail structure, not safety: " +
                    reason + Environment.NewLine + string.Join(Environment.NewLine, rows));
                LevelLayoutMetrics layout = new LevelLayoutAnalyzer().Analyze(level);
                Assert.IsFalse(
                    LevelLayoutAnalyzer.MeetsExtraHardFloor(
                        level, layout, out string structure),
                    "Rejected ribbon unexpectedly passed: " +
                    Environment.NewLine + string.Join(Environment.NewLine, rows));
                Assert.AreEqual(0, layout.OpenFloorCoreCount, structure);
            }
        }

        [Test]
        public void ArenaMetricFixture_MatchesSharedGoldenValues()
        {
            Level source = TestHelpers.Make(
                "..._....",
                "........",
                ".....##.",
                "....###.",
                "..#.....",
                "__......",
                "__S._...");
            LevelLayoutMetrics metrics = new LevelLayoutAnalyzer().Analyze(
                new Level(source.Grid, source.Spawn, Difficulty.ExtraHard, 1));

            Assert.AreEqual(4, metrics.OpenFloorCoreCount);
            Assert.AreEqual(24, metrics.OpenFloorCoreCellCount);
            Assert.AreEqual(24.0 / 44.0, metrics.OpenFloorCoreCellRatio, 0.000001);
            Assert.AreEqual(1, metrics.MaximumFloorClearance);
            Assert.AreEqual(10.0 / 44.0, metrics.FloorDegreeFourRatio, 0.000001);
            Assert.AreEqual(15.0 / 44.0, metrics.LargestOpenCoreComponentRatio, 0.000001);
            Assert.AreEqual(2, metrics.InteriorWallIslandCount);
            Assert.AreEqual(1.0, metrics.WallIslandCellRatio, 0.000001);
            Assert.AreEqual(0.0, metrics.ParallelSeparatorWallRatio, 0.000001);
            Assert.AreEqual(1, metrics.ExteriorCutDepth);
            Assert.AreEqual(5, metrics.SilhouetteConcaveCornerCount);
        }

        [TestCase(Difficulty.ExtraHard, 1)]
        [TestCase(Difficulty.ExtraHard, 131)]
        [TestCase(Difficulty.ExtraHard, 253)]
        [TestCase(Difficulty.ExtraHard, 376)]
        [TestCase(Difficulty.UltraHard, 1)]
        [TestCase(Difficulty.UltraHard, 251)]
        [TestCase(Difficulty.UltraHard, 500)]
        public void RepresentativePrototype_IsDeterministicSafeOpenAndExteriorOnly(
            Difficulty difficulty, int index)
        {
            var generator = new LevelGenerator();
            var options = new LevelGenerationOptions { AttemptCount = 1 };
            Level first = generator.Generate(difficulty, index, options);
            Level second = generator.Generate(difficulty, index, options);
            var solver = new Solver();
            LevelLayoutMetrics layout = new LevelLayoutAnalyzer().Analyze(first);

            Assert.AreEqual(
                LevelFingerprint.From(first).Hash,
                LevelFingerprint.From(second).Hash);
            Assert.IsTrue(LevelSafetyValidator.IsSafe(first, solver, out string safety),
                safety);
            Assert.IsTrue(LevelTopology.HasOnlyExteriorVoid(first));
            Assert.IsTrue(LevelLayoutAnalyzer.MeetsExtraHardFloor(
                first, layout, out string structure), structure);
        }

        [Test]
        public void StructuralOperationBudgets_AreFixedByBoardBand()
        {
            Assert.AreEqual(8, LevelGenerator.ExtraHardOperationBudgetFor(9, 9));
            Assert.AreEqual(9, LevelGenerator.ExtraHardOperationBudgetFor(12, 14));
            Assert.AreEqual(10, LevelGenerator.ExtraHardOperationBudgetFor(16, 22));
        }

        [TestCase(Difficulty.ExtraHard, 1)]
        [TestCase(Difficulty.ExtraHard, 126)]
        [TestCase(Difficulty.ExtraHard, 251)]
        [TestCase(Difficulty.ExtraHard, 376)]
        [TestCase(Difficulty.UltraHard, 126)]
        [TestCase(Difficulty.UltraHard, 251)]
        [TestCase(Difficulty.UltraHard, 376)]
        [TestCase(Difficulty.UltraHard, 501)]
        public void ArenaBands_ProduceStructuralDiversityWithinSingleAttemptBudget(
            Difficulty difficulty, int firstIndex)
        {
            var generator = new LevelGenerator();
            var fingerprints = new HashSet<ulong>();
            for (int offset = 0; offset < 4; offset++)
            {
                Level level = generator.Generate(
                    difficulty,
                    firstIndex + offset,
                    new LevelGenerationOptions { AttemptCount = 1 });
                fingerprints.Add(LevelFingerprint.From(level).Hash);
            }
            Assert.GreaterOrEqual(
                fingerprints.Count, 3,
                $"{difficulty} band at {firstIndex} repeated its arena root too often.");
        }

        [Test]
        public void SizeAndOpenComplexity_GrowAcrossRepresentativeBands()
        {
            var generator = new LevelGenerator();
            var analyzer = new LevelLayoutAnalyzer();
            Level small = generator.Generate(Difficulty.ExtraHard, 1);
            Level middle = generator.Generate(Difficulty.ExtraHard, 253);
            Level largest = generator.Generate(Difficulty.UltraHard, 1500);
            LevelLayoutMetrics smallLayout = analyzer.Analyze(small);
            LevelLayoutMetrics middleLayout = analyzer.Analyze(middle);
            LevelLayoutMetrics largestLayout = analyzer.Analyze(largest);

            Assert.Less(small.Rows * small.Cols, middle.Rows * middle.Cols);
            Assert.Less(middle.Rows * middle.Cols, largest.Rows * largest.Cols);
            Assert.Less(small.TotalPaintable, middle.TotalPaintable);
            Assert.Less(middle.TotalPaintable, largest.TotalPaintable);
            Assert.Less(smallLayout.OpenFloorQuadCount, middleLayout.OpenFloorQuadCount);
            Assert.Less(middleLayout.OpenFloorQuadCount, largestLayout.OpenFloorQuadCount);
            Assert.Less(smallLayout.FloorGraphCycleRank, largestLayout.FloorGraphCycleRank);
        }

        [TestCase(9, 9, Difficulty.ExtraHard)]
        [TestCase(10, 10, Difficulty.ExtraHard)]
        [TestCase(12, 14, Difficulty.ExtraHard)]
        [TestCase(14, 16, Difficulty.ExtraHard)]
        [TestCase(14, 18, Difficulty.UltraHard)]
        [TestCase(16, 18, Difficulty.UltraHard)]
        [TestCase(16, 20, Difficulty.UltraHard)]
        [TestCase(16, 22, Difficulty.UltraHard)]
        public void ResourceFallbackBank_CoversEveryArenaSize(
            int rows, int cols, Difficulty difficulty)
        {
            IReadOnlyList<Level> templates = OpenExtraHardFallbackBank.Load();
            Assert.GreaterOrEqual(templates.Count, 8);
            var solver = new Solver();
            var layoutAnalyzer = new LevelLayoutAnalyzer();
            var orientations = new HashSet<string>();

            for (int index = 2000; index < 2016; index++)
            {
                Assert.IsTrue(OpenExtraHardFallbackBank.TrySelect(
                    templates, rows, cols, difficulty, index,
                    out Level level));
                Assert.AreEqual(rows, level.Rows);
                Assert.AreEqual(cols, level.Cols);
                Assert.IsTrue(LevelSafetyValidator.IsSafe(
                    level, solver, out string safety), safety);
                LevelLayoutMetrics layout = layoutAnalyzer.Analyze(level);
                Assert.IsTrue(LevelLayoutAnalyzer.MeetsExtraHardFloor(
                    level, layout, out string structure), structure);
                orientations.Add(RawLayoutKey(level));
            }
            Assert.GreaterOrEqual(orientations.Count, 2);
        }

        [TestCase(1001)]
        [TestCase(2000)]
        public void RuntimeVisiblePrototype_IsSafeOpenAndDeterministic(int visibleIndex)
        {
            string firstRoot = Path.Combine(
                Path.GetTempPath(), "paintmaze-open-a-" + Guid.NewGuid().ToString("N"));
            string secondRoot = Path.Combine(
                Path.GetTempPath(), "paintmaze-open-b-" + Guid.NewGuid().ToString("N"));
            try
            {
                Level first = new DeterministicBatchLevelProvider(
                    new LevelCacheStore(firstRoot), allowBakedRange: true).GetLevel(
                    Difficulty.ExtraHard, visibleIndex);
                Level second = new DeterministicBatchLevelProvider(
                    new LevelCacheStore(secondRoot), allowBakedRange: true).GetLevel(
                    Difficulty.ExtraHard, visibleIndex);
                Assert.AreEqual(
                    LevelFingerprint.From(first).Hash,
                    LevelFingerprint.From(second).Hash);
                Assert.IsTrue(LevelSafetyValidator.IsSafe(
                    first, new Solver(), out string safety), safety);
                LevelLayoutMetrics layout = new LevelLayoutAnalyzer().Analyze(first);
                Assert.IsTrue(LevelLayoutAnalyzer.MeetsExtraHardFloor(
                    first, layout, out string structure), structure);
            }
            finally
            {
                if (Directory.Exists(firstRoot)) Directory.Delete(firstRoot, true);
                if (Directory.Exists(secondRoot)) Directory.Delete(secondRoot, true);
            }
        }

        private static int LargestWallCluster(Level level)
        {
            var seen = new HashSet<Position>();
            int best = 0;
            for (int r = 0; r < level.Rows; r++)
            {
                for (int c = 0; c < level.Cols; c++)
                {
                    var start = new Position(r, c);
                    if (level.Grid[r, c] != Tile.Wall || !seen.Add(start)) continue;
                    int count = 0;
                    var queue = new Queue<Position>();
                    queue.Enqueue(start);
                    while (queue.Count > 0)
                    {
                        Position current = queue.Dequeue();
                        count++;
                        Visit(current.Row - 1, current.Col);
                        Visit(current.Row + 1, current.Col);
                        Visit(current.Row, current.Col - 1);
                        Visit(current.Row, current.Col + 1);
                    }
                    if (count > best) best = count;

                    void Visit(int row, int col)
                    {
                        if (row < 0 || row >= level.Rows || col < 0 || col >= level.Cols ||
                            level.Grid[row, col] != Tile.Wall)
                            return;
                        var next = new Position(row, col);
                        if (seen.Add(next)) queue.Enqueue(next);
                    }
                }
            }
            return best;
        }

        private static IEnumerable<string[]> ReferenceArenaFixtures()
        {
            yield return new[]
            {
                "..._....",
                "........",
                ".....##.",
                "....###.",
                "..#.....",
                "__......",
                "__S._..."
            };
            yield return new[]
            {
                "...___...",
                ".........",
                ".....###.",
                "_....###.",
                "_.....##.",
                ".....S##.",
                ".........",
                "....__..."
            };
            yield return new[]
            {
                ".....___.",
                ".........",
                "........S",
                ".###..___",
                ".###..###",
                ".##....##",
                ".####....",
                ".....##..",
                ".....##.."
            };
        }

        private static IEnumerable<string[]> RejectedRibbonFixtures()
        {
            yield return new[]
            {
                "S........",
                ".........",
                "########.",
                ".........",
                ".........",
                ".########",
                ".........",
                "........."
            };
            yield return new[]
            {
                "S.........#...",
                ".......#..#..#",
                "######.#..#..#",
                ".......#..#..#",
                ".......#..#..#",
                ".#######.....#",
                ".#######.#####",
                ".....#........",
                "..#..#.#......",
                "..#..#.######.",
                "..#..#.#......",
                "..#....#......"
            };
        }

        private static string RawLayoutKey(Level level)
        {
            var rows = new string[level.Rows];
            for (int r = 0; r < level.Rows; r++)
            {
                var chars = new char[level.Cols];
                for (int c = 0; c < level.Cols; c++)
                {
                    var position = new Position(r, c);
                    chars[c] = position == level.Spawn
                        ? 'S'
                        : level.Grid[r, c] == Tile.Floor
                            ? '.'
                            : level.Grid[r, c] == Tile.Wall ? '#' : '_';
                }
                rows[r] = new string(chars);
            }
            return string.Join("\n", rows);
        }
    }
}
