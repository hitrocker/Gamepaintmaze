using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PaintMaze.Core;
using PaintMaze.Domain;

namespace PaintMaze.Tests
{
    public class LevelCacheStoreTests
    {
        private string _tempRoot;

        [SetUp]
        public void SetUp() => _tempRoot = EditModeTestSupport.CreateTempDirectory("pm-cache-");

        [TearDown]
        public void TearDown() => EditModeTestSupport.DeleteDirectoryIfExists(_tempRoot);

        [Test]
        public void SaveAndLoadLevel_RoundTripsOneLevel()
        {
            var store = new LevelCacheStore(_tempRoot);
            Level source = new SectionedLevelProvider().GetLevel(Difficulty.Medium, 10);

            store.SaveLevel(Difficulty.Medium, 501, source);
            Assert.IsTrue(store.TryLoadLevel(Difficulty.Medium, 501, out Level loaded));

            Assert.IsTrue(EditModeTestSupport.GridsEqual(source.Grid, loaded.Grid));
            Assert.AreEqual(source.Spawn, loaded.Spawn);
            Assert.AreEqual(Difficulty.Medium, loaded.Difficulty);
            Assert.AreEqual(501, loaded.Index);
        }

        [Test]
        public void CorruptCache_IsIgnoredAndDeleted()
        {
            var store = new LevelCacheStore(_tempRoot);
            string path = Path.Combine(store.Root, "easy", "easy_0000000501.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "not a valid pack {{{");

            Assert.IsFalse(store.TryLoadLevel(Difficulty.Easy, 501, out _));
            Assert.IsFalse(File.Exists(path));
        }

        [Test]
        public void MultipleLevels_OnDisk_AreTreatedAsCorrupt()
        {
            var store = new LevelCacheStore(_tempRoot);
            var baked = new SectionedLevelProvider();
            string path = Path.Combine(store.Root, "easy", "easy_0000000501.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, LevelPackSerializer.ToPack(new[]
            {
                baked.GetLevel(Difficulty.Easy, 1),
                baked.GetLevel(Difficulty.Easy, 2)
            }));

            Assert.IsFalse(store.TryLoadLevel(Difficulty.Easy, 501, out _));
            Assert.IsFalse(File.Exists(path));
        }

        [Test]
        public void EnclosedVoid_OnDisk_IsTreatedAsCorrupt()
        {
            var store = new LevelCacheStore(_tempRoot);
            Level source = TestHelpers.Make(
                "S##",
                "#_#",
                "###");
            var level = new Level(source.Grid, source.Spawn, Difficulty.Easy, 501);

            store.SaveLevel(Difficulty.Easy, 501, level);
            string path = Path.Combine(store.Root, "easy", "easy_0000000501.txt");

            Assert.IsFalse(store.TryLoadLevel(Difficulty.Easy, 501, out _));
            Assert.IsFalse(File.Exists(path));
        }

        [Test]
        public void StaleGeneratorVersion_OnDisk_IsTreatedAsCorrupt()
        {
            var store = new LevelCacheStore(_tempRoot);
            Level level = new SectionedLevelProvider().GetLevel(Difficulty.Easy, 1);
            store.SaveLevel(Difficulty.Easy, 501, level);

            string path = Path.Combine(store.Root, "easy", "easy_0000000501.txt");
            string current =
                $"# generator-version={LevelGenerator.VersionFor(Difficulty.Easy)}";
            File.WriteAllText(path, File.ReadAllText(path).Replace(current, "# generator-version=0"));

            Assert.IsFalse(store.TryLoadLevel(Difficulty.Easy, 501, out _));
            Assert.IsFalse(File.Exists(path));
        }

        [Test]
        public void VersionInvalidation_IsScopedToExtraHard()
        {
            var store = new LevelCacheStore(_tempRoot);
            Level easy = new SectionedLevelProvider().GetLevel(Difficulty.Easy, 1);
            store.SaveLevel(Difficulty.Easy, 501, easy);
            Assert.IsTrue(store.TryLoadLevel(Difficulty.Easy, 501, out _),
                "unchanged v3 lower-mode cache should remain valid");

            Level source = new Level(
                easy.Grid, easy.Spawn, Difficulty.ExtraHard, 1001);
            store.SaveLevel(Difficulty.ExtraHard, 1001, source);
            string path = Path.Combine(
                store.Root, "extrahard", "extrahard_0000001001.txt");
            string current =
                $"# generator-version={LevelGenerator.VersionFor(Difficulty.ExtraHard)}";
            File.WriteAllText(path, File.ReadAllText(path).Replace(
                current, "# generator-version=4"));

            Assert.IsFalse(store.TryLoadLevel(Difficulty.ExtraHard, 1001, out _));
            Assert.IsFalse(File.Exists(path), "stale Extra Hard v4 cache should be invalidated");
        }

        [Test]
        public void UnsolvableLevels_OnDisk_AreTreatedAsCorrupt()
        {
            var store = new LevelCacheStore(_tempRoot);
            Level source = TestHelpers.Make(
                "S#.",
                "###");
            var level = new Level(source.Grid, source.Spawn, Difficulty.Hard, 501);

            store.SaveLevel(Difficulty.Hard, 501, level);
            string path = Path.Combine(store.Root, "hard", "hard_0000000501.txt");

            Assert.IsFalse(store.TryLoadLevel(Difficulty.Hard, 501, out _));
            Assert.IsFalse(File.Exists(path));
        }

        [Test]
        public void MalformedSpawn_OnDisk_IsTreatedAsCorrupt()
        {
            var store = new LevelCacheStore(_tempRoot);
            string path = Path.Combine(store.Root, "medium", "medium_0000000501.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path,
                "# PaintMaze deterministic level section\n" +
                $"# generator-version={LevelGenerator.VersionFor(Difficulty.Medium)}\n\n" +
                "Medium 501\n" +
                "...\n" +
                "...\n");

            Assert.IsFalse(store.TryLoadLevel(Difficulty.Medium, 501, out _));
            Assert.IsFalse(File.Exists(path));
        }
    }
}
