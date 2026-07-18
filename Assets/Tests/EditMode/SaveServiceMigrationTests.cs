using System.IO;
using NUnit.Framework;
using PaintMaze.Domain;
using PaintMaze.Services;
using UnityEngine;

namespace PaintMaze.Tests
{
    public class SaveServiceMigrationTests
    {
        [TearDown]
        public void TearDown()
        {
            SaveService.SetProgressOwner(null);
            EditModeTestSupport.ClearPlayerPrefs();
        }

        [SetUp]
        public void SetUp()
        {
            SaveService.SetProgressOwner(null);
            EditModeTestSupport.ClearPlayerPrefs();
        }

        [Test]
        public void Migration_ResetsUnlockProgress_ToOne()
        {
            SeedLegacyProgress(unlockedLevel: 47);
            _ = SaveService.HighestUnlocked(Difficulty.Easy);
            Assert.AreEqual(1, SaveService.HighestUnlocked(Difficulty.Easy));
            Assert.AreEqual(1, SaveService.HighestUnlocked(Difficulty.UltraHard));
        }

        [Test]
        public void Migration_PreservesThemeSoundAndHaptics()
        {
            SeedLegacyProgress(unlockedLevel: 12);
            PlayerPrefs.SetInt("pm_theme", (int)ThemeMode.Light);
            PlayerPrefs.SetInt("pm_sound", 0);
            PlayerPrefs.SetInt("pm_haptics", 0);
            PlayerPrefs.Save();

            _ = SaveService.HighestUnlocked(Difficulty.Medium);

            Assert.AreEqual(ThemeMode.Light, SaveService.ThemeMode);
            Assert.IsFalse(SaveService.SoundEnabled);
            Assert.IsFalse(SaveService.HapticsEnabled);
        }

        [Test]
        public void Migration_IsIdempotent()
        {
            SeedLegacyProgress(unlockedLevel: 25);
            SaveService.MarkCompleted(Difficulty.Hard, 3);
            int unlocked = SaveService.HighestUnlocked(Difficulty.Hard);
            Assert.AreEqual(4, unlocked);
            Assert.AreEqual(SaveService.CurrentSchemaVersion, PlayerPrefs.GetInt("pm_save_schema", 0));

            // Second migration pass must not reset progress again.
            SaveService.EnsureMigrated();
            Assert.AreEqual(4, SaveService.HighestUnlocked(Difficulty.Hard));
            Assert.AreEqual(ThemeMode.Dark, SaveService.ThemeMode);
        }

        [Test]
        public void Migration_SetsCurrentSchemaVersion()
        {
            SeedLegacyProgress(unlockedLevel: 9);
            SaveService.EnsureMigrated();
            Assert.AreEqual(SaveService.CurrentSchemaVersion, PlayerPrefs.GetInt("pm_save_schema"));
        }

        [Test]
        public void Schema3_OffsetsUltraProgressIntoExtraHard()
        {
            SeedSchema2(extraUnlocked: 420, ultraUnlocked: 75);

            SaveService.EnsureMigrated();

            Assert.AreEqual(575, SaveService.HighestUnlocked(Difficulty.ExtraHard));
            Assert.IsFalse(PlayerPrefs.HasKey("pm_unlocked_4"));
        }

        [Test]
        public void Schema3_KeepsHigherExistingExtraHardProgress()
        {
            SeedSchema2(extraUnlocked: 800, ultraUnlocked: 75);

            SaveService.EnsureMigrated();

            Assert.AreEqual(800, SaveService.HighestUnlocked(Difficulty.ExtraHard));
        }

        [Test]
        public void Schema3_RemapSelectedUltraHardToExtraHard()
        {
            SeedSchema2(extraUnlocked: 1, ultraUnlocked: 1);
            PlayerPrefs.SetInt("pm_difficulty", (int)Difficulty.UltraHard);

            SaveService.EnsureMigrated();

            Assert.AreEqual(Difficulty.ExtraHard, SaveService.SelectedDifficulty);
            Assert.AreEqual((int)Difficulty.ExtraHard, PlayerPrefs.GetInt("pm_difficulty"));
        }

        [Test]
        public void Schema3_ClearsGeneratedCacheWithOldBoundary()
        {
            SeedSchema2(extraUnlocked: 1, ultraUnlocked: 1);
            string cache = Path.Combine(Application.persistentDataPath, "generated-levels");
            Directory.CreateDirectory(cache);
            File.WriteAllText(Path.Combine(cache, "stale.txt"), "stale");

            SaveService.EnsureMigrated();

            Assert.IsFalse(Directory.Exists(cache));
        }

        [Test]
        public void CameraShake_DefaultsOff_AndPersistsSelection()
        {
            Assert.IsFalse(SaveService.CameraShakeEnabled);

            SaveService.CameraShakeEnabled = true;

            Assert.IsTrue(SaveService.CameraShakeEnabled);
            Assert.AreEqual(1, PlayerPrefs.GetInt("pm_camera_shake"));
        }

        [Test]
        public void PaintSplashes_DefaultOn_AndPersistSelection()
        {
            Assert.IsTrue(SaveService.PaintSplashesEnabled);

            SaveService.PaintSplashesEnabled = false;

            Assert.IsFalse(SaveService.PaintSplashesEnabled);
            Assert.AreEqual(0, PlayerPrefs.GetInt("pm_paint_splashes"));
        }

