using NUnit.Framework;
using PaintMaze.Core;
using PaintMaze.Domain;

namespace PaintMaze.Tests
{
    public class GeneratedLevelProviderTests
    {
        private readonly Solver _solver = new Solver();

        [Test]
        public void BakedCountFor_IsZero_ForEndlessGenerator()
        {
            var gen = new GeneratedLevelProvider(_solver);
            foreach (Difficulty d in EditModeTestSupport.AllDifficulties)
                Assert.AreEqual(0, gen.BakedCountFor(d), d.ToString());
        }

        [Test]
        public void EveryGeneratedLevel_IsNeverStuck_ForAllFiveModes()
        {
            var gen = new GeneratedLevelProvider(_solver);
            foreach (Difficulty d in EditModeTestSupport.AllDifficulties)
            {
                for (int i = 1; i <= 10; i++)
                {
                    var level = gen.GetLevel(d, i);
                    Assert.IsNotNull(level, $"{d} #{i} null");
                    Assert.AreEqual(d, level.Difficulty);
                    Assert.IsTrue(_solver.IsAlwaysSolvable(level), $"{d} #{i} is not never-stuck");
                }
            }
        }

        [Test]
        public void GeneratedLevels_AreDeterministic()
        {
            var gen = new GeneratedLevelProvider(_solver);
            var a = gen.GetLevel(Difficulty.Hard, 4);
            var b = gen.GetLevel(Difficulty.Hard, 4);
            Assert.AreEqual(a.Rows, b.Rows);
            Assert.AreEqual(a.Spawn, b.Spawn);
            for (int r = 0; r < a.Rows; r++)
                for (int c = 0; c < a.Cols; c++)
                    Assert.AreEqual(a.Grid[r, c], b.Grid[r, c], $"cell {r},{c} differs");
        }

        [Test]
        public void GeneratedLevels_HaveExteriorVoid()
        {
            var gen = new GeneratedLevelProvider(_solver);
            foreach (Difficulty d in EditModeTestSupport.AllDifficulties)
            {
                for (int i = 1; i <= 6; i++)
                {
                    var level = gen.GetLevel(d, i);
                    bool hasVoid = false;
                    for (int r = 0; r < level.Rows; r++)
                        for (int c = 0; c < level.Cols; c++)
                            hasVoid |= level.Grid[r, c] == Tile.Void;
                    Assert.IsTrue(hasVoid, $"{d} #{i} has no exterior void");
                }
            }
        }

        [Test]
        public void GeneratedLevels_LimitIsolatedWalls()
        {
            var gen = new GeneratedLevelProvider(_solver);
            foreach (Difficulty d in EditModeTestSupport.AllDifficulties)
            {
                for (int i = 1; i <= 6; i++)
                {
                    var level = gen.GetLevel(d, i);
                    int walls = 0, isolated = 0;
                    for (int r = 0; r < level.Rows; r++)
                    {
                        for (int c = 0; c < level.Cols; c++)
                        {
                            if (level.Grid[r, c] != Tile.Wall) continue;
                            walls++;
                            bool joined =
                                (r > 0 && level.Grid[r - 1, c] == Tile.Wall) ||
                                (r + 1 < level.Rows && level.Grid[r + 1, c] == Tile.Wall) ||
                                (c > 0 && level.Grid[r, c - 1] == Tile.Wall) ||
                                (c + 1 < level.Cols && level.Grid[r, c + 1] == Tile.Wall);
                            if (!joined) isolated++;
                        }
                    }

                    if (walls > 1)
                        Assert.LessOrEqual(isolated / (float)walls, LevelBakeValidator.MaxIsolatedWallRatio,
                            $"{d} #{i} has too many isolated walls ({isolated}/{walls})");
                }
            }
        }

        [Test]
        public void GeneratedLevels_AreNonTrivial()
        {
            var gen = new GeneratedLevelProvider(_solver);
            for (int i = 1; i <= 6; i++)
            {
                var level = gen.GetLevel(Difficulty.Medium, i);
                var mm = _solver.SolveMinMoves(level);
                Assert.IsTrue(mm.HasValue && mm.Value >= 3, $"Medium #{i} too trivial (minMoves={mm})");
            }
        }

        [Test]
        public void HardestModes_TrendToLargerCyclicArenas()
        {
            var gen = new GeneratedLevelProvider(_solver);
            (int cells, int cycles) ArenaScale(Difficulty d)
            {
                int totalCells = 0, totalCycles = 0, count = 0;
                var analyzer = new LevelLayoutAnalyzer();
                for (int i = 1; i <= 8; i++)
                {
                    var lvl = gen.GetLevel(d, i);
                    totalCells += lvl.Rows * lvl.Cols;
                    totalCycles += analyzer.Analyze(lvl).FloorGraphCycleRank;
                    count++;
                }
                return (totalCells / count, totalCycles / count);
            }

            var easy = ArenaScale(Difficulty.Easy);
            var medium = ArenaScale(Difficulty.Medium);
            var extra = ArenaScale(Difficulty.ExtraHard);
            var ultra = ArenaScale(Difficulty.UltraHard);
            Assert.Less(easy.cells, ultra.cells);
            Assert.Less(medium.cells, extra.cells);
            Assert.Less(easy.cycles, ultra.cycles);
            Assert.Less(medium.cycles, extra.cycles);
        }
    }
}
