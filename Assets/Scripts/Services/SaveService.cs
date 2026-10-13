using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using PaintMaze.Domain;

namespace PaintMaze.Services
{
    public sealed class ProgressSnapshot
    {
        private readonly Dictionary<Difficulty, int> _values = new();

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

    public sealed class EconomySnapshot
    {
        public int Coins { get; set; }
        public IReadOnlyList<string> OwnedSkinIds { get; set; } =
            Array.Empty<string>();
        public string EquippedSkinId { get; set; } = SaveService.DefaultSkinId;
        public string DailyHintDayId { get; set; } = string.Empty;
        public int DailyHintsUsed { get; set; }

        public static EconomySnapshot MergeMax(params EconomySnapshot[] snapshots)
        {
            var owned = new HashSet<string>(StringComparer.Ordinal)
            {
                SaveService.DefaultSkinId
            };
            int coins = 0;
            string equipped = SaveService.DefaultSkinId;
            string hintDayId = string.Empty;
            int hintsUsed = 0;
            if (snapshots != null)
            {
                foreach (EconomySnapshot snapshot in snapshots)
                {
                    if (snapshot == null) continue;
                    coins = Mathf.Max(coins, Mathf.Max(0, snapshot.Coins));
                    if (snapshot.OwnedSkinIds != null)
                    {
                        foreach (string skinId in snapshot.OwnedSkinIds)
                        {
                            if (!string.IsNullOrWhiteSpace(skinId))
                                owned.Add(skinId.Trim());
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(snapshot.EquippedSkinId) &&
                        owned.Contains(snapshot.EquippedSkinId.Trim()))
                        equipped = snapshot.EquippedSkinId.Trim();

                    string incomingDay = SaveService.NormalizeDayId(
                        snapshot.DailyHintDayId);
                    int incomingUsed = Mathf.Clamp(snapshot.DailyHintsUsed, 0, 3);
                    int dayComparison = string.CompareOrdinal(
                        incomingDay,
                        hintDayId);
                    if (dayComparison > 0)
                    {
                        hintDayId = incomingDay;
                        hintsUsed = incomingUsed;
                    }
                    else if (dayComparison == 0 &&
                             !string.IsNullOrEmpty(incomingDay))
                    {
                        hintsUsed = Mathf.Max(hintsUsed, incomingUsed);
                    }
                }
            }

            if (!owned.Contains(equipped))
                equipped = SaveService.DefaultSkinId;
            return new EconomySnapshot
            {
                Coins = coins,
                OwnedSkinIds = owned.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                EquippedSkinId = equipped,
                DailyHintDayId = hintDayId,
                DailyHintsUsed = hintsUsed
            };
        }
    }

    /// <summary>
    /// Persists player progress via PlayerPrefs: selected mode, highest unlocked
    /// level per mode, and presentation toggles. Level 1 is always unlocked.
    /// </summary>
    public static class SaveService
    {
        public const string DefaultSkinId = "classic";
        public const int MaxCoins = 100_000_000;
        public const int MaxOwnedSkins = 64;

        private const string KeyDifficulty = "pm_difficulty";
        private const string KeySound = "pm_sound";
        private const string KeyHaptics = "pm_haptics";
        private const string KeyPaintSplashes = "pm_paint_splashes";
        private const string KeyTheme = "pm_theme";
        private const string KeySchema = "pm_save_schema";
        private const string KeyProgressClaimedBy = "pm_progress_claimed_by";
        private const string KeyLastProgressOwner = "pm_last_progress_owner";
        private const string LegacyKeyCoins = "pm_coins";
        private const string LegacyKeyOwnedSkins = "pm_owned_skins";
        private const string LegacyKeyEquippedSkin = "pm_equipped_skin";
        private const string LegacyKeyDailyHintDay = "pm_daily_hint_day";
        private const string LegacyKeyDailyHintsUsed = "pm_daily_hints_used";
        public const int CurrentSchemaVersion = 4;

        private static string _progressOwner;
        public static string CurrentProgressOwner => _progressOwner;
        private static string LegacyKeyUnlocked(Difficulty d) => "pm_unlocked_" + (int)d;
        private static string KeyUnlocked(Difficulty d) => KeyUnlockedFor(_progressOwner, d);
        private static string KeyUnlockedFor(string owner, Difficulty d) =>
            string.IsNullOrEmpty(owner)
                ? LegacyKeyUnlocked(d)
                : "pm_user_" + owner + "_unlocked_" + (int)d;

