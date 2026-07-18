using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PaintMaze.Core;
using PaintMaze.Domain;
using PaintMaze.Game;
using PaintMaze.Services;

namespace PaintMaze.Tests
{
    public class ActiveLevelSessionStoreTests
    {
        private string _tempRoot;
        private ActiveLevelSessionStore _store;

        [SetUp]
        public void SetUp()
        {
            _tempRoot =
                EditModeTestSupport.CreateTempDirectory("pm-active-session-");
            _store = new ActiveLevelSessionStore(_tempRoot);
        }

        [TearDown]
        public void TearDown() =>
            EditModeTestSupport.DeleteDirectoryIfExists(_tempRoot);

        [Test]
        public void GameState_TryRestorePreservesCommittedProgress()
        {
            Level level = OpenLevel(Difficulty.Easy, 7);
            var painted = new[]
            {
                level.Spawn,
                new Position(0, 1),
                new Position(0, 2)
            };

            bool restored = GameState.TryRestore(
                level, new Position(0, 2), painted, 3, out GameState state);

            Assert.IsTrue(restored);
            Assert.AreEqual(new Position(0, 2), state.BallPos);
            Assert.AreEqual(3, state.MoveCount);
            CollectionAssert.AreEquivalent(painted, state.Painted);
            Assert.IsFalse(state.IsComplete);
        }

        [Test]
        public void SaveAndLoad_RoundTripsOwnerAndCommittedState()
        {
            Level level = OpenLevel(Difficulty.Easy, 9);
            GameState original = ProgressedState(level);
            ActiveLevelSnapshot snapshot =
                ActiveLevelSnapshot.From(original, "player-one");
            _store.Save(snapshot);

            bool loaded = _store.TryLoad(
                "player-one", level, out ActiveLevelSnapshot stored,
                out GameState restored);

            Assert.IsTrue(loaded);
            Assert.AreEqual("player-one", stored.owner);
            Assert.AreEqual(original.BallPos, restored.BallPos);
            Assert.AreEqual(original.MoveCount, restored.MoveCount);
            CollectionAssert.AreEquivalent(original.Painted, restored.Painted);
        }

        [Test]
        public void Store_IsolatesOwnersAndDifficulties()
        {
            Level easy = OpenLevel(Difficulty.Easy, 3);
            Level hard = OpenLevel(Difficulty.Hard, 3);
            _store.Save(ActiveLevelSnapshot.From(
                ProgressedState(easy), "player-a"));
            _store.Save(ActiveLevelSnapshot.From(
                ProgressedState(hard), "player-a"));

            Assert.IsFalse(_store.TryLoad(
                "player-b", easy, out _, out _));
            Assert.IsTrue(_store.TryLoad(
                "player-a", easy, out _, out _));
            Assert.IsTrue(_store.TryLoad(
                "player-a", hard, out _, out _));
        }

        [Test]
        public void Clear_RemovesOnlyRequestedDifficulty()
        {
            Level easy = OpenLevel(Difficulty.Easy, 4);
            Level medium = OpenLevel(Difficulty.Medium, 4);
            _store.Save(ActiveLevelSnapshot.From(
                ProgressedState(easy), "player"));
            _store.Save(ActiveLevelSnapshot.From(
                ProgressedState(medium), "player"));

            _store.Clear("player", Difficulty.Easy);

            Assert.IsFalse(_store.TryLoad(
                "player", easy, out _, out _));
            Assert.IsTrue(_store.TryLoad(
                "player", medium, out _, out _));
        }

        [Test]
        public void CorruptSnapshot_IsDeletedAndIgnored()
        {
            Level level = OpenLevel(Difficulty.Easy, 5);
            string path = _store.PathFor("player", Difficulty.Easy);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "{ definitely not valid json");

            Assert.IsFalse(_store.TryLoad(
                "player", level, out _, out _));
            Assert.IsFalse(File.Exists(path));
        }

        [Test]
        public void FingerprintMismatch_IsDeletedAndIgnored()
        {
            Level original = OpenLevel(Difficulty.Easy, 6);
            _store.Save(ActiveLevelSnapshot.From(
                ProgressedState(original), "player"));

            Tile[,] changedGrid = EditModeTestSupport.CloneGrid(original.Grid);
            changedGrid[1, 2] = Tile.Void;
            var changed = new Level(
                changedGrid, original.Spawn,
                original.Difficulty, original.Index);

            Assert.IsFalse(_store.TryLoad(
                "player", changed, out _, out _));
            Assert.IsFalse(File.Exists(
                _store.PathFor("player", Difficulty.Easy)));
        }

