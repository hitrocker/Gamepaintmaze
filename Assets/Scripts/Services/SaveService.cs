using System.IO;
using UnityEngine;
using PaintMaze.Domain;

namespace PaintMaze.Services
{
    public sealed class ProgressSnapshot
    {
        private readonly System.Collections.Generic.Dictionary<Difficulty, int> _values =
            new();

        public int HighestUnlocked(Difficulty difficulty)
        {
            difficulty = DifficultyCatalog.NormalizePlayable(difficulty);
            return _values.TryGetValue(difficulty, out int value)
                ? Mathf.Clamp(value, 1, DifficultyConfig.PracticalMaxLevel)
                : 1;
        }

        public void SetHighestUnlocked(Difficulty difficulty, int value)
        {
            difficulty = DifficultyCatalog.NormalizePlayable(difficulty);
            _values[difficulty] = Mathf.Clamp(
                value, 1, DifficultyConfig.PracticalMaxLevel);
        }

        public static ProgressSnapshot MergeMax(params ProgressSnapshot[] snapshots)
        {
            var merged = new ProgressSnapshot();
            foreach (Difficulty difficulty in DifficultyCatalog.Playable)
            {
                int best = 1;
                if (snapshots != null)
                    foreach (ProgressSnapshot snapshot in snapshots)
                        if (snapshot != null)
                            best = Mathf.Max(best, snapshot.HighestUnlocked(difficulty));
                merged.SetHighestUnlocked(difficulty, best);
            }
            return merged;
        }
    }

    /// <summary>
    /// Persists player progress via PlayerPrefs: selected mode, highest unlocked
    /// level per mode, and presentation toggles. Level 1 is always unlocked.
    /// </summary>
    public static class SaveService
    {
        private const string KeyDifficulty = "pm_difficulty";
        private const string KeySound = "pm_sound";
        private const string KeyHaptics = "pm_haptics";
        private const string KeyCameraShake = "pm_camera_shake";
        private const string KeyPaintSplashes = "pm_paint_splashes";
        private const string KeyTheme = "pm_theme";
        private const string KeySchema = "pm_save_schema";
        private const string KeyProgressClaimedBy = "pm_progress_claimed_by";
        private const string KeyLastProgressOwner = "pm_last_progress_owner";
        public const int CurrentSchemaVersion = 4;

