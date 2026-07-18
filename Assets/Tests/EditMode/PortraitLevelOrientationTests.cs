using NUnit.Framework;
using PaintMaze.Core;
using PaintMaze.Domain;
using PaintMaze.Game;

namespace PaintMaze.Tests
{
    public class PortraitLevelOrientationTests
    {
        [Test]
        public void LandscapeFloorBounds_RotateClockwiseWithSpawnAndMetadata()
        {
            Level source = TestHelpers.Make(
                "S#.",
                "._.");
            string sourceBytes = LevelPackSerializer.ToBlock(source);

            Level rotated = PortraitLevelOrientation.Apply(source);

            Assert.AreEqual(3, rotated.Rows);
            Assert.AreEqual(2, rotated.Cols);
            Assert.AreEqual(new Position(0, 1), rotated.Spawn);
            Assert.AreEqual(source.Difficulty, rotated.Difficulty);
            Assert.AreEqual(source.Index, rotated.Index);
            Assert.AreEqual(Tile.Floor, rotated.Grid[0, 0]);
            Assert.AreEqual(Tile.Floor, rotated.Grid[0, 1]);
            Assert.AreEqual(Tile.Void, rotated.Grid[1, 0]);
            Assert.AreEqual(Tile.Wall, rotated.Grid[1, 1]);
            Assert.AreEqual(Tile.Floor, rotated.Grid[2, 0]);
            Assert.AreEqual(Tile.Floor, rotated.Grid[2, 1]);
            Assert.AreEqual(sourceBytes, LevelPackSerializer.ToBlock(source),
                "Presentation normalization must not mutate canonical source bytes.");
        }

        [Test]
        public void RawLandscapeWithPortraitFloorBounds_IsNotRotated()
        {
            Level source = TestHelpers.Make(
                "S.___",
                "..___",
                "..___");

            Assert.IsFalse(PortraitLevelOrientation.HasLandscapeFloorBounds(source));
            Assert.AreSame(source, PortraitLevelOrientation.Apply(source));
        }

        [Test]
        public void SquareGridWithLandscapeFloorBounds_RotatesAndIsIdempotent()
        {
            Level source = TestHelpers.Make(
                "S...",
                "....",
                "____",
                "____");

            Level rotated = PortraitLevelOrientation.Apply(source);

            Assert.AreNotSame(source, rotated);
            Assert.IsFalse(PortraitLevelOrientation.HasLandscapeFloorBounds(rotated));
            Assert.AreSame(rotated, PortraitLevelOrientation.Apply(rotated));
        }

        [Test]
        public void ClockwiseRotation_IsMovementIsometry()
        {
            Level source = TestHelpers.Make(
                "...",
                "S..");
            Level rotated = PortraitLevelOrientation.Apply(source);
            var movement = new MovementSystem();

            var sourceSlide = movement.Slide(source, source.Spawn, SwipeDirection.Up);
            var rotatedSlide = movement.Slide(rotated, rotated.Spawn, SwipeDirection.Right);

            Position expectedRotatedStop = RotateClockwise(sourceSlide.stop, source.Rows);
            Assert.AreEqual(expectedRotatedStop, rotatedSlide.stop);
            Assert.AreEqual(sourceSlide.path.Count, rotatedSlide.path.Count);
            for (int i = 0; i < sourceSlide.path.Count; i++)
            {
                Assert.AreEqual(
                    RotateClockwise(sourceSlide.path[i], source.Rows),
                    rotatedSlide.path[i]);
            }
        }

        [Test]
        public void CertifiedWideArena_PreservesSafetyTopologyFingerprintAndSourceBytes()
        {
            Assert.IsTrue(OpenExtraHardFallbackBank.TrySelect(
                OpenExtraHardFallbackBank.Load(),
                16,
                22,
                Difficulty.UltraHard,
                501,
                out Level source));
            string sourceBytes = LevelPackSerializer.ToBlock(source);

            Level rotated = PortraitLevelOrientation.Apply(source);
            var solver = new Solver();
            LevelFingerprint sourceFingerprint = LevelFingerprint.From(source);
            LevelFingerprint rotatedFingerprint = LevelFingerprint.From(rotated);

            Assert.AreEqual(22, rotated.Rows);
            Assert.AreEqual(16, rotated.Cols);
            Assert.AreEqual(source.TotalPaintable, rotated.TotalPaintable);
            Assert.AreEqual(Count(source, Tile.Wall), Count(rotated, Tile.Wall));
            Assert.AreEqual(Count(source, Tile.Void), Count(rotated, Tile.Void));
            Assert.IsTrue(LevelSafetyValidator.IsSafe(rotated, solver, out string safety), safety);
            Assert.IsTrue(LevelTopology.HasOnlyExteriorVoid(rotated.Grid));
            Assert.AreEqual(sourceFingerprint.Hash, rotatedFingerprint.Hash);
            Assert.IsTrue(LevelFingerprint.CanonicalBytesEqual(
                sourceFingerprint.CanonicalBytes,
                rotatedFingerprint.CanonicalBytes));
            Assert.AreEqual(sourceBytes, LevelPackSerializer.ToBlock(source),
                "The provider/cache representation must remain canonical.");
        }

        private static Position RotateClockwise(Position position, int sourceRows) =>
            new Position(position.Col, sourceRows - 1 - position.Row);

        private static int Count(Level level, Tile tile)
        {
            int count = 0;
            for (int row = 0; row < level.Rows; row++)
                for (int col = 0; col < level.Cols; col++)
                    if (level.Grid[row, col] == tile) count++;
            return count;
        }
    }
}