        [Test]
        public void Progress_IsScopedToFirebaseUser()
        {
            PlayerPrefs.SetInt("pm_save_schema", SaveService.CurrentSchemaVersion);
            PlayerPrefs.SetString("pm_progress_claimed_by", "legacy-owner");

            SaveService.SetProgressOwner("user-a");
            SaveService.MarkCompleted(Difficulty.Easy, 9);

            SaveService.SetProgressOwner("user-b");
            Assert.AreEqual(1, SaveService.HighestUnlocked(Difficulty.Easy));
            SaveService.MarkCompleted(Difficulty.Easy, 2);

            SaveService.SetProgressOwner("user-a");
            Assert.AreEqual(10, SaveService.HighestUnlocked(Difficulty.Easy));
            SaveService.SetProgressOwner("user-b");
            Assert.AreEqual(3, SaveService.HighestUnlocked(Difficulty.Easy));
        }

        [Test]
        public void FirstFirebaseUser_ClaimsLegacyProgress()
        {
            PlayerPrefs.SetInt("pm_save_schema", SaveService.CurrentSchemaVersion);
            PlayerPrefs.SetInt("pm_unlocked_0", 18);

            SaveService.SetProgressOwner("first-user");
            Assert.AreEqual(18, SaveService.HighestUnlocked(Difficulty.Easy));

            SaveService.SetProgressOwner("second-user");
            Assert.AreEqual(1, SaveService.HighestUnlocked(Difficulty.Easy));
        }

        [Test]
        public void ProgressOwner_IsPersistedForHomeFirstStartup()
        {
            PlayerPrefs.SetInt("pm_save_schema", SaveService.CurrentSchemaVersion);

            SaveService.SetProgressOwner("returning-user");

            Assert.AreEqual("returning-user",
                PlayerPrefs.GetString("pm_last_progress_owner"));
        }

        [Test]
        public void ProgressSnapshot_MergesMaximumPerDifficulty()
        {
            var local = new ProgressSnapshot();
            local.SetHighestUnlocked(Difficulty.Easy, 12);
            local.SetHighestUnlocked(Difficulty.Hard, 4);
            var cloud = new ProgressSnapshot();
            cloud.SetHighestUnlocked(Difficulty.Easy, 7);
            cloud.SetHighestUnlocked(Difficulty.Hard, 21);

            ProgressSnapshot merged = ProgressSnapshot.MergeMax(local, cloud);

            Assert.AreEqual(12, merged.HighestUnlocked(Difficulty.Easy));
            Assert.AreEqual(21, merged.HighestUnlocked(Difficulty.Hard));
            Assert.AreEqual(1, merged.HighestUnlocked(Difficulty.Medium));
        }

        [Test]
        public void ReturningAccountMerge_PreservesGuestRegisteredAndCloudBest()
        {
            var guest = new ProgressSnapshot();
            guest.SetHighestUnlocked(Difficulty.Easy, 15);
            guest.SetHighestUnlocked(Difficulty.Medium, 3);
            var registered = new ProgressSnapshot();
            registered.SetHighestUnlocked(Difficulty.Easy, 9);
            registered.SetHighestUnlocked(Difficulty.Hard, 18);
            var cloud = new ProgressSnapshot();
            cloud.SetHighestUnlocked(Difficulty.Medium, 22);
            cloud.SetHighestUnlocked(Difficulty.ExtraHard, 6);

            ProgressSnapshot merged =
                ProgressSnapshot.MergeMax(registered, guest, cloud);

            Assert.AreEqual(15, merged.HighestUnlocked(Difficulty.Easy));
            Assert.AreEqual(22, merged.HighestUnlocked(Difficulty.Medium));
            Assert.AreEqual(18, merged.HighestUnlocked(Difficulty.Hard));
            Assert.AreEqual(6, merged.HighestUnlocked(Difficulty.ExtraHard));
        }

        [Test]
        public void ClearProgressForUser_PreservesOtherUsersAndDevicePreferences()
        {
            PlayerPrefs.SetInt("pm_save_schema", SaveService.CurrentSchemaVersion);
            PlayerPrefs.SetInt("pm_unlocked_0", 7);
            SaveService.ThemeMode = ThemeMode.Light;
            SaveService.SoundEnabled = false;

            SaveService.SetProgressOwner("delete-me");
            SaveService.MarkCompleted(Difficulty.Easy, 19);
            SaveService.SetProgressOwner("keep-me");
            SaveService.MarkCompleted(Difficulty.Hard, 11);

            SaveService.ClearProgressForUser("delete-me");

            Assert.AreEqual(1, SaveService.CaptureProgressForOwner("delete-me")
                .HighestUnlocked(Difficulty.Easy));
            Assert.IsFalse(PlayerPrefs.HasKey("pm_unlocked_0"));
            Assert.AreEqual(12, SaveService.CaptureProgressForOwner("keep-me")
                .HighestUnlocked(Difficulty.Hard));
            Assert.AreEqual(ThemeMode.Light, SaveService.ThemeMode);
            Assert.IsFalse(SaveService.SoundEnabled);
        }

        private static void SeedLegacyProgress(int unlockedLevel)
        {
            PlayerPrefs.SetInt("pm_save_schema", 0);
            foreach (Difficulty d in EditModeTestSupport.AllDifficulties)
                PlayerPrefs.SetInt("pm_unlocked_" + (int)d, unlockedLevel);
            string cache = Path.Combine(Application.persistentDataPath, "generated-levels");
            if (Directory.Exists(cache))
                Directory.Delete(cache, true);
            PlayerPrefs.Save();
        }

        private static void SeedSchema2(int extraUnlocked, int ultraUnlocked)
        {
            PlayerPrefs.SetInt("pm_save_schema", 2);
            PlayerPrefs.SetInt("pm_unlocked_3", extraUnlocked);
            PlayerPrefs.SetInt("pm_unlocked_4", ultraUnlocked);
            PlayerPrefs.Save();
        }
    }
}