        private static string KeyCoins() => KeyCoinsFor(_progressOwner);
        private static string KeyCoinsFor(string owner) =>
            string.IsNullOrEmpty(owner) ? LegacyKeyCoins : "pm_user_" + owner + "_coins";
        private static string KeyOwnedSkins() => KeyOwnedSkinsFor(_progressOwner);
        private static string KeyOwnedSkinsFor(string owner) =>
            string.IsNullOrEmpty(owner)
                ? LegacyKeyOwnedSkins
                : "pm_user_" + owner + "_owned_skins";
        private static string KeyEquippedSkin() => KeyEquippedSkinFor(_progressOwner);
        private static string KeyEquippedSkinFor(string owner) =>
            string.IsNullOrEmpty(owner)
                ? LegacyKeyEquippedSkin
                : "pm_user_" + owner + "_equipped_skin";
        private static string KeyDailyHintDay() =>
            KeyDailyHintDayFor(_progressOwner);
        private static string KeyDailyHintDayFor(string owner) =>
            string.IsNullOrEmpty(owner)
                ? LegacyKeyDailyHintDay
                : "pm_user_" + owner + "_daily_hint_day";
        private static string KeyDailyHintsUsed() =>
            KeyDailyHintsUsedFor(_progressOwner);
        private static string KeyDailyHintsUsedFor(string owner) =>
            string.IsNullOrEmpty(owner)
                ? LegacyKeyDailyHintsUsed
                : "pm_user_" + owner + "_daily_hints_used";
        private static string KeyDailyBaseGranted(string dayId) =>
            KeyDailyBaseGrantedFor(_progressOwner, dayId);
        private static string KeyDailyBaseGrantedFor(string owner, string dayId) =>
            string.IsNullOrEmpty(owner)
                ? "pm_coins_daily_base_" + dayId
                : "pm_user_" + owner + "_coins_daily_base_" + dayId;
        private static string KeyDailyPodiumGranted(string dayId) =>
            KeyDailyPodiumGrantedFor(_progressOwner, dayId);
        private static string KeyDailyPodiumGrantedFor(string owner, string dayId) =>
            string.IsNullOrEmpty(owner)
                ? "pm_coins_daily_podium_" + dayId
                : "pm_user_" + owner + "_coins_daily_podium_" + dayId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void EnsureMigrated()
        {
            int schema = PlayerPrefs.GetInt(KeySchema, 0);
            if (schema >= CurrentSchemaVersion) return;

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

            ClaimLegacyEconomyForCurrentOwner();
            PlayerPrefs.SetString(KeyProgressClaimedBy, _progressOwner);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Claims device-legacy progress for <paramref name="userId"/> first, then
        /// merge-maxes any carried guest snapshots onto that owner. Economy is
        /// replaced only after the claim so a zero snapshot cannot wipe coins.
        /// </summary>
        public static void AdoptOwner(
            string userId,
            ProgressSnapshot progressCarry = null,
            EconomySnapshot economyCarry = null)
        {
            if (string.IsNullOrWhiteSpace(userId)) return;
            string uid = userId.Trim();
            SetProgressOwner(uid);
            ProgressSnapshot progress = ProgressSnapshot.MergeMax(
                CaptureProgressForOwner(uid), progressCarry);
            EconomySnapshot economy = EconomySnapshot.MergeMax(
                CaptureEconomyForOwner(uid), economyCarry);
            ApplyProgress(uid, progress);
            ApplyEconomy(uid, economy, mergeWithExisting: false);
        }

        private static void ClaimLegacyEconomyForCurrentOwner()
        {
            int legacyCoins = Mathf.Clamp(
                PlayerPrefs.GetInt(LegacyKeyCoins, 0), 0, MaxCoins);
            if (legacyCoins > Coins)
                PlayerPrefs.SetInt(KeyCoins(), legacyCoins);

            string legacyOwned = PlayerPrefs.GetString(LegacyKeyOwnedSkins, string.Empty);
            if (!string.IsNullOrWhiteSpace(legacyOwned))
            {
                var merged = new HashSet<string>(OwnedSkinIds, StringComparer.Ordinal);
                foreach (string skinId in ParseSkinIds(legacyOwned))
                    merged.Add(skinId);
                WriteOwnedSkinIds(merged);
            }

            string legacyEquipped = PlayerPrefs.GetString(
                LegacyKeyEquippedSkin, string.Empty);
            if (!string.IsNullOrWhiteSpace(legacyEquipped) &&
                OwnsSkin(legacyEquipped.Trim()))
                PlayerPrefs.SetString(KeyEquippedSkin(), legacyEquipped.Trim());

            string legacyHintDay = NormalizeDayId(
                PlayerPrefs.GetString(LegacyKeyDailyHintDay, string.Empty));
            int legacyHintsUsed = Mathf.Clamp(
                PlayerPrefs.GetInt(LegacyKeyDailyHintsUsed, 0),
                0,
                3);
            EconomySnapshot mergedHints = EconomySnapshot.MergeMax(
                new()
                {
                    DailyHintDayId = DailyHintDayId,
                    DailyHintsUsed = DailyHintsUsed
                },
                new()
                {
                    DailyHintDayId = legacyHintDay,
                    DailyHintsUsed = legacyHintsUsed
                });
            if (!string.IsNullOrEmpty(mergedHints.DailyHintDayId))
            {
                PlayerPrefs.SetString(
                    KeyDailyHintDay(),
                    mergedHints.DailyHintDayId);
                PlayerPrefs.SetInt(
                    KeyDailyHintsUsed(),
                    mergedHints.DailyHintsUsed);
            }
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

        public static int Coins
        {
            get
            {
                EnsureMigrated();
                return Mathf.Clamp(PlayerPrefs.GetInt(KeyCoins(), 0), 0, MaxCoins);
            }
            set
            {
                EnsureMigrated();
                PlayerPrefs.SetInt(KeyCoins(), Mathf.Clamp(value, 0, MaxCoins));
                PlayerPrefs.Save();
            }
        }

        public static int AddCoins(int amount)
        {
            if (amount <= 0) return Coins;
            long next = (long)Coins + amount;
            Coins = next > MaxCoins ? MaxCoins : (int)next;
            return Coins;
        }

        public static bool TrySpendCoins(int amount)
        {
            if (amount <= 0) return true;
            int balance = Coins;
            if (balance < amount) return false;
            Coins = balance - amount;
            return true;
        }

        public static IReadOnlyList<string> OwnedSkinIds
        {
            get
            {
                EnsureMigrated();
                string raw = PlayerPrefs.GetString(KeyOwnedSkins(), string.Empty);
                var owned = new HashSet<string>(ParseSkinIds(raw), StringComparer.Ordinal)
                {
                    DefaultSkinId
                };
                return owned.OrderBy(id => id, StringComparer.Ordinal).ToArray();
            }
        }

        public static string EquippedSkinId
        {
            get
            {
                EnsureMigrated();
                string equipped = PlayerPrefs.GetString(KeyEquippedSkin(), DefaultSkinId);
                if (string.IsNullOrWhiteSpace(equipped) || !OwnsSkin(equipped))
                    return DefaultSkinId;
                return equipped.Trim();
            }
            set
            {
                EnsureMigrated();
                string skinId = string.IsNullOrWhiteSpace(value)
                    ? DefaultSkinId
                    : value.Trim();
                if (!OwnsSkin(skinId)) skinId = DefaultSkinId;
                PlayerPrefs.SetString(KeyEquippedSkin(), skinId);
                PlayerPrefs.Save();
            }
        }

        public static bool OwnsSkin(string skinId)
        {
            if (string.IsNullOrWhiteSpace(skinId)) return false;
            string id = skinId.Trim();
            if (id == DefaultSkinId) return true;
            foreach (string owned in OwnedSkinIds)
                if (owned == id) return true;
            return false;
        }

        public static void GrantSkin(string skinId)
        {
            if (string.IsNullOrWhiteSpace(skinId)) return;
            EnsureMigrated();
            var owned = new HashSet<string>(OwnedSkinIds, StringComparer.Ordinal)
            {
                skinId.Trim()
            };
            WriteOwnedSkinIds(owned);
        }

        public static bool WasDailyBaseGranted(string dayId) =>
            !string.IsNullOrEmpty(dayId) &&
            PlayerPrefs.GetInt(KeyDailyBaseGranted(dayId), 0) != 0;

        public static void MarkDailyBaseGranted(string dayId)
        {
            if (string.IsNullOrEmpty(dayId)) return;
            EnsureMigrated();
            PlayerPrefs.SetInt(KeyDailyBaseGranted(dayId), 1);
            PlayerPrefs.Save();
        }

        public static bool WasDailyPodiumGranted(string dayId) =>
            !string.IsNullOrEmpty(dayId) &&
            PlayerPrefs.GetInt(KeyDailyPodiumGranted(dayId), 0) != 0;

        public static void MarkDailyPodiumGranted(string dayId)
        {
            if (string.IsNullOrEmpty(dayId)) return;
            EnsureMigrated();
            PlayerPrefs.SetInt(KeyDailyPodiumGranted(dayId), 1);
            PlayerPrefs.Save();
        }

        public static string DailyHintDayId
        {
            get
            {
                EnsureMigrated();
                return NormalizeDayId(
                    PlayerPrefs.GetString(KeyDailyHintDay(), string.Empty));
            }
        }

        public static int DailyHintsUsed
        {
            get
            {
                EnsureMigrated();
                return Mathf.Clamp(
                    PlayerPrefs.GetInt(KeyDailyHintsUsed(), 0),
                    0,
                    3);
            }
        }

        public static int GetDailyHintsUsed(string dayId) =>
            NormalizeDayId(dayId) == DailyHintDayId
                ? DailyHintsUsed
                : 0;

        public static void SetDailyHintUsage(string dayId, int used)
        {
            string normalized = NormalizeDayId(dayId);
            if (string.IsNullOrEmpty(normalized)) return;
            EnsureMigrated();
            PlayerPrefs.SetString(KeyDailyHintDay(), normalized);
            PlayerPrefs.SetInt(KeyDailyHintsUsed(), Mathf.Clamp(used, 0, 3));
            PlayerPrefs.Save();
        }

        public static EconomySnapshot CaptureEconomy() =>
            CaptureEconomyForOwner(_progressOwner);

        public static EconomySnapshot CaptureEconomyForOwner(string userId)
        {
            EnsureMigrated();
            string owner = string.IsNullOrWhiteSpace(userId) ? null : userId.Trim();
            string previous = _progressOwner;
            _progressOwner = owner;
            var snapshot = new EconomySnapshot
            {
                Coins = Coins,
                OwnedSkinIds = OwnedSkinIds,
                EquippedSkinId = EquippedSkinId,
                DailyHintDayId = DailyHintDayId,
                DailyHintsUsed = DailyHintsUsed
            };
            _progressOwner = previous;
            return snapshot;
        }

        public static void ApplyEconomy(string userId, EconomySnapshot snapshot,
            bool mergeWithExisting = true)
        {
            if (snapshot == null) return;
            EnsureMigrated();
            string owner = string.IsNullOrWhiteSpace(userId) ? null : userId.Trim();
            string previous = _progressOwner;
            _progressOwner = owner;
            EconomySnapshot applied = mergeWithExisting
                ? EconomySnapshot.MergeMax(CaptureEconomy(), snapshot)
                : NormalizeEconomy(snapshot);
            Coins = applied.Coins;
            WriteOwnedSkinIds(applied.OwnedSkinIds);
            EquippedSkinId = applied.EquippedSkinId;
            if (string.IsNullOrEmpty(applied.DailyHintDayId))
            {
                PlayerPrefs.DeleteKey(KeyDailyHintDay());
                PlayerPrefs.DeleteKey(KeyDailyHintsUsed());
            }
            else
            {
                PlayerPrefs.SetString(
                    KeyDailyHintDay(),
                    applied.DailyHintDayId);
                PlayerPrefs.SetInt(
                    KeyDailyHintsUsed(),
                    applied.DailyHintsUsed);
            }
            PlayerPrefs.Save();
            _progressOwner = previous;
        }

        public static void ClearProgressForUser(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId)) return;
            EnsureMigrated();
            string owner = userId.Trim();
            foreach (Difficulty difficulty in DifficultyCatalog.Playable)
                PlayerPrefs.DeleteKey(KeyUnlockedFor(owner, difficulty));
            PlayerPrefs.DeleteKey(KeyUnlockedFor(owner, Difficulty.UltraHard));
            PlayerPrefs.DeleteKey(KeyCoinsFor(owner));
            PlayerPrefs.DeleteKey(KeyOwnedSkinsFor(owner));
            PlayerPrefs.DeleteKey(KeyEquippedSkinFor(owner));
            PlayerPrefs.DeleteKey(KeyDailyHintDayFor(owner));
            PlayerPrefs.DeleteKey(KeyDailyHintsUsedFor(owner));
            if (PlayerPrefs.GetString(KeyProgressClaimedBy, string.Empty) == owner)
            {
                PlayerPrefs.DeleteKey(KeyProgressClaimedBy);
                foreach (Difficulty difficulty in DifficultyCatalog.Playable)
                    PlayerPrefs.DeleteKey(LegacyKeyUnlocked(difficulty));
                PlayerPrefs.DeleteKey(LegacyKeyUnlocked(Difficulty.UltraHard));
                PlayerPrefs.DeleteKey(LegacyKeyCoins);
                PlayerPrefs.DeleteKey(LegacyKeyOwnedSkins);
                PlayerPrefs.DeleteKey(LegacyKeyEquippedSkin);
                PlayerPrefs.DeleteKey(LegacyKeyDailyHintDay);
                PlayerPrefs.DeleteKey(LegacyKeyDailyHintsUsed);
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
            PlayerPrefs.DeleteKey(KeyCoins());
            PlayerPrefs.DeleteKey(KeyOwnedSkins());
            PlayerPrefs.DeleteKey(KeyEquippedSkin());
            PlayerPrefs.DeleteKey(KeyDailyHintDay());
            PlayerPrefs.DeleteKey(KeyDailyHintsUsed());
            PlayerPrefs.DeleteKey(LegacyKeyCoins);
            PlayerPrefs.DeleteKey(LegacyKeyOwnedSkins);
            PlayerPrefs.DeleteKey(LegacyKeyEquippedSkin);
            PlayerPrefs.DeleteKey(LegacyKeyDailyHintDay);
            PlayerPrefs.DeleteKey(LegacyKeyDailyHintsUsed);
            PlayerPrefs.DeleteKey(KeyProgressClaimedBy);
            PlayerPrefs.DeleteKey(KeyLastProgressOwner);
            PlayerPrefs.DeleteKey(KeyDifficulty);
            PlayerPrefs.Save();
        }

        internal static string NormalizeDayId(string dayId)
        {
            if (string.IsNullOrWhiteSpace(dayId)) return string.Empty;
            string trimmed = dayId.Trim();
            return DateTime.TryParseExact(
                trimmed,
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _)
                ? trimmed
                : string.Empty;
        }

        private static EconomySnapshot NormalizeEconomy(EconomySnapshot snapshot)
        {
            var owned = new HashSet<string>(StringComparer.Ordinal) { DefaultSkinId };
            if (snapshot.OwnedSkinIds != null)
            {
                foreach (string skinId in snapshot.OwnedSkinIds)
                {
                    if (!string.IsNullOrWhiteSpace(skinId))
                        owned.Add(skinId.Trim());
                }
            }

            while (owned.Count > MaxOwnedSkins)
            {
                string remove = owned
                    .Where(id => id != DefaultSkinId)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (remove == null) break;
                owned.Remove(remove);
            }

            string equipped = string.IsNullOrWhiteSpace(snapshot.EquippedSkinId)
                ? DefaultSkinId
                : snapshot.EquippedSkinId.Trim();
            if (!owned.Contains(equipped))
                equipped = DefaultSkinId;
            string hintDayId = NormalizeDayId(snapshot.DailyHintDayId);
            return new EconomySnapshot
            {
                Coins = Mathf.Clamp(snapshot.Coins, 0, MaxCoins),
                OwnedSkinIds = owned.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                EquippedSkinId = equipped,
                DailyHintDayId = hintDayId,
                DailyHintsUsed = string.IsNullOrEmpty(hintDayId)
                    ? 0
                    : Mathf.Clamp(snapshot.DailyHintsUsed, 0, 3)
            };
        }

        private static void WriteOwnedSkinIds(IEnumerable<string> skinIds)
        {
            var owned = new HashSet<string>(StringComparer.Ordinal) { DefaultSkinId };
            if (skinIds != null)
            {
                foreach (string skinId in skinIds)
                {
                    if (!string.IsNullOrWhiteSpace(skinId))
                        owned.Add(skinId.Trim());
                }
            }

            while (owned.Count > MaxOwnedSkins)
            {
                string remove = owned
                    .Where(id => id != DefaultSkinId)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (remove == null) break;
                owned.Remove(remove);
            }

            string serialized = string.Join(
                ",",
                owned.OrderBy(id => id, StringComparer.Ordinal));
            PlayerPrefs.SetString(KeyOwnedSkins(), serialized);
            if (!owned.Contains(EquippedSkinId))
                PlayerPrefs.SetString(KeyEquippedSkin(), DefaultSkinId);
            PlayerPrefs.Save();
        }

        private static IEnumerable<string> ParseSkinIds(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                yield break;
            foreach (string part in raw.Split(','))
            {
                string skinId = part?.Trim();
                if (!string.IsNullOrEmpty(skinId) && skinId.Length <= 64)
                    yield return skinId;
            }
        }
    }
}