        [Test]
        public void WrongCatalogOrderVersion_IsRejectedForBakedLevel()
        {
            Level level = OpenLevel(Difficulty.Easy, 10);
            ActiveLevelSnapshot snapshot = ActiveLevelSnapshot.From(
                ProgressedState(level), "player");
            snapshot.catalogOrderVersion =
                LevelCatalogOrder.OrderVersion + 1;
            _store.Save(snapshot);

            Assert.IsFalse(_store.TryLoad(
                "player", level, out _, out _));
        }

        [Test]
        public void CompletedState_IsNotPersisted()
        {
            var grid = new[,] { { Tile.Floor, Tile.Floor } };
            var level = new Level(
                grid, new Position(0, 0), Difficulty.Easy, 1);
            var state = new GameState(level);
            state.Apply(
                new Position(0, 1),
                new[] { new Position(0, 1) });

            Assert.IsTrue(state.IsComplete);
            Assert.IsNull(ActiveLevelSnapshot.From(state, "player"));
        }

        [Test]
        public void PortraitGameplayCoordinates_RoundTrip()
        {
            var landscapeGrid = new[,]
            {
                { Tile.Floor, Tile.Floor, Tile.Floor, Tile.Floor },
                { Tile.Floor, Tile.Floor, Tile.Floor, Tile.Floor }
            };
            var source = new Level(
                landscapeGrid, new Position(0, 0),
                Difficulty.Medium, 12);
            Level gameplay = PortraitLevelOrientation.Apply(source);
            GameState original = ProgressedState(gameplay);
            _store.Save(ActiveLevelSnapshot.From(
                original, "portrait-player"));

            Assert.IsTrue(_store.TryLoad(
                "portrait-player", gameplay, out _, out GameState restored));
            Assert.AreEqual(original.BallPos, restored.BallPos);
            CollectionAssert.AreEquivalent(original.Painted, restored.Painted);
        }

        [TestCase(Difficulty.Easy, 1)]
        [TestCase(Difficulty.Easy, 501)]
        public void ProviderLevels_RoundTripAcrossBakedShuffleAndGeneratedRange(
            Difficulty difficulty, int visibleIndex)
        {
            string cacheRoot = Path.Combine(_tempRoot, "generated");
            var provider = new ScalableLevelProvider(
                new SectionedLevelProvider(),
                new DeterministicBatchLevelProvider(
                    new LevelCacheStore(cacheRoot)));
            Level level = PortraitLevelOrientation.Apply(
                provider.GetLevel(difficulty, visibleIndex));
            Assert.IsNotNull(level);
            GameState original = ProgressedState(level);
            _store.Save(ActiveLevelSnapshot.From(
                original, "provider-player"));

            Assert.IsTrue(_store.TryLoad(
                "provider-player", level, out _, out GameState restored));
            Assert.AreEqual(visibleIndex, restored.Level.Index);
            Assert.AreEqual(original.BallPos, restored.BallPos);
        }

        private static Level OpenLevel(Difficulty difficulty, int index)
        {
            var grid = new[,]
            {
                { Tile.Floor, Tile.Floor, Tile.Floor },
                { Tile.Floor, Tile.Floor, Tile.Floor },
                { Tile.Floor, Tile.Floor, Tile.Floor }
            };
            return new Level(grid, new Position(0, 0), difficulty, index);
        }

        private static GameState ProgressedState(Level level)
        {
            Position target = FirstOtherFloor(level);
            var state = new GameState(level);
            state.Apply(target, new List<Position> { target });
            return state;
        }

        private static Position FirstOtherFloor(Level level)
        {
            for (int row = 0; row < level.Rows; row++)
                for (int col = 0; col < level.Cols; col++)
                {
                    var position = new Position(row, col);
                    if (position != level.Spawn && level.IsFloor(position))
                        return position;
                }
            Assert.Fail("Level needs at least two floor cells.");
            return level.Spawn;
        }
    }
}
