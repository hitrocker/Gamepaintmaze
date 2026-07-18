using System.Collections.Generic;
using NUnit.Framework;
using PaintMaze.Core;
using PaintMaze.Domain;
using PaintMaze.Game;

namespace PaintMaze.Tests
{
    public class HintRouteTests
    {
        private readonly Solver _solver = new Solver();

        [Test]
        public void NextProgressMove_BuildsOneCompleteWallToWallSegment()
        {
            Level level = TestHelpers.Make(
                "S..",
                "##.",
                "...");
            var painted = new HashSet<Position> { level.Spawn };

            SwipeDirection? next =
                _solver.SuggestProgressMove(level, level.Spawn, painted);
            List<HintRouteSegment> route =
                HintRouteBuilder.Build(level, level.Spawn, new[] { next.Value });

            Assert.AreEqual(SwipeDirection.Right, next);
            Assert.AreEqual(1, route.Count);
            Assert.AreEqual(level.Spawn, route[0].Start);
            Assert.AreEqual(new Position(0, 2), route[0].Stop);
            Assert.That(route[0].Cells, Is.EqualTo(new[] {
                new Position(0, 1),
                new Position(0, 2)
            }));
        }

        [Test]
        public void PartialPaintedState_SuggestsOnlyTheNextProgressSegment()
        {
            Level level = TestHelpers.Make(
                "S..",
                "##.",
                "...");
            var painted = new HashSet<Position>
            {
                new Position(0, 0),
                new Position(0, 1),
                new Position(0, 2)
            };
            Position current = new Position(0, 2);

            SwipeDirection? next =
                _solver.SuggestProgressMove(level, current, painted);
            List<HintRouteSegment> route =
                HintRouteBuilder.Build(level, current, new[] { next.Value });

            Assert.AreEqual(SwipeDirection.Down, next);
            Assert.AreEqual(1, route.Count);
            Assert.AreEqual(current, route[0].Start);
            Assert.AreEqual(new Position(2, 2), route[0].Stop);
        }

        [Test]
        public void PaintedOnlySuggestedMove_StillBuildsTheFullSlide()
        {
            Level level = TestHelpers.Make(
                "S..",
                "##.",
                "...");
            var painted = new HashSet<Position>
            {
                new Position(0, 0),
                new Position(0, 1),
                new Position(0, 2),
                new Position(1, 2),
                new Position(2, 2)
            };
            Position current = new Position(0, 2);
            SwipeDirection? next =
                _solver.SuggestProgressMove(level, current, painted);

            List<HintRouteSegment> route = HintRouteBuilder.Build(
                level, current, new[] { next.Value });

            Assert.AreEqual(SwipeDirection.Down, next);
            Assert.AreEqual(1, route.Count);
            Assert.AreEqual(new Position(2, 2), route[0].Stop);
            foreach (Position cell in route[0].Cells)
                Assert.IsTrue(painted.Contains(cell));
        }
    }
}