        private static string _progressOwner;
        public static string CurrentProgressOwner => _progressOwner;
        private static string LegacyKeyUnlocked(Difficulty d) => "pm_unlocked_" + (int)d;
        private static string KeyUnlocked(Difficulty d) => KeyUnlockedFor(_progressOwner, d);
        private static string KeyUnlockedFor(string owner, Difficulty d) =>
            string.IsNullOrEmpty(owner)
                ? LegacyKeyUnlocked(d)
                : "pm_user_" + owner + "_unlocked_" + (int)d;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void EnsureMigrated()
        {
            int schema = PlayerPrefs.GetInt(KeySchema, 0);
            if (schema >= CurrentSchemaVersion) return;

            if (schema < 2)
            {
                // The unlimited generator replaced every old board in schema 2, so level
                // progression reset once. Player preferences intentionally survived.
                foreach (Difficulty d in System.Enum.GetValues(typeof(Difficulty)))
                    PlayerPrefs.DeleteKey(KeyUnlocked(d));
            }

            if (schema < 3)
            {
                string ultraKey = KeyUnlocked(Difficulty.UltraHard);
                if (PlayerPrefs.HasKey(ultraKey))
                {
                    int extraProgress = PlayerPrefs.GetInt(KeyUnlocked(Difficulty.ExtraHard), 1);
                    int ultraProgress = PlayerPrefs.GetInt(ultraKey, 1);
                    int mergedProgress = Mathf.Clamp(
                        Mathf.Max(extraProgress, DifficultyCatalog.StandardBakedCount + ultraProgress),
                        1, DifficultyConfig.PracticalMaxLevel);
                    PlayerPrefs.SetInt(KeyUnlocked(Difficulty.ExtraHard), mergedProgress);
                }

                PlayerPrefs.DeleteKey(ultraKey);
                if (PlayerPrefs.GetInt(KeyDifficulty, 0) == (int)Difficulty.UltraHard)
                    PlayerPrefs.SetInt(KeyDifficulty, (int)Difficulty.ExtraHard);
            }

            ClearGeneratedCache();
            PlayerPrefs.SetInt(KeySchema, CurrentSchemaVersion);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Selects the Firebase user whose progression is currently active. The
        /// first authenticated user claims any progression from pre-account builds.
        /// Presentation preferences remain shared by the device.
        /// </summary>
        public static void SetProgressOwner(string userId)
        {
            EnsureMigrated();
            _progressOwner = string.IsNullOrWhiteSpace(userId) ? null : userId.Trim();
            if (string.IsNullOrEmpty(_progressOwner))
            {
                PlayerPrefs.DeleteKey(KeyLastProgressOwner);
                PlayerPrefs.Save();
                return;
            }

            PlayerPrefs.SetString(KeyLastProgressOwner, _progressOwner);
            if (PlayerPrefs.HasKey(KeyProgressClaimedBy))
            {
                PlayerPrefs.Save();
                return;
            }

            foreach (Difficulty d in DifficultyCatalog.Playable)
            {
                int legacy = PlayerPrefs.GetInt(LegacyKeyUnlocked(d), 1);
                if (legacy > 1) PlayerPrefs.SetInt(KeyUnlocked(d), legacy);
            }

            PlayerPrefs.SetString(KeyProgressClaimedBy, _progressOwner);
            PlayerPrefs.Save();
        }

        public static void RestoreLastProgressOwner()
        {
            EnsureMigrated();
            string stored = PlayerPrefs.GetString(KeyLastProgressOwner, string.Empty);
            _progressOwner = string.IsNullOrWhiteSpace(stored) ? null : stored.Trim();
        }

        private static void ClearGeneratedCache()
        {
            string cache = Path.Combine(Application.persistentDataPath, "generated-levels");
            try
            {
                if (Directory.Exists(cache)) Directory.Delete(cache, true);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[Save] Could not clear generated-level cache: " + ex.Message);
            }
        }

        public static Difficulty SelectedDifficulty
        {
            get
            {
                EnsureMigrated();
                var stored = (Difficulty)Mathf.Clamp(PlayerPrefs.GetInt(KeyDifficulty, 0), 0, 4);
                return DifficultyCatalog.NormalizePlayable(stored);
            }
            set
            {
                EnsureMigrated();
                PlayerPrefs.SetInt(KeyDifficulty, (int)DifficultyCatalog.NormalizePlayable(value));
                PlayerPrefs.Save();
            }
        }

        /// <summary>Visual theme: 0 = Dark, 1 = Light. Defaults to Dark.</summary>
        public static ThemeMode ThemeMode
        {
            get => (ThemeMode)Mathf.Clamp(PlayerPrefs.GetInt(KeyTheme, 0), 0, 1);
            set { PlayerPrefs.SetInt(KeyTheme, (int)value); PlayerPrefs.Save(); }
        }

        public static bool SoundEnabled
        {
            get => PlayerPrefs.GetInt(KeySound, 1) == 1;
            set { PlayerPrefs.SetInt(KeySound, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static bool HapticsEnabled
        {
            get => PlayerPrefs.GetInt(KeyHaptics, 1) == 1;
            set { PlayerPrefs.SetInt(KeyHaptics, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>Camera movement on wall impacts. Defaults off.</summary>
        public static bool CameraShakeEnabled
        {
            get => PlayerPrefs.GetInt(KeyCameraShake, 0) == 1;
            set { PlayerPrefs.SetInt(KeyCameraShake, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>Paint droplets emitted while crossing tiles. Defaults on.</summary>
        public static bool PaintSplashesEnabled
        {
            get => PlayerPrefs.GetInt(KeyPaintSplashes, 1) == 1;
            set { PlayerPrefs.SetInt(KeyPaintSplashes, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static int HighestUnlocked(Difficulty d)
        {
            EnsureMigrated();
            d = DifficultyCatalog.NormalizePlayable(d);
            return Mathf.Clamp(PlayerPrefs.GetInt(KeyUnlocked(d), 1),
                1, DifficultyConfig.PracticalMaxLevel);
        }

        /// <summary>Records completion of a level, unlocking the next one.</summary>
        public static void MarkCompleted(Difficulty d, int levelIndex)
        {
            EnsureMigrated();
            d = DifficultyCatalog.NormalizePlayable(d);
            int next = levelIndex < DifficultyConfig.PracticalMaxLevel
                ? levelIndex + 1
                : DifficultyConfig.PracticalMaxLevel;
            if (next > HighestUnlocked(d))
            {
                PlayerPrefs.SetInt(KeyUnlocked(d), next);
                PlayerPrefs.Save();
            }
        }

        public static ProgressSnapshot CaptureProgress() =>
            CaptureProgressForOwner(_progressOwner);

        public static ProgressSnapshot CaptureProgressForOwner(string userId)
        {
            EnsureMigrated();
            string owner = string.IsNullOrWhiteSpace(userId) ? null : userId.Trim();
            var snapshot = new ProgressSnapshot();
            foreach (Difficulty difficulty in DifficultyCatalog.Playable)
            {
                int unlocked = PlayerPrefs.GetInt(
                    KeyUnlockedFor(owner, difficulty), 1);
                snapshot.SetHighestUnlocked(difficulty, unlocked);
            }
            return snapshot;
        }

        public static void ApplyProgress(string userId, ProgressSnapshot snapshot,
            bool mergeWithExisting = true)
        {
            if (snapshot == null) return;
            EnsureMigrated();
            string owner = string.IsNullOrWhiteSpace(userId) ? null : userId.Trim();
            foreach (Difficulty difficulty in DifficultyCatalog.Playable)
            {
                int incoming = snapshot.HighestUnlocked(difficulty);
                string key = KeyUnlockedFor(owner, difficulty);
                int value = mergeWithExisting
                    ? Mathf.Max(PlayerPrefs.GetInt(key, 1), incoming)
                    : incoming;
                PlayerPrefs.SetInt(key, Mathf.Clamp(
                    value, 1, DifficultyConfig.PracticalMaxLevel));
            }
            PlayerPrefs.Save();
        }

        public static void ClearProgressForUser(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId)) return;
            EnsureMigrated();
            string owner = userId.Trim();
            foreach (Difficulty difficulty in DifficultyCatalog.Playable)
                PlayerPrefs.DeleteKey(KeyUnlockedFor(owner, difficulty));
            PlayerPrefs.DeleteKey(KeyUnlockedFor(owner, Difficulty.UltraHard));
            if (PlayerPrefs.GetString(KeyProgressClaimedBy, string.Empty) == owner)
            {
                PlayerPrefs.DeleteKey(KeyProgressClaimedBy);
                foreach (Difficulty difficulty in DifficultyCatalog.Playable)
                    PlayerPrefs.DeleteKey(LegacyKeyUnlocked(difficulty));
                PlayerPrefs.DeleteKey(LegacyKeyUnlocked(Difficulty.UltraHard));
            }
            if (PlayerPrefs.GetString(KeyLastProgressOwner, string.Empty) == owner)
                PlayerPrefs.DeleteKey(KeyLastProgressOwner);
            if (_progressOwner == owner) _progressOwner = null;
            PlayerPrefs.Save();
        }

        public static void ResetAll()
        {
            foreach (Difficulty d in DifficultyCatalog.Playable)
            {
                PlayerPrefs.DeleteKey(KeyUnlocked(d));
                PlayerPrefs.DeleteKey(LegacyKeyUnlocked(d));
            }
            PlayerPrefs.DeleteKey(KeyUnlocked(Difficulty.UltraHard));
            PlayerPrefs.DeleteKey(LegacyKeyUnlocked(Difficulty.UltraHard));
            PlayerPrefs.DeleteKey(KeyProgressClaimedBy);
            PlayerPrefs.DeleteKey(KeyLastProgressOwner);
            PlayerPrefs.DeleteKey(KeyDifficulty);
            PlayerPrefs.Save();
        }
    }
}
