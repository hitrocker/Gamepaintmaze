using NUnit.Framework;
using PaintMaze.Core;
using PaintMaze.Domain;

namespace PaintMaze.Tests
{
    public class LevelTopologyTests
    {
        [Test]
        public void ExteriorNotches_AreValidVoid()
        {
            Level level = TestHelpers.Make(
                "__###",
                "_S..#",
                "##..#",
                "#####");

            Assert.IsTrue(LevelTopology.HasOnlyExteriorVoid(level));
        }

        [Test]
        public void EnclosedVoid_IsRejected()
        {
            Level level = TestHelpers.Make(
                "#####",
                "#S..#",
                "#._.#",
                "#...#",
                "#####");

            Assert.IsFalse(LevelTopology.HasOnlyExteriorVoid(level));
        }

        [Test]
        public void SealEnclosedVoid_ConvertsOnlyInteriorPocketToWall()
        {
            Level level = TestHelpers.Make(
                "_####",
                "#S..#",
                "#._.#",
                "#...#",
                "#####");

            int repaired = LevelTopology.SealEnclosedVoid(level.Grid);

            Assert.AreEqual(1, repaired);
            Assert.AreEqual(Tile.Void, level.Grid[0, 0]);
            Assert.AreEqual(Tile.Wall, level.Grid[2, 2]);
            Assert.IsTrue(LevelTopology.HasOnlyExteriorVoid(level));
        }

        [Test]
        public void GeneratedRepresentativeLevels_HaveOnlyExteriorVoid()
        {
            var provider = new GeneratedLevelProvider();
            foreach (Difficulty difficulty in EditModeTestSupport.AllDifficulties)
            {
                foreach (int index in new[] { 1, 10, 501, 2000, 10000 })
                {
                    Level level = provider.GetLevel(difficulty, index);
                    Assert.IsTrue(LevelTopology.HasOnlyExteriorVoid(level),
                        $"{difficulty} #{index} contains enclosed background");
                }
            }
        }
    }
}
