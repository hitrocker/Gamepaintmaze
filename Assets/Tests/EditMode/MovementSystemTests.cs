using NUnit.Framework;
using PaintMaze.Core;
using PaintMaze.Domain;

namespace PaintMaze.Tests
{
    public class MovementSystemTests
    {
        private readonly MovementSystem _m = new MovementSystem();

        [Test]
        public void Slide_RollsToEdge_AndPaintsPath()
        {
            var level = TestHelpers.Make("S....");
            var (stop, path) = _m.Slide(level, level.Spawn, SwipeDirection.Right);
            Assert.AreEqual(new Position(0, 4), stop);
            Assert.AreEqual(4, path.Count); // cells (0,1)..(0,4), excludes start
            Assert.AreEqual(new Position(0, 1), path[0]);
            Assert.AreEqual(new Position(0, 4), path[3]);
        }

        [Test]
        public void Slide_StopsBeforeWall()
        {
            var level = TestHelpers.Make("S.#..");
            var (stop, path) = _m.Slide(level, level.Spawn, SwipeDirection.Right);
            Assert.AreEqual(new Position(0, 1), stop);
            Assert.AreEqual(1, path.Count);
        }

        [Test]
        public void Slide_NoMoveAgainstWall_ReturnsEmptyPath()
        {
            var level = TestHelpers.Make("S#...");
            var (stop, path) = _m.Slide(level, level.Spawn, SwipeDirection.Right);
            Assert.AreEqual(level.Spawn, stop);
            Assert.IsEmpty(path);
        }

        [Test]
        public void Slide_NoMoveAtBoardEdge()
        {
            var level = TestHelpers.Make("S....");
            var (stop, path) = _m.Slide(level, level.Spawn, SwipeDirection.Left);
            Assert.AreEqual(level.Spawn, stop);
            Assert.IsEmpty(path);
        }

        [Test]
        public void Slide_StopsBeforeExteriorVoid()
        {
            var level = TestHelpers.Make("S.._");
            var (stop, path) = _m.Slide(level, level.Spawn, SwipeDirection.Right);
            Assert.AreEqual(new Position(0, 2), stop);
            Assert.AreEqual(2, path.Count);
        }
    }
}
