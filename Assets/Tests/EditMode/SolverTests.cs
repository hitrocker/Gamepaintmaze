using System.Collections.Generic;
using NUnit.Framework;
using PaintMaze.Core;
using PaintMaze.Domain;

namespace PaintMaze.Tests
{
    public class SolverTests
    {
        private readonly Solver _solver = new Solver();

        [Test]
        public void SolutionFrom_StraightLine_IsOneMove()
        {
            var level = TestHelpers.Make("S....");
            var sol = _solver.SolutionFrom(level, level.Spawn, new HashSet<Position> { level.Spawn });
            Assert.IsNotNull(sol);
            Assert.AreEqual(1, sol.Count);
            Assert.AreEqual(SwipeDirection.Right, sol[0]);
        }

        [Test]
        public void SolutionFrom_AlreadyComplete_IsEmpty()
        {
            var level = TestHelpers.Make("S");
            var sol = _solver.SolutionFrom(level, level.Spawn, new HashSet<Position> { level.Spawn });
            Assert.IsNotNull(sol);
            Assert.IsEmpty(sol);
        }

        [Test]
        public void SuggestProgressMove_ChoosesImmediateUnpaintedLane()
        {
            var level = TestHelpers.Make("S....");

            SwipeDirection? hint = _solver.SuggestProgressMove(
                level, level.Spawn, new HashSet<Position> { level.Spawn });

            Assert.AreEqual(SwipeDirection.Right, hint);
        }

        [Test]
        public void SuggestProgressMove_NavigatesStopGraphBeforeUnpaintedLane()
        {
            var level = TestHelpers.Make(
                "S..",
                "...",
                "...");
            var painted = new HashSet<Position>();
            for (int row = 0; row < level.Rows; row++)
                for (int col = 0; col < level.Cols; col++)
                    if (level.Grid[row, col] == Tile.Floor)
                        painted.Add(new Position(row, col));
            painted.Remove(new Position(2, 1));

            SwipeDirection? hint = _solver.SuggestProgressMove(
                level, level.Spawn, painted);

            Assert.AreEqual(SwipeDirection.Down, hint,
                "First move should navigate to the bottom-left stop, where Right paints progress.");
        }

        [Test]
        public void SuggestProgressMove_AlreadyComplete_ReturnsNull()
        {
            var level = TestHelpers.Make("S");

            SwipeDirection? hint = _solver.SuggestProgressMove(
                level, level.Spawn, new HashSet<Position> { level.Spawn });

            Assert.IsFalse(hint.HasValue);
        }

        [Test]
        public void IsAlwaysSolvable_True_ForSerpentine()
        {
            var gen = new GeneratedLevelProvider(_solver);
            foreach (int size in new[] { 5, 7, 9 })
            {
                var level = gen.GenerateSerpentine(size, Difficulty.Easy, 1);
                Assert.IsTrue(_solver.IsAlwaysSolvable(level), $"serpentine size {size} should be never-stuck");
            }
        }

        [Test]
        public void IsAlwaysSolvable_False_WhenCoverageFails()
        {
            // (0,2) floor is unreachable: no slide ever crosses it.
            var level = TestHelpers.Make(
                "S#.",
                "###");
            NeverStuckAnalysis analysis = _solver.AnalyzeNeverStuck(level);
            Assert.IsFalse(analysis.Passes);
            Assert.AreEqual(NeverStuckFailureKind.UncoveredFloor, analysis.FailureKind);
            Assert.AreEqual(1, analysis.CoveredFloorCount);
            Assert.AreEqual(2, analysis.TotalFloorCount);
            Assert.AreEqual(new Position(0, 2), analysis.UncoveredFloors[0]);
            Assert.IsEmpty(analysis.StrandedStops);
            Assert.AreEqual(analysis.Passes, _solver.IsAlwaysSolvable(level));
            Assert.IsFalse(LevelSafetyValidator.IsSafe(level, _solver, out string reason));
            StringAssert.Contains("uncovered floor (1/2", reason);
        }

        [Test]
        public void IsAlwaysSolvable_False_WhenNotStronglyConnected()
        {
            // Fully covered, but a reachable node cannot navigate back to spawn.
            var level = TestHelpers.Make(
                ".S..",
                "...#",
                "#.#.",
                "....");
            NeverStuckAnalysis analysis = _solver.AnalyzeNeverStuck(level);
            Assert.IsFalse(analysis.Passes);
            Assert.AreEqual(
                NeverStuckFailureKind.NotStronglyConnected, analysis.FailureKind);
            Assert.AreEqual(analysis.TotalFloorCount, analysis.CoveredFloorCount);
            Assert.IsNotEmpty(analysis.ReachableStops);
            Assert.IsNotEmpty(analysis.StrandedStops);
            Assert.AreEqual(analysis.Passes, _solver.IsAlwaysSolvable(level));
            Assert.IsFalse(LevelSafetyValidator.IsSafe(level, _solver, out string reason));
            StringAssert.Contains("stop graph not strongly connected", reason);
        }

        [Test]
        public void SolveMinMoves_MatchesKnownSerpentine()
        {
            var gen = new GeneratedLevelProvider(_solver);
            var level = gen.GenerateSerpentine(5, Difficulty.Easy, 1);
            Assert.AreEqual(5, _solver.SolveMinMoves(level));
        }
    }
}
